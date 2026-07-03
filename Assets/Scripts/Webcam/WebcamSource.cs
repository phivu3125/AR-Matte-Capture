using UnityEngine;

namespace ARMatteCapture.Webcam
{
    /// <summary>
    /// Wraps a single WebCamTexture with Inspector-configurable settings.
    /// Provides a clean abstraction for dual-webcam pipeline where each
    /// source (portrait RVM, paper scan) has its own WebcamSource instance.
    /// </summary>
    public class WebcamSource : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Device Selection")]
        [Tooltip("Webcam device name (leave empty for auto-select by index)")]
        [SerializeField] private string deviceName = "";

        [Tooltip("Device index fallback when deviceName is empty (0 = first camera)")]
        [SerializeField] private int deviceIndex = 0;

        [Header("Resolution")]
        [Tooltip("Requested webcam width")]
        [SerializeField] private int width = 1920;

        [Tooltip("Requested webcam height")]
        [SerializeField] private int height = 1080;

        [Tooltip("Requested frames per second")]
        [SerializeField] private int fps = 30;

        [Header("Options")]
        [Tooltip("Mirror the webcam image horizontally")]
        [SerializeField] private bool mirrorHorizontal = false;

        [Tooltip("Automatically start the webcam on Awake")]
        [SerializeField] private bool playOnAwake = true;

        #endregion

        #region Private Fields

        private WebCamTexture webcamTexture;
        private bool isCleanedUp = false;

        #endregion

        #region Public Properties

        /// <summary>Whether the webcam is currently playing.</summary>
        public bool IsPlaying => webcamTexture != null && webcamTexture.isPlaying;

        /// <summary>Whether the webcam texture was updated this frame.</summary>
        public bool DidUpdateThisFrame => webcamTexture != null && webcamTexture.didUpdateThisFrame;

        /// <summary>The resolved device name of the active webcam.</summary>
        public string DeviceName => webcamTexture != null ? webcamTexture.deviceName : deviceName;

        /// <summary>Whether mirroring is enabled for this source.</summary>
        public bool MirrorHorizontal => mirrorHorizontal;

        /// <summary>Actual width of the webcam texture (available after Play).</summary>
        public int ActualWidth => webcamTexture != null ? webcamTexture.width : 0;

        /// <summary>Actual height of the webcam texture (available after Play).</summary>
        public int ActualHeight => webcamTexture != null ? webcamTexture.height : 0;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (playOnAwake)
            {
                Play();
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets the underlying WebCamTexture for direct pixel access.
        /// Returns null if webcam is not initialized.
        /// </summary>
        public WebCamTexture GetTexture()
        {
            return webcamTexture;
        }

        /// <summary>
        /// Initializes and starts the webcam.
        /// </summary>
        public void Play()
        {
            if (webcamTexture != null && webcamTexture.isPlaying)
                return;

            if (webcamTexture == null)
            {
                if (!InitializeWebcam())
                    return;
            }

            webcamTexture.Play();
            Debug.Log($"[WebcamSource] Playing: {webcamTexture.deviceName} ({width}x{height} @ {fps}fps requested)");
        }

        /// <summary>
        /// Stops the webcam without destroying it.
        /// </summary>
        public void Stop()
        {
            if (webcamTexture != null && webcamTexture.isPlaying)
            {
                webcamTexture.Stop();
                Debug.Log($"[WebcamSource] Stopped: {webcamTexture.deviceName}");
            }
        }

        /// <summary>
        /// Stops, destroys, and reinitializes the webcam.
        /// </summary>
        public void Restart()
        {
            Cleanup();
            isCleanedUp = false;
            Play();
        }

        /// <summary>
        /// Change the target device at runtime. Restarts if currently playing.
        /// </summary>
        public void SetDevice(string newDeviceName)
        {
            deviceName = newDeviceName;
            deviceIndex = -1; // force name-based resolve
            if (webcamTexture != null) Restart();
        }

        /// <summary>
        /// Change the target device by index at runtime. Restarts if currently playing.
        /// </summary>
        public void SetDeviceIndex(int newIndex)
        {
            deviceName = "";
            deviceIndex = newIndex;
            if (webcamTexture != null) Restart();
        }

        /// <summary>
        /// Change resolution and FPS at runtime. Restarts if currently playing.
        /// </summary>
        public void SetResolution(int newWidth, int newHeight, int newFps)
        {
            width = newWidth;
            height = newHeight;
            fps = newFps;
            if (webcamTexture != null) Restart();
        }

        /// <summary>
        /// Set device, resolution, and FPS in one call with a single restart.
        /// Avoids double-restart timing issues when changing multiple settings.
        /// </summary>
        public void Configure(string newDeviceName, int newWidth, int newHeight, int newFps)
        {
            deviceName = newDeviceName;
            deviceIndex = -1;
            width = newWidth;
            height = newHeight;
            fps = newFps;
            Restart();
        }

        /// <summary>Current requested width.</summary>
        public int RequestedWidth => width;
        /// <summary>Current requested height.</summary>
        public int RequestedHeight => height;
        /// <summary>Current requested FPS.</summary>
        public int RequestedFPS => fps;
        /// <summary>Current device index.</summary>
        public int DeviceIndex => deviceIndex;

        #endregion

        #region Private Methods

        private bool InitializeWebcam()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Debug.LogError("[WebcamSource] No webcam devices found!");
                return false;
            }

            string resolvedName = ResolveDeviceName(devices);
            if (string.IsNullOrEmpty(resolvedName))
            {
                Debug.LogError($"[WebcamSource] Could not resolve webcam device (name='{deviceName}', index={deviceIndex})");
                return false;
            }

            webcamTexture = new WebCamTexture(resolvedName, width, height, fps)
            {
                filterMode = FilterMode.Bilinear
            };

            Debug.Log($"[WebcamSource] Initialized: {resolvedName} ({width}x{height} @ {fps}fps)");
            return true;
        }

        private string ResolveDeviceName(WebCamDevice[] devices)
        {
            // Priority 1: explicit device name
            if (!string.IsNullOrEmpty(deviceName))
            {
                foreach (var device in devices)
                {
                    if (device.name == deviceName)
                        return device.name;
                }
                Debug.LogWarning($"[WebcamSource] Device '{deviceName}' not found, falling back to index {deviceIndex}");
            }

            // Priority 2: device index
            if (deviceIndex >= 0 && deviceIndex < devices.Length)
            {
                return devices[deviceIndex].name;
            }

            // Fallback: first device
            Debug.LogWarning($"[WebcamSource] Index {deviceIndex} out of range, using first device");
            return devices[0].name;
        }

        private void Cleanup()
        {
            if (isCleanedUp) return;
            isCleanedUp = true;

            if (webcamTexture != null)
            {
                if (webcamTexture.isPlaying)
                    webcamTexture.Stop();
                Destroy(webcamTexture);
                webcamTexture = null;
            }
        }

        #endregion
    }
}
