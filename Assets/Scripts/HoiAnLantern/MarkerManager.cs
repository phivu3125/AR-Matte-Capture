using System.Collections.Generic;
using UnityEngine;

namespace HoiAnLantern
{
    /// <summary>
    /// Reduced facade. Timer logic moved to ScanSessionController.
    /// UI logic moved to MarkerScanPresenter.
    /// Kept for MarkerBorder.GetGradientDuration() and scene serialized refs.
    /// </summary>
    public class MarkerManager : MonoBehaviour
    {
        public static MarkerManager instance;

        [SerializeField] float gradientDuration = 3f;
        int markerCount = 4;

        // Scene-serialized fields kept to avoid Inspector reference loss
        [SerializeField] GameObject marker0;
        [SerializeField] MarkerBorder markerBorder0;
        [SerializeField] GameObject marker1;
        [SerializeField] MarkerBorder markerBorder1;
        [SerializeField] GameObject marker2;
        [SerializeField] MarkerBorder markerBorder2;
        [SerializeField] GameObject marker3;
        [SerializeField] MarkerBorder markerBorder3;
        [SerializeField] List<Texture2D> missingMaterials;
        [SerializeField] List<Texture2D> detectedMaterials;
        [SerializeField] float timeBeforeScan = 3f;

        void Awake()
        {
            if (instance == null) instance = this;
            else Destroy(this);
        }

        public float GetGradientDuration() => gradientDuration;
        public int GetMarkerCount() => markerCount;
    }
}
