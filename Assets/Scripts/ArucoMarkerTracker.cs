using UnityEngine;
using UnityEngine.UI;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ArucoModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using System.Collections.Generic;

using ArucoDictionary = OpenCVForUnity.ArucoModule.Dictionary;

/// <summary>
/// Detects ArUco markers by reading directly from the WebCamTexture (via RVMCore)
/// and maps the marker's 2D position onto a World Space Canvas, positioning
/// a target 3D object at the marker location on the video surface.
/// Uses direct CPU pixel access (GetPixels32) to eliminate GPU round-trip latency,
/// with optional low-resolution detection via OpenCV resize for reduced CPU workload.
/// Designed for VR scenes where the video feed is displayed as a world-space surface.
/// </summary>
public class ArucoMarkerTracker : MonoBehaviour
{
    #region Enums

    public enum DictionaryId
    {
        DICT_4X4_50 = Aruco.DICT_4X4_50,
        DICT_4X4_100 = Aruco.DICT_4X4_100,
        DICT_4X4_250 = Aruco.DICT_4X4_250,
        DICT_4X4_1000 = Aruco.DICT_4X4_1000,
        DICT_5X5_50 = Aruco.DICT_5X5_50,
        DICT_5X5_100 = Aruco.DICT_5X5_100,
        DICT_5X5_250 = Aruco.DICT_5X5_250,
        DICT_5X5_1000 = Aruco.DICT_5X5_1000,
        DICT_6X6_50 = Aruco.DICT_6X6_50,
        DICT_6X6_100 = Aruco.DICT_6X6_100,
        DICT_6X6_250 = Aruco.DICT_6X6_250,
        DICT_6X6_1000 = Aruco.DICT_6X6_1000,
        DICT_7X7_50 = Aruco.DICT_7X7_50,
        DICT_7X7_100 = Aruco.DICT_7X7_100,
        DICT_7X7_250 = Aruco.DICT_7X7_250,
        DICT_7X7_1000 = Aruco.DICT_7X7_1000,
        DICT_ARUCO_ORIGINAL = Aruco.DICT_ARUCO_ORIGINAL,
    }

    #endregion

    #region Serialized Fields

    [Header("References")]
    [Tooltip("RVMCore providing the raw WebCamTexture via GetWebCamTexture()")]
    [SerializeField] private RVMCore rvmCore;

    [Tooltip("The 3D object that appears at the marker position on the video surface")]
    [SerializeField] private Transform targetObject;

    [Tooltip("RawImage on a World Space Canvas that displays the video feed")]
    [SerializeField] private RawImage videoDisplay;

    [Header("ArUco Settings")]
    [Tooltip("ArUco dictionary to use for detection")]
    [SerializeField] private DictionaryId dictionaryId = DictionaryId.DICT_4X4_50;

    [Tooltip("Physical marker side length in meters (5cm = 0.05)")]
    [SerializeField] private float markerLength = 0.05f;

    [Tooltip("ID of the marker to track (others are ignored)")]
    [SerializeField] private int targetMarkerId = 0;

    [Header("Performance")]
    [Tooltip("Process detection every N frames (1 = every frame)")]
    [Range(1, 10)]
    [SerializeField] private int processEveryNFrames = 1;

    [Header("Detection Resolution")]
    [Tooltip("Width of the downscaled texture for ArUco detection (0 = use full webcam resolution)")]
    [SerializeField] private int detectionWidth = 320;

    [Tooltip("Height of the downscaled texture for ArUco detection (0 = use full webcam resolution)")]
    [SerializeField] private int detectionHeight = 240;

    [Header("Position Mapping")]
    [Tooltip("Offset from canvas surface toward the viewer (meters). 0 = on surface.")]
    [SerializeField] private float depthOffset = 0.01f;

    [Header("Smoothing")]
    [Tooltip("Enable position smoothing to reduce jitter")]
    [SerializeField] private bool enableSmoothing = true;

