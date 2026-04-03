using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ARMatteCapture.Webcam;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.ArucoModule;

namespace ARMatteCapture.Scanning
{
    /// <summary>
    /// Pure OpenCV ArUco detection + perspective warp + export service.
    /// Processes webcam frames each frame, fires detection events, exports on command.
    /// No knowledge of UI state, lifecycle management, or scene-specific logic.
    /// </summary>
    public class PaperScanDetector : MonoBehaviour
    {
        #region Serialized Fields

        [Header("References")]
        [Tooltip("WebcamSource for paper scan camera")]
        [SerializeField] private WebcamSource webcamSource;

        [Tooltip("Renderer to display live camera feed / warped preview")]
        [SerializeField] private Renderer previewRenderer;

        [Header("Detection")]
        [Tooltip("Number of ArUco markers expected")]
        [SerializeField] private int expectedMarkerCount = 4;

        [Header("Output")]
        [Tooltip("Subfolder in StreamingAssets for saved images")]
        [SerializeField] private string outputFolder = "Images";

        #endregion

        #region Events

        /// <summary>
        /// Fired each frame with the list of detected marker IDs.
        /// Empty list means no markers detected.
        /// </summary>
        public event Action<List<int>> OnMarkersDetected;

        /// <summary>
        /// Fired when an individual marker is first detected (was absent, now present).
        /// </summary>
        public event Action<int> OnMarkerFound;

        /// <summary>
        /// Fired when an individual marker is lost (was present, now absent).
        /// </summary>
        public event Action<int> OnMarkerLost;

        /// <summary>
        /// Fired after a frame is exported successfully, with the file path.
        /// </summary>
        public event Action<string> OnTextureExported;

        #endregion

        #region Properties

        public int ExpectedMarkerCount => expectedMarkerCount;

        #endregion

        #region Private Fields

        private Mat frameMat;
        private Dictionary dictionary;
        private DetectorParameters parameters;
        private Texture2D displayTexture;
        private Color32[] webcamColors;
        private bool _exportRequested;
        private HashSet<int> _previouslyDetected = new HashSet<int>();

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            dictionary = Aruco.getPredefinedDictionary(Aruco.DICT_4X4_1000);
            parameters = DetectorParameters.create();
        }

        void Update()
        {
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

            Utils.webCamTextureToMat(webCamTexture, frameMat, webcamColors);
            ProcessFrame(frameMat);
        }

        void OnDestroy()
        {
            frameMat?.Dispose();
            if (displayTexture != null) Destroy(displayTexture);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Request export of current displayed frame.
        /// The actual export happens at the end of the next ProcessFrame() call.
        /// </summary>
        public void RequestExport()
        {
            _exportRequested = true;
        }

        #endregion

        #region Detection Pipeline

        private void ProcessFrame(Mat frame)
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

            // Build detected ID list
            List<int> detectedIds = new List<int>();
            if (!ids.empty())
            {
                for (int i = 0; i < ids.total(); i++)
                    detectedIds.Add((int)ids.get(i, 0)[0]);
            }

            // Fire per-marker Found/Lost events by diffing with previous frame
            HashSet<int> currentSet = new HashSet<int>(detectedIds);
            foreach (int id in currentSet)
            {
                if (!_previouslyDetected.Contains(id))
                    OnMarkerFound?.Invoke(id);
            }
            foreach (int id in _previouslyDetected)
            {
                if (!currentSet.Contains(id))
                    OnMarkerLost?.Invoke(id);
            }
            _previouslyDetected = currentSet;

            // Fire bulk detection event
            OnMarkersDetected?.Invoke(detectedIds);

            // Warp perspective if all markers detected, otherwise show raw
            Mat displayFrame = frame;
            Mat warped = null;

            if (!ids.empty() && ids.total() == expectedMarkerCount)
            {
                List<Mat> sortedCorners = SortCornersByIDs(corners, ids);
                warped = WarpPerspective(frame.clone(), sortedCorners);
                displayFrame = warped;
            }

            RenderToPreview(displayFrame);

            // Handle export if requested
            if (_exportRequested)
            {
                _exportRequested = false;
                string fileName = "paper-scan-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png";
                ExportFrame(displayFrame, fileName);
            }

            // Cleanup
            warped?.Dispose();
            ids.Dispose();
            foreach (var corner in corners) corner.Dispose();
            foreach (var r in rejectedImgPoints) r.Dispose();
        }

        #endregion

        #region Rendering

        private void RenderToPreview(Mat mat)
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

            if (previewRenderer != null)
                previewRenderer.material.mainTexture = displayTexture;
        }

        #endregion

        #region Perspective Warp (matching legacy WarpPerspective)

        private List<Mat> SortCornersByIDs(List<Mat> corners, Mat ids)
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
        private Mat WarpPerspective(Mat img, List<Mat> corners)
        {
            // Extract corner points (same indices as legacy repo)
            Point topLeft = new Point(corners[0].get(0, 2)[0], corners[0].get(0, 2)[1]);
            Point bottomLeft = new Point(corners[1].get(0, 1)[0], corners[1].get(0, 1)[1]);
            Point topRight = new Point(corners[2].get(0, 3)[0], corners[2].get(0, 3)[1]);
            Point bottomRight = new Point(corners[3].get(0, 0)[0], corners[3].get(0, 0)[1]);

            // Calculate dimensions dynamically
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
        /// </summary>
        private void ExportFrame(Mat inputFrame, string fileName)
        {
            Mat tmpFrame = new Mat(inputFrame.height(), inputFrame.width(), inputFrame.type());

            // Flip horizontally, then rotate (matching legacy repo)
            Core.flip(inputFrame, tmpFrame, 1);
            Core.rotate(tmpFrame, tmpFrame, Core.ROTATE_90_COUNTERCLOCKWISE);
            Core.rotate(tmpFrame, tmpFrame, Core.ROTATE_180);

            string folderPath = Path.Combine(Application.streamingAssetsPath, outputFolder);
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            string filePath = Path.Combine(folderPath, fileName);

            Texture2D saveTex = new Texture2D(tmpFrame.width(), tmpFrame.height(), TextureFormat.RGBA32, false);
            Utils.matToTexture2D(tmpFrame, saveTex);
            byte[] bytes = saveTex.EncodeToPNG();
            File.WriteAllBytes(filePath, bytes);
            Destroy(saveTex);
            tmpFrame.Dispose();

            Debug.Log($"[PaperScanDetector] Saved image to {filePath}");
            OnTextureExported?.Invoke(filePath);
        }

        #endregion
    }
}
