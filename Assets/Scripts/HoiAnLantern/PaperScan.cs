using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ARMatteCapture.Webcam;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.ArucoModule;

namespace HoiAnLantern
{
/// <summary>
/// Self-contained paper scan pipeline matching the legacy repo behavior.
/// Every frame: reads webcam via WebcamSource, runs ArUco detection,
/// renders warped (or raw) preview onto liveFeedRenderer quad,
/// updates MarkerManager directly, triggers scan flow on first detection.
/// Export is requested by MarkerManager when all 4 markers hold for N seconds.
/// </summary>
public class PaperScan : MonoBehaviour
{
    #region Singleton

    public static PaperScan instance;

    #endregion

    #region Serialized Fields

    [Header("References")]
    [Tooltip("WebcamSource for paper scan camera (separate from portrait camera)")]
    [SerializeField] private WebcamSource webcamSource;

    [Tooltip("Renderer (e.g. Quad) on the scan table to display the live paper camera feed")]
    [SerializeField] private Renderer liveFeedRenderer;

    [Tooltip("Reference to CheckARObjectHitTarget for re-scan event")]
    [SerializeField] private CheckARObjectHitTarget checkARObjectHitTarget;

    [Header("Output")]
    [Tooltip("Subfolder in StreamingAssets for saved images")]
    [SerializeField] private string outputFolder = "Images";

    #endregion

    #region Public Fields

    /// <summary>
    /// Whether a new client scan is ready to begin.
    /// Checked by MarkerManager each frame; set to false after export.
    /// Re-enabled by AllowNewUserScan (OnLanternHangSuccess event).
    /// </summary>
    [HideInInspector]
    public bool isNewClientScan = true;

    #endregion

    #region Private Fields

    private bool _hasScanned = false;
    private bool needExport = false;

    // OpenCV ArUco
    private Mat frameMat;
    private Dictionary dictionary;
    private DetectorParameters parameters;

    // Display
    private Texture2D displayTexture;
    private Color32[] webcamColors;

    #endregion

    #region Unity Lifecycle

    void Awake()
    {
        if (instance == null) instance = this;
        else Destroy(this);

        // Create ArUco detector
        dictionary = Aruco.getPredefinedDictionary(Aruco.DICT_4X4_1000);
        parameters = DetectorParameters.create();
    }

    void OnEnable()
    {
        if (checkARObjectHitTarget != null)
            checkARObjectHitTarget.OnLanternHangSuccess += AllowNewUserScan;
    }

    void OnDisable()
    {
        if (checkARObjectHitTarget != null)
            checkARObjectHitTarget.OnLanternHangSuccess -= AllowNewUserScan;
    }

    void Update()
    {
        if (!isNewClientScan) return;
        if (webcamSource == null) return;

        WebCamTexture webCamTexture = webcamSource.GetTexture();
        if (webCamTexture == null || !webCamTexture.isPlaying || !webCamTexture.didUpdateThisFrame) return;

        // Initialize frameMat on first use or resolution change
        if (frameMat == null || frameMat.height() != webCamTexture.height || frameMat.width() != webCamTexture.width)
        {
            frameMat?.Dispose();
            frameMat = new Mat(webCamTexture.height, webCamTexture.width, CvType.CV_8UC4);
        }

        // Initialize color buffer
        if (webcamColors == null || webcamColors.Length != webCamTexture.width * webCamTexture.height)
            webcamColors = new Color32[webCamTexture.width * webCamTexture.height];

        // Convert webcam texture to OpenCV Mat (match old repo: flip vertically)
        Utils.webCamTextureToMat(webCamTexture, frameMat, webcamColors);

        // Process the frame
        CropFrame(frameMat);
    }