    [Tooltip("Smooth time in seconds (lower = faster response, higher = smoother)")]
    [Range(0.01f, 0.5f)]
    [SerializeField] private float smoothTime = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = false;

    #endregion

    #region Private Fields

    // ArUco
    private ArucoDictionary dictionary;
    private DetectorParameters detectorParams;
    private Mat camMatrix;
    private MatOfDouble distCoeffs;

    // Detection output
    private Mat ids;
    private List<Mat> corners;
    private List<Mat> rejectedCorners;

    // WebCamTexture direct-read pipeline
    private Color32[] colors32;   // reusable buffer for GetPixels32 (zero GC)
    private Mat webcamMat;        // full-res RGBA Mat (only when downscaling)
    private Mat rgbaMat;          // detection-resolution RGBA Mat
    private Mat rgbMat;           // detection-resolution RGB Mat

    // Resolution tracking
    private bool useFullResolution;
    private int sourceWidth;
    private int sourceHeight;
    private int imageWidth;   // effective detection width
    private int imageHeight;  // effective detection height

    // State — decoupled detection vs. rendering
    private Vector3 latestTargetPos;   // raw world position from last detection
    private Vector3 smoothVelocity;
    private bool hasDetectedOnce;
    private bool isInitialized;
    private int frameCounter;
    private int framesWithoutMarker;
    private const int HIDE_AFTER_FRAMES = 15;
    private readonly Vector3[] canvasWorldCorners = new Vector3[4]; // cached to avoid GC alloc

    #endregion

    #region Unity Lifecycle

    void Awake()
    {
        AutoWireReferences();
    }

    void Update()
    {
        if (rvmCore == null || targetObject == null || videoDisplay == null) return;

        // Get raw WebCamTexture for direct CPU pixel access (bypasses GPU round-trip)
        WebCamTexture webcam = rvmCore.GetWebCamTexture();
        if (webcam == null || !webcam.isPlaying || webcam.width <= 16) return;

        if (!isInitialized)
        {
            InitializeOpenCV(webcam.width, webcam.height);
            isInitialized = true;
        }
        // Reinitialize if webcam resolution changed (device switch, renegotiation)
        else if (webcam.width != sourceWidth || webcam.height != sourceHeight)
        {
            Debug.Log($"[ArUco] Webcam resolution changed from {sourceWidth}x{sourceHeight} to {webcam.width}x{webcam.height}, reinitializing");
            CleanUp();
            InitializeOpenCV(webcam.width, webcam.height);
            isInitialized = true;
        }

        // Smoothly move object toward latest detected position EVERY frame
        // (decoupled from detection rate for smooth tracking)
        if (hasDetectedOnce && targetObject != null)
        {
            if (enableSmoothing)
            {
                targetObject.position = Vector3.SmoothDamp(
                    targetObject.position, latestTargetPos, ref smoothVelocity, smoothTime);
            }
            else
            {
                targetObject.position = latestTargetPos;
            }
        }

        frameCounter++;
        if (frameCounter % processEveryNFrames != 0) return;

        // Only capture when webcam has delivered a new frame
        if (!webcam.didUpdateThisFrame) return;

        CaptureAndDetect(webcam);
    }

    void OnDestroy()
    {
        CleanUp();
    }

    #endregion

    #region Initialization

