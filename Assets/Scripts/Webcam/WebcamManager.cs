using System.Collections.Generic;
using UnityEngine;

namespace ARMatteCapture.Webcam
{
    /// <summary>
    /// Singleton that manages dual-webcam sources, mapping each WebcamRole
    /// to a WebcamSource instance. Enumerates available devices on Awake
    /// and logs bindings.
    /// </summary>
    public class WebcamManager : MonoBehaviour
    {
        #region Singleton

        public static WebcamManager Instance { get; private set; }

        #endregion

        #region Serialized Fields

        [Header("Webcam Sources")]
        [Tooltip("WebcamSource for portrait/RVM pipeline (background removal + lantern tracking)")]
        [SerializeField] private WebcamSource portraitSource;

        [Tooltip("WebcamSource for paper scan pipeline (4-corner ArUco marker detection)")]
        [SerializeField] private WebcamSource paperScanSource;

        #endregion

        #region Private Fields

        private Dictionary<WebcamRole, WebcamSource> sourceMap;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[WebcamManager] Duplicate instance destroyed");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            InitializeSourceMap();
            LogAvailableDevices();
            LogBindings();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets the WebcamSource assigned to the specified role.
        /// Returns null if no source is assigned for that role.
        /// </summary>
        public WebcamSource GetSource(WebcamRole role)
        {
            if (sourceMap != null && sourceMap.TryGetValue(role, out var source))
                return source;

            Debug.LogWarning($"[WebcamManager] No source assigned for role: {role}");
            return null;
        }

        #endregion

        #region Private Methods

        private void InitializeSourceMap()
        {
            sourceMap = new Dictionary<WebcamRole, WebcamSource>();

            if (portraitSource != null)
                sourceMap[WebcamRole.Portrait] = portraitSource;
            else
                Debug.LogWarning("[WebcamManager] Portrait source not assigned");

            if (paperScanSource != null)
                sourceMap[WebcamRole.PaperScan] = paperScanSource;
            else
                Debug.LogWarning("[WebcamManager] PaperScan source not assigned");
        }

        private void LogAvailableDevices()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            Debug.Log($"[WebcamManager] Available devices: {devices.Length}");
            for (int i = 0; i < devices.Length; i++)
            {
                Debug.Log($"[WebcamManager]   [{i}] {devices[i].name} (front={devices[i].isFrontFacing})");
            }
        }

        private void LogBindings()
        {
            foreach (var kvp in sourceMap)
            {
                Debug.Log($"[WebcamManager] {kvp.Key} -> {kvp.Value.DeviceName}");
            }

            // Warn if two roles map to the same device
            if (portraitSource != null && paperScanSource != null)
            {
                string portraitDevice = portraitSource.DeviceName;
                string paperDevice = paperScanSource.DeviceName;
                if (!string.IsNullOrEmpty(portraitDevice) && portraitDevice == paperDevice)
                {
                    Debug.LogWarning($"[WebcamManager] WARNING: Both Portrait and PaperScan use the same device '{portraitDevice}'. This may cause conflicts.");
                }
            }
        }

        #endregion
    }
}