    void OnDestroy()
    {
        frameMat?.Dispose();
        if (displayTexture != null) Destroy(displayTexture);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Request an export/capture. Called by MarkerManager when all 4 markers are found for sufficient time.
    /// The actual export happens at the end of the next CropFrame() call.
    /// </summary>
    public void RequestExport()
    {
        needExport = true;
    }

    /// <summary>
    /// Reset the scan state to allow a new scan cycle.
    /// Call this when the experience needs to capture another image.
    /// </summary>
    public void ResetScan()
    {
        isNewClientScan = true;
        _hasScanned = false;
    }

    #endregion

    #region Detection & Rendering Pipeline

    /// <summary>
    /// Core pipeline matching legacy repo: detect markers, update MarkerManager,
    /// warp preview if 4 markers found, handle export.
    /// </summary>
    void CropFrame(Mat frame)
    {
        // Convert to grayscale for detection
        Mat gray = new Mat();
        Imgproc.cvtColor(frame, gray, Imgproc.COLOR_RGBA2GRAY);

        // Detect markers
        List<Mat> corners = new List<Mat>();
        Mat ids = new Mat();
        List<Mat> rejectedImgPoints = new List<Mat>();

        Aruco.detectMarkers(gray, dictionary, corners, ids, parameters, rejectedImgPoints);
        gray.Dispose();

        // If any marker detected
        if (!ids.empty())
        {
            // Trigger scan flow on first detection (like legacy repo)
            if (!_hasScanned)
            {
                _hasScanned = true;
                ScanLanternManager.Instance?.OnScanningLanternTexture();
            }

            // Update MarkerManager directly (like legacy repo)
            if (MarkerManager.instance != null)
            {
                List<int> detectedIds = new List<int>();
                for (int i = 0; i < ids.total(); i++)
                {
                    detectedIds.Add((int)ids.get(i, 0)[0]);
                }
                for (int i = 0; i < MarkerManager.instance.GetMarkerCount(); i++)
                {
                    if (detectedIds.Contains(i))
                        MarkerManager.instance.FoundMarker(i);
                    else
                        MarkerManager.instance.LostMarker(i);
                }
            }
        }
        else if (MarkerManager.instance != null)
        {
            // No markers detected — mark all as lost so MarkerManager timer resets
            for (int i = 0; i < MarkerManager.instance.GetMarkerCount(); i++)
            {
                MarkerManager.instance.LostMarker(i);
            }
        }

        // Warp perspective if all 4 markers detected, otherwise show raw
        Mat displayFrame = frame;
        Mat warped = null;

        if (!ids.empty() && ids.total() == 4)
        {
            List<Mat> sortedCorners = SortCornersByIDs(corners, ids);
            warped = WarpPerspective(frame.clone(), sortedCorners);
            displayFrame = warped;
        }

        renderToScreen(displayFrame);

        // Handle export if requested
        if (needExport)
        {
            needExport = false;
            ExportCurrentFrame(displayFrame,
                "paper-scan-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
            isNewClientScan = false;
        }

        // Cleanup
        warped?.Dispose();
        ids.Dispose();
        foreach (var corner in corners) corner.Dispose();
        foreach (var r in rejectedImgPoints) r.Dispose();
    }

    #endregion

    #region Rendering

    /// <summary>
    /// Render a Mat onto the liveFeedRenderer quad (matching legacy renderToScreen).
    /// </summary>
    void renderToScreen(Mat mat)
    {
        if (displayTexture == null)
        {
            displayTexture = new Texture2D(mat.width(), mat.height(), TextureFormat.RGBA32, false);
        }
        else if (displayTexture.width != mat.width() || displayTexture.height != mat.height())
        {
            displayTexture.Reinitialize(mat.width(), mat.height());
        }

        Utils.matToTexture2D(mat, displayTexture);

        if (liveFeedRenderer != null)
            liveFeedRenderer.material.mainTexture = displayTexture;
    }

    #endregion

    #region Perspective Warp (matching legacy WarpPerspective)

    /// <summary>
    /// Sort detected marker corners by their ArUco IDs (ascending).
    /// </summary>
    List<Mat> SortCornersByIDs(List<Mat> corners, Mat ids)
    {
        List<KeyValuePair<int, Mat>> cornerWithIDs = new List<KeyValuePair<int, Mat>>();

        for (int i = 0; i < ids.total(); i++)
        {
            cornerWithIDs.Add(new KeyValuePair<int, Mat>((int)ids.get(i, 0)[0], corners[i]));
        }

        cornerWithIDs.Sort((a, b) => a.Key.CompareTo(b.Key));

        List<Mat> sortedCorners = new List<Mat>();
        foreach (var pair in cornerWithIDs)
        {
            sortedCorners.Add(pair.Value);
        }

        return sortedCorners;
    }

    /// <summary>
    /// Perspective warp using specific marker corners (matching legacy repo exactly).
    /// marker0 corner[2] = inner corner of top-left marker
    /// marker1 corner[1] = inner corner of top-right marker
    /// marker2 corner[3] = inner corner of bottom-left marker
    /// marker3 corner[0] = inner corner of bottom-right marker
    /// </summary>
    Mat WarpPerspective(Mat img, List<Mat> corners)
    {
        // Extract corner points (same indices as legacy repo)
        Point topLeft = new Point(corners[0].get(0, 2)[0], corners[0].get(0, 2)[1]);
        Point bottomLeft = new Point(corners[1].get(0, 1)[0], corners[1].get(0, 1)[1]);
        Point topRight = new Point(corners[2].get(0, 3)[0], corners[2].get(0, 3)[1]);
        Point bottomRight = new Point(corners[3].get(0, 0)[0], corners[3].get(0, 0)[1]);

        // Calculate dimensions dynamically (like legacy repo)
        double bottomWidth = Math.Sqrt(Math.Pow(bottomRight.x - bottomLeft.x, 2) + Math.Pow(bottomRight.y - bottomLeft.y, 2));
        double topWidth = Math.Sqrt(Math.Pow(topRight.x - topLeft.x, 2) + Math.Pow(topRight.y - topLeft.y, 2));
        double rightHeight = Math.Sqrt(Math.Pow(topRight.x - bottomRight.x, 2) + Math.Pow(topRight.y - bottomRight.y, 2));
        double leftHeight = Math.Sqrt(Math.Pow(topLeft.x - bottomLeft.x, 2) + Math.Pow(topLeft.y - bottomLeft.y, 2));

        double maxWidth = Math.Max(bottomWidth, topWidth);
        double maxHeight = Math.Max(rightHeight, leftHeight);

        // Source and destination points for perspective transform
        MatOfPoint2f srcPoints = new MatOfPoint2f(
            new Point(topLeft.x, topLeft.y),
            new Point(topRight.x, topRight.y),
            new Point(bottomLeft.x, bottomLeft.y),
            new Point(bottomRight.x, bottomRight.y)
        );

        MatOfPoint2f dstPoints = new MatOfPoint2f(
            new Point(0, 0),
            new Point(maxWidth, 0),
            new Point(0, maxHeight),
            new Point(maxWidth, maxHeight)
        );

        Mat matrix = Imgproc.getPerspectiveTransform(srcPoints, dstPoints);
        Mat outputImage = new Mat();
        Imgproc.warpPerspective(img, outputImage, matrix, new Size(maxWidth, maxHeight));

        // Cleanup
        img.Dispose();
        srcPoints.Dispose();
        dstPoints.Dispose();
        matrix.Dispose();

        return outputImage;
    }

    #endregion

    #region Export (matching legacy ExportCurrentFrame + SaveImage)

    /// <summary>
    /// Export the current frame: flip + rotate to correct orientation, save as PNG.
    /// Matching legacy repo transforms exactly.
    /// </summary>
    void ExportCurrentFrame(Mat inputFrame, string fileName)
    {
        Mat tmpFrame = new Mat(inputFrame.height(), inputFrame.width(), inputFrame.type());

        // Flip horizontally, then rotate (matching legacy repo)
        Core.flip(inputFrame, tmpFrame, 1);
        Core.rotate(tmpFrame, tmpFrame, Core.ROTATE_90_COUNTERCLOCKWISE);
        Core.rotate(tmpFrame, tmpFrame, Core.ROTATE_180);

        SaveImage(tmpFrame, fileName);
        tmpFrame.Dispose();
    }

    /// <summary>
    /// Save Mat as PNG to StreamingAssets and notify ScanLanternManager.
    /// </summary>
    void SaveImage(Mat image, string filename)
    {
        string folderPath = Path.Combine(Application.streamingAssetsPath, outputFolder);
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        string filePath = Path.Combine(folderPath, filename);

        Texture2D saveTex = new Texture2D(image.width(), image.height(), TextureFormat.RGBA32, false);
        Utils.matToTexture2D(image, saveTex);
        byte[] bytes = saveTex.EncodeToPNG();
        File.WriteAllBytes(filePath, bytes);
        Destroy(saveTex);

        Debug.Log($"[PaperScan] Saved image to {filePath}");

        // Notify scan complete (matching legacy: SaveImage calls OnScanningSuccess)
        ScanLanternManager.Instance?.OnScanningSuccess();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Allow a new user scan cycle. Called when a lantern is hung successfully.
    /// Matching legacy AllowNewUserScan.
    /// </summary>
    void AllowNewUserScan()
    {
        isNewClientScan = true;
        _hasScanned = false;
    }

    #endregion
}
} // namespace HoiAnLantern