    private void InitializeOpenCV(int camWidth, int camHeight)
    {
        sourceWidth = camWidth;
        sourceHeight = camHeight;

        // Determine effective detection resolution (clamp to source to prevent upscaling)
        useFullResolution = (detectionWidth <= 0 || detectionHeight <= 0);
        int effectiveDetW = useFullResolution ? camWidth : Mathf.Min(detectionWidth, camWidth);
        int effectiveDetH = useFullResolution ? camHeight : Mathf.Min(detectionHeight, camHeight);

        imageWidth = effectiveDetW;
        imageHeight = effectiveDetH;

        // Pre-allocate Color32 buffer for zero-GC webcam reads
        colors32 = new Color32[camWidth * camHeight];

        // Full-res Mat for webcam capture (only needed when downscaling to detection res)
        if (!useFullResolution)
        {
            webcamMat = new Mat(camHeight, camWidth, CvType.CV_8UC4);
        }

        // ArUco dictionary and detector
        dictionary = Aruco.getPredefinedDictionary((int)dictionaryId);
        detectorParams = DetectorParameters.create();

        // Camera intrinsics at detection resolution for corner sub-pixel refinement
        int maxD = Mathf.Max(effectiveDetW, effectiveDetH);
        camMatrix = new Mat(3, 3, CvType.CV_64FC1);
        camMatrix.put(0, 0, (double)maxD);
        camMatrix.put(0, 1, 0.0);
        camMatrix.put(0, 2, effectiveDetW / 2.0);
        camMatrix.put(1, 0, 0.0);
        camMatrix.put(1, 1, (double)maxD);
        camMatrix.put(1, 2, effectiveDetH / 2.0);
        camMatrix.put(2, 0, 0.0);
        camMatrix.put(2, 1, 0.0);
        camMatrix.put(2, 2, 1.0);
        distCoeffs = new MatOfDouble(0, 0, 0, 0);

        // Reusable Mats at detection resolution
        rgbaMat = new Mat(effectiveDetH, effectiveDetW, CvType.CV_8UC4);
        rgbMat = new Mat(effectiveDetH, effectiveDetW, CvType.CV_8UC3);

        // Detection output containers
        ids = new Mat();
        corners = new List<Mat>();
        rejectedCorners = new List<Mat>();

        string resInfo = useFullResolution
            ? $"{effectiveDetW}x{effectiveDetH} (full resolution)"
            : $"{effectiveDetW}x{effectiveDetH} (downscaled from {camWidth}x{camHeight})";
        Debug.Log($"[ArUco] Initialized: {resInfo}, dict={(DictionaryId)dictionaryId}, marker={markerLength}m, target ID={targetMarkerId}, direct WebCamTexture read");
    }

    #endregion

    #region Detection Pipeline

