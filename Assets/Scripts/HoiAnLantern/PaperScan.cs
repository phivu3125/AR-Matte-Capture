using System.Collections.Generic;
using UnityEngine;
using ARMatteCapture.Scanning;

namespace HoiAnLantern
{
/// <summary>
/// Thin orchestrator wiring scanning modules together.
/// Detection → PaperScanDetector, State → ScanSessionController,
/// UI → MarkerScanPresenter, Presentation → ScanLanternManager.
/// </summary>
public class PaperScan : MonoBehaviour
{
    public static PaperScan instance;

    [Header("Scanning Modules")]
    [SerializeField] private PaperScanDetector detector;
    [SerializeField] private ScanSessionController sessionController;
    [SerializeField] private MarkerScanPresenter markerPresenter;

    [HideInInspector] public bool isNewClientScan = true;

    /// <summary>
    /// Cached texture from the last export, passed directly to avoid disk I/O.
    /// Ownership transfers to ScanLanternManager on success.
    /// </summary>
    private Texture2D _lastCapturedTexture;

    /// <summary>
    /// Prevents camera from moving multiple times if markers are lost and regained
    /// within the same scan cycle.
    /// </summary>
    private bool _cameraMoved;

    void Awake()
    {
        if (instance == null) instance = this;
        else Destroy(this);
    }

    void OnEnable()
    {
        if (detector != null)
        {
            detector.OnMarkersDetected += OnDetected;
            detector.OnMarkerFound += OnFound;
            detector.OnMarkerLost += OnLost;
            detector.OnTextureExported += OnExported;
            detector.OnTextureCaptured += OnTextureCaptured;
        }
        if (sessionController != null)
        {
            sessionController.OnScanStarted += OnStarted;
            sessionController.OnExportRequested += OnExportReq;
            sessionController.OnScanSuccess += OnSuccess;
            sessionController.OnScanReset += OnReset;
            sessionController.OnStateChanged += OnState;
        }
    }

    void OnDisable()
    {
        if (detector != null)
        {
            detector.OnMarkersDetected -= OnDetected;
            detector.OnMarkerFound -= OnFound;
            detector.OnMarkerLost -= OnLost;
            detector.OnTextureExported -= OnExported;
            detector.OnTextureCaptured -= OnTextureCaptured;
        }
        if (sessionController != null)
        {
            sessionController.OnScanStarted -= OnStarted;
            sessionController.OnExportRequested -= OnExportReq;
            sessionController.OnScanSuccess -= OnSuccess;
            sessionController.OnScanReset -= OnReset;
            sessionController.OnStateChanged -= OnState;
        }
    }

    public void RequestExport() => detector?.RequestExport();
    public void ResetScan() => sessionController?.ResetForNewScan();

    // Detector → Controller
    void OnDetected(List<int> ids) => sessionController?.ReportMarkerStatus(ids.Count, detector.ExpectedMarkerCount);
    void OnFound(int id) => markerPresenter?.OnMarkerFound(id);
    void OnLost(int id) => markerPresenter?.OnMarkerLost(id);
    void OnExported(string _) => sessionController?.NotifyExportComplete();

    void OnTextureCaptured(Texture2D tex)
    {
        if (_lastCapturedTexture != null)
            Destroy(_lastCapturedTexture);
        _lastCapturedTexture = tex;
    }

    // Controller → Presentation
    void OnStarted() { } // Camera no longer moves on first marker; waits for all markers
    void OnExportReq() => detector?.RequestExport();
    void OnSuccess()
    {
        ScanLanternManager.Instance?.OnScanningSuccess(_lastCapturedTexture);
        _lastCapturedTexture = null; // ownership transferred
    }

    void OnReset()
    {
        isNewClientScan = true;
        _cameraMoved = false;
        markerPresenter?.ResetAll();
    }

    void OnState(ScanState oldState, ScanState newState)
    {
        bool active = newState == ScanState.Idle || newState == ScanState.Scanning
            || newState == ScanState.AllMarkersDetected || newState == ScanState.Exporting;
        if (detector != null) detector.enabled = active;
        isNewClientScan = (newState == ScanState.Idle);

        // Move camera to scan table only when all 4 markers are first detected
        if (newState == ScanState.AllMarkersDetected && !_cameraMoved)
        {
            _cameraMoved = true;
            ScanLanternManager.Instance?.OnScanningLanternTexture();
        }
    }
}
}
