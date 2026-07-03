using UnityEngine;
using HoiAnLantern;

namespace ARMatteCapture.Scanning
{
    /// <summary>
    /// UI-only marker status display.
    /// Subscribes to PaperScanDetector Found/Lost events and updates
    /// visual indicators (marker quads + MarkerBorder color animations).
    /// No detection logic, no state management, no OpenCV.
    /// </summary>
    public class MarkerScanPresenter : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Marker Visuals")]
        [SerializeField] private GameObject marker0;
        [SerializeField] private MarkerBorder markerBorder0;
        [SerializeField] private GameObject marker1;
        [SerializeField] private MarkerBorder markerBorder1;
        [SerializeField] private GameObject marker2;
        [SerializeField] private MarkerBorder markerBorder2;
        [SerializeField] private GameObject marker3;
        [SerializeField] private MarkerBorder markerBorder3;

        [Header("Textures")]
        [SerializeField] private Texture2D[] missingTextures;
        [SerializeField] private Texture2D[] detectedTextures;

        [Header("Animation")]
        [SerializeField] private float gradientDuration = 3f;

        #endregion

        #region Private Fields

        private int markerCount = 4;
        private readonly bool[] markerFound = new bool[4];
        private GameObject[] markers;
        private MarkerBorder[] borders;

        #endregion

        #region Properties

        public float GradientDuration => gradientDuration;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            markers = new[] { marker0, marker1, marker2, marker3 };
            borders = new[] { markerBorder0, markerBorder1, markerBorder2, markerBorder3 };
        }

        #endregion

        #region Public API

        /// <summary>
        /// Called when an individual marker is detected.
        /// Updates visual to "detected" state.
        /// </summary>
        public void OnMarkerFound(int markerId)
        {
            if (markerId < 0 || markerId >= markerCount) return;
            if (markers == null) return;
            if (markerFound[markerId]) return; // already shown as found

            markerFound[markerId] = true;

            if (markers[markerId] != null && detectedTextures != null && markerId < detectedTextures.Length)
            {
                Renderer rend = markers[markerId].GetComponent<Renderer>();
                if (rend != null) rend.material.mainTexture = detectedTextures[markerId];
            }

            if (borders[markerId] != null)
                borders[markerId].SetDetected();
        }

        /// <summary>
        /// Called when an individual marker is lost.
        /// Updates visual to "missing" state.
        /// </summary>
        public void OnMarkerLost(int markerId)
        {
            if (markerId < 0 || markerId >= markerCount) return;
            if (markers == null) return;
            if (!markerFound[markerId]) return; // already shown as missing

            markerFound[markerId] = false;

            if (markers[markerId] != null && missingTextures != null && markerId < missingTextures.Length)
            {
                Renderer rend = markers[markerId].GetComponent<Renderer>();
                if (rend != null) rend.material.mainTexture = missingTextures[markerId];
            }

            if (borders[markerId] != null)
                borders[markerId].SetMissing();
        }

        /// <summary>
        /// Reset all markers to "missing" state. Called when scan cycle resets.
        /// </summary>
        public void ResetAll()
        {
            for (int i = 0; i < markerCount; i++)
            {
                // Force the update by setting to true first, then calling LostMarker
                markerFound[i] = true;
                OnMarkerLost(i);
            }
        }

        /// <summary>
        /// Check if a specific marker is currently shown as found.
        /// </summary>
        public bool IsMarkerShownAsFound(int markerId)
        {
            if (markerId < 0 || markerId >= markerCount) return false;
            return markerFound[markerId];
        }

        #endregion
    }
}