    /// <summary>
    /// Reads pixel data directly from the WebCamTexture (CPU-side, zero GPU latency),
    /// optionally downscales via OpenCV resize, then runs the ArUco detection pipeline.
    /// </summary>
    private void CaptureAndDetect(WebCamTexture webcam)
    {
        try
        {
            if (useFullResolution)
            {
                // Full resolution: read webcam pixels directly into detection-sized rgbaMat
                Utils.webCamTextureToMat(webcam, rgbaMat, colors32);
            }
            else
            {
                // Read full-res webcam pixels, then CPU-downscale to detection resolution
                Utils.webCamTextureToMat(webcam, webcamMat, colors32);
                Imgproc.resize(webcamMat, rgbaMat, new Size(imageWidth, imageHeight));
            }

            ProcessDetection();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ArUco] Error processing webcam frame: {e.Message}");
        }
    }

    /// <summary>
    /// Runs the OpenCV ArUco detection pipeline on the CPU-side pixel data.
    /// Marker pixel coordinates are in detection-resolution space; UV normalization
    /// by imageWidth/imageHeight maps them correctly to the canvas regardless of
    /// whether downscaling is enabled.
    /// </summary>
    private void ProcessDetection()
    {
        // Step 1: RGBA -> RGB (ArUco expects 3-channel)
        Imgproc.cvtColor(rgbaMat, rgbMat, Imgproc.COLOR_RGBA2RGB);

        // Step 2: WebCamTexture provides the raw un-mirrored frame.
        // No need to flip for detection — the image is already in its original orientation.
        // The isMirrored flag is only used for UV mapping (display shows mirrored video).
        bool isMirrored = rvmCore != null && rvmCore.mirrorCamera;

        // Step 3: Dispose previous frame's corner Mats, then detect
        DisposeCornersAndRejected();
        Aruco.detectMarkers(rgbMat, dictionary, corners, ids, detectorParams, rejectedCorners, camMatrix, distCoeffs);

        if (ids.total() <= 0)
        {
            OnMarkerLost();
            return;
        }

        // Step 4: Find our target marker and map to canvas position
        for (int i = 0; i < (int)ids.total(); i++)
        {
            int detectedId = (int)ids.get(i, 0)[0];
            if (detectedId == targetMarkerId)
            {
                UpdateTargetPosition(i, isMirrored);
                return;
            }
        }

        // Target marker not in detected set
        OnMarkerLost();
    }

    /// <summary>
    /// Maps the detected marker's 2D pixel center onto the World Space Canvas,
    /// then positions the target object at that world location.
    /// Marker coordinates are in detection-resolution space; dividing by
    /// imageWidth/imageHeight produces correct 0-1 UV regardless of downscaling.
    /// </summary>
    private void UpdateTargetPosition(int markerIndex, bool isMirrored)
    {
        // Get the 4 corners of the detected marker (1x4 Mat, CV_32FC2)
        Mat cornerMat = corners[markerIndex];

        // Calculate 2D center (average of 4 corner points)
        float cx = 0f, cy = 0f;
        for (int i = 0; i < 4; i++)
        {
            double[] pt = cornerMat.get(0, i);
            cx += (float)pt[0];
            cy += (float)pt[1];
        }
        cx /= 4f;
        cy /= 4f;

        // Normalize to 0-1 range (detection-resolution coords / detection dimensions)
        float nx = cx / imageWidth;
        float ny = cy / imageHeight;

        // Detection ran on un-mirrored image, but the video display is mirrored.
        // Flip X so the 3D object matches the mirrored video on the Canvas.
        if (isMirrored)
        {
            nx = 1f - nx;
        }

        // Map normalized image coordinates to Canvas world position
        videoDisplay.rectTransform.GetWorldCorners(canvasWorldCorners);
        // GetWorldCorners: [0]=bottomLeft, [1]=topLeft, [2]=topRight, [3]=bottomRight

        // OpenCV: Y=0 at TOP of image. Unity RectTransform: Y=0 at BOTTOM.
        // Flip Y so top-of-image maps to top-of-canvas.
        float u = nx;
        float v = 1f - ny;

        // Bilinear interpolation across the Canvas surface
        Vector3 bottomPos = Vector3.Lerp(canvasWorldCorners[0], canvasWorldCorners[3], u);
        Vector3 topPos = Vector3.Lerp(canvasWorldCorners[1], canvasWorldCorners[2], u);
        Vector3 canvasPos = Vector3.Lerp(bottomPos, topPos, v);

        // Offset from canvas surface toward the viewer.
        // In World Space Canvas, content faces local -Z, so -forward = toward viewer.
        Vector3 targetPos = canvasPos - videoDisplay.transform.forward * depthOffset;

        // Store latest target position for Update() to interpolate toward every frame.
        // This decouples detection rate from render-rate smoothing.
        latestTargetPos = targetPos;
        hasDetectedOnce = true;
        framesWithoutMarker = 0;

        // Show target when marker is found
        if (targetObject != null && !targetObject.gameObject.activeSelf)
        {
            targetObject.gameObject.SetActive(true);
        }

        if (showDebugLog)
        {
            Debug.Log($"[ArUco] Marker {targetMarkerId} @ pixel({cx:F0},{cy:F0}) -> uv({u:F3},{v:F3}) -> world: {targetObject.position}");
        }
    }

    /// <summary>
    /// Called when the target marker is not detected in the current frame.
    /// Hides the target object after a grace period to avoid flickering.
    /// </summary>
    private void OnMarkerLost()
    {
        framesWithoutMarker++;

        if (framesWithoutMarker >= HIDE_AFTER_FRAMES && targetObject != null && targetObject.gameObject.activeSelf)
        {
            targetObject.gameObject.SetActive(false);
            if (showDebugLog)
            {
                Debug.Log($"[ArUco] Marker {targetMarkerId} lost for {HIDE_AFTER_FRAMES} frames — hiding target");
            }
        }
    }

    #endregion

    #region Cleanup

    private void DisposeCornersAndRejected()
    {
        if (corners != null)
        {
            foreach (Mat c in corners) c?.Dispose();
            corners.Clear();
        }
        if (rejectedCorners != null)
        {
            foreach (Mat c in rejectedCorners) c?.Dispose();
            rejectedCorners.Clear();
        }
    }

    private void CleanUp()
    {
        isInitialized = false;

        camMatrix?.Dispose();
        distCoeffs?.Dispose();
        ids?.Dispose();
        rgbaMat?.Dispose();
        rgbMat?.Dispose();
        webcamMat?.Dispose();
        dictionary?.Dispose();
        detectorParams?.Dispose();

        DisposeCornersAndRejected();

        colors32 = null;
        hasDetectedOnce = false;

        Debug.Log("[ArUco] Cleaned up OpenCV resources");
    }

    #endregion

    #region Auto-Wiring

    /// <summary>
    /// Auto-finds references not assigned in the Inspector.
    /// Configures Canvas for World Space if needed.
    /// Creates a small cube as the target object if none assigned.
    /// </summary>
    private void AutoWireReferences()
    {
        // Auto-find RVMCore
        if (rvmCore == null)
        {
            rvmCore = FindFirstObjectByType<RVMCore>();
            if (rvmCore != null)
                Debug.Log("[ArUco] Auto-found RVMCore");
            else
                Debug.LogError("[ArUco] No RVMCore found in scene! Assign manually.");
        }

        // Auto-find RawImage (video display)
        if (videoDisplay == null)
        {
            videoDisplay = FindFirstObjectByType<RawImage>();
            if (videoDisplay != null)
                Debug.Log("[ArUco] Auto-found RawImage for video display");
            else
                Debug.LogError("[ArUco] No RawImage found! Assign videoDisplay manually.");
        }

        // Configure Canvas: World Space, parented under camera so it follows head movement
        if (videoDisplay != null)
        {
            Canvas canvas = videoDisplay.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                Camera mainCam = Camera.main;

                // Switch to World Space if not already
                if (canvas.renderMode != RenderMode.WorldSpace)
                {
                    canvas.renderMode = RenderMode.WorldSpace;
                    Debug.Log("[ArUco] Canvas render mode -> World Space");
                }

                // Parent under Main Camera so Canvas follows camera movement (VR head-lock)
                if (mainCam != null && canvas.transform.parent != mainCam.transform)
                {
                    canvas.transform.SetParent(mainCam.transform, false);
                    Debug.Log("[ArUco] Canvas parented under Main Camera (follows head)");
                }

                // Scale canvas to ~2m wide
                RectTransform canvasRect = canvas.GetComponent<RectTransform>();
                float canvasPixelWidth = canvasRect.rect.width;
                if (canvasPixelWidth > 0)
                {
                    float desiredWorldWidth = 2f;
                    float scale = desiredWorldWidth / canvasPixelWidth;
                    canvasRect.localScale = Vector3.one * scale;
                }

                // Position 2m in front of camera (local space)
                canvas.transform.localPosition = new Vector3(0f, 0f, 1f);
                canvas.transform.localRotation = Quaternion.identity;

                Debug.Log("[ArUco] Canvas: World Space, ~2m wide, 2m ahead of camera");
            }
        }

        // Ensure Main Camera renders all layers (VR scene needs to see everything)
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.cullingMask = ~0; // Everything
            Debug.Log("[ArUco] Main Camera culling mask set to Everything");
        }

        // Auto-create target object (small cube) if not assigned
        if (targetObject == null)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "ArUco Target";
            cube.transform.localScale = Vector3.one * 0.05f; // 5cm to match marker
            targetObject = cube.transform;
            Debug.Log("[ArUco] Auto-created target cube (5cm)");
        }

        // Hide target until first marker detection
        if (targetObject != null)
        {
            targetObject.gameObject.SetActive(false);
        }
    }

    #endregion
}
