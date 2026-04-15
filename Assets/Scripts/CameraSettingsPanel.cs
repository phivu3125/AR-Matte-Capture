using UnityEngine;
using ARMatteCapture.Webcam;

/// <summary>
/// Debug IMGUI panel for configuring webcam devices at runtime.
/// Press Tab to toggle visibility.
/// Attach to any active GameObject in the scene.
/// </summary>
public class CameraSettingsPanel : MonoBehaviour
{
    #region Serialized Fields

    [Header("Toggle")]
    [Tooltip("Key to toggle the settings panel")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    [Header("Panel")]
    [Tooltip("Panel width in pixels")]
    [SerializeField] private float panelWidth = 420f;

    [Tooltip("Panel height in pixels")]
    [SerializeField] private float panelHeight = 600f;

    #endregion

    #region Private Fields

    private bool _showPanel;
    private WebCamDevice[] _devices;
    private string[] _deviceNames;
    private Vector2 _scrollPos;

    // Cached references
    private WebcamSource _portraitSource;
    private WebcamSource _paperScanSource;

    // Resolution presets
    private static readonly string[] ResolutionLabels = {
        "640×480", "1280×720", "1920×1080", "2560×1440"
    };
    private static readonly int[][] ResolutionValues = {
        new[] { 640, 480 },
        new[] { 1280, 720 },
        new[] { 1920, 1080 },
        new[] { 2560, 1440 }
    };

    // Per-source state
    private int _portraitDeviceIdx;
    private int _portraitResIdx = 2; // default 1920×1080
    private int _portraitFps = 30;

    private int _paperScanDeviceIdx;
    private int _paperScanResIdx = 2;
    private int _paperScanFps = 30;

    // GUI style cache
    private GUIStyle _headerStyle;
    private GUIStyle _subHeaderStyle;
    private GUIStyle _boxStyle;
    private bool _stylesInitialized;

    #endregion

    #region Unity Lifecycle

    void Start()
    {
        RefreshDeviceList();
        CacheSources();
        SyncStateFromSources();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            _showPanel = !_showPanel;
            if (_showPanel)
            {
                RefreshDeviceList();
                CacheSources();
                SyncStateFromSources();
            }
        }
    }

    void OnGUI()
    {
        if (!_showPanel) return;

        InitStyles();

        float x = (Screen.width - panelWidth) / 2f;
        float y = (Screen.height - panelHeight) / 2f;
        Rect panelRect = new Rect(x, y, panelWidth, panelHeight);

        GUI.Box(panelRect, "");

        GUILayout.BeginArea(panelRect);
        _scrollPos = GUILayout.BeginScrollView(_scrollPos);

        GUILayout.Space(8);
        GUILayout.Label("⚙ Camera Settings", _headerStyle);
        GUILayout.Space(4);

        // Device list
        DrawDeviceList();

        GUILayout.Space(12);

        // Portrait source
        DrawSourceSection(
            "Portrait (RVM)", _portraitSource,
            ref _portraitDeviceIdx, ref _portraitResIdx, ref _portraitFps
        );

        GUILayout.Space(12);

        // Paper Scan source
        DrawSourceSection(
            "Paper Scan", _paperScanSource,
            ref _paperScanDeviceIdx, ref _paperScanResIdx, ref _paperScanFps
        );

        GUILayout.Space(12);

        // Refresh button
        if (GUILayout.Button("🔄 Refresh Device List", GUILayout.Height(30)))
        {
            RefreshDeviceList();
            CacheSources();
            SyncStateFromSources();
        }

        GUILayout.Space(4);

        // Close
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.Label($"Press [{toggleKey}] to close", GUI.skin.label);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    #endregion

    #region Drawing

    private void DrawDeviceList()
    {
        GUILayout.Label("Available Cameras", _subHeaderStyle);

        if (_devices == null || _devices.Length == 0)
        {
            GUILayout.Label("  No webcam devices found.");
            return;
        }

        for (int i = 0; i < _devices.Length; i++)
        {
            string front = _devices[i].isFrontFacing ? " (front)" : "";
            GUILayout.Label($"  [{i}] {_devices[i].name}{front}");
        }
    }

    private void DrawSourceSection(
        string label, WebcamSource source,
        ref int deviceIdx, ref int resIdx, ref int fps)
    {
        GUILayout.Box("", _boxStyle, GUILayout.ExpandWidth(true), GUILayout.Height(2));
        GUILayout.Label(label, _subHeaderStyle);

        if (source == null)
        {
            GUILayout.Label("  (not assigned in WebcamManager)");
            return;
        }

        // Status
        string status = source.IsPlaying ? "<color=green>● Playing</color>" : "<color=red>● Stopped</color>";
        var richStyle = new GUIStyle(GUI.skin.label) { richText = true };
        GUILayout.Label($"  Status: {status}  |  Actual: {source.ActualWidth}×{source.ActualHeight}", richStyle);
        GUILayout.Label($"  Device: {source.DeviceName}");

        GUILayout.Space(4);

        // Device selection
        if (_devices != null && _devices.Length > 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("  Device:", GUILayout.Width(70));
            int newIdx = GUILayout.SelectionGrid(deviceIdx, _deviceNames, 1);
            GUILayout.EndHorizontal();

            if (newIdx != deviceIdx)
            {
                deviceIdx = newIdx;
            }
        }

        GUILayout.Space(4);

        // Resolution selection
        GUILayout.BeginHorizontal();
        GUILayout.Label("  Resolution:", GUILayout.Width(80));
        resIdx = GUILayout.SelectionGrid(resIdx, ResolutionLabels, ResolutionLabels.Length);
        GUILayout.EndHorizontal();

        // FPS
        GUILayout.BeginHorizontal();
        GUILayout.Label("  FPS:", GUILayout.Width(80));
        if (GUILayout.Button("15")) fps = 15;
        if (GUILayout.Button("30")) fps = 30;
        if (GUILayout.Button("60")) fps = 60;
        GUILayout.Label($"  → {fps}", GUILayout.Width(50));
        GUILayout.EndHorizontal();

        GUILayout.Space(4);

        // Apply button
        GUILayout.BeginHorizontal();
        GUILayout.Space(10);
        if (GUILayout.Button($"✅ Apply to {label}", GUILayout.Height(28)))
        {
            ApplySettings(source, deviceIdx, resIdx, fps);
        }
        GUILayout.Space(10);
        GUILayout.EndHorizontal();
    }

    #endregion

    #region Logic

    private void RefreshDeviceList()
    {
        _devices = WebCamTexture.devices;
        _deviceNames = new string[_devices.Length];
        for (int i = 0; i < _devices.Length; i++)
        {
            string front = _devices[i].isFrontFacing ? " (front)" : "";
            _deviceNames[i] = $"[{i}] {_devices[i].name}{front}";
        }
    }

    private void CacheSources()
    {
        if (WebcamManager.Instance == null) return;
        _portraitSource = WebcamManager.Instance.GetSource(WebcamRole.Portrait);
        _paperScanSource = WebcamManager.Instance.GetSource(WebcamRole.PaperScan);
    }

    private void SyncStateFromSources()
    {
        SyncSourceState(_portraitSource, ref _portraitDeviceIdx, ref _portraitResIdx, ref _portraitFps);
        SyncSourceState(_paperScanSource, ref _paperScanDeviceIdx, ref _paperScanResIdx, ref _paperScanFps);
    }

    private void SyncSourceState(WebcamSource source, ref int deviceIdx, ref int resIdx, ref int fps)
    {
        if (source == null || _devices == null) return;

        // Match current device name to index
        string currentName = source.DeviceName;
        deviceIdx = 0;
        for (int i = 0; i < _devices.Length; i++)
        {
            if (_devices[i].name == currentName)
            {
                deviceIdx = i;
                break;
            }
        }

        // Match current resolution to preset index
        int w = source.RequestedWidth;
        int h = source.RequestedHeight;
        resIdx = 2; // default 1920×1080
        for (int i = 0; i < ResolutionValues.Length; i++)
        {
            if (ResolutionValues[i][0] == w && ResolutionValues[i][1] == h)
            {
                resIdx = i;
                break;
            }
        }

        fps = source.RequestedFPS;
    }

    private void ApplySettings(WebcamSource source, int deviceIdx, int resIdx, int fps)
    {
        if (source == null || _devices == null || _devices.Length == 0) return;

        int clampedIdx = Mathf.Clamp(deviceIdx, 0, _devices.Length - 1);
        string targetDevice = _devices[clampedIdx].name;

        int clampedRes = Mathf.Clamp(resIdx, 0, ResolutionValues.Length - 1);
        int w = ResolutionValues[clampedRes][0];
        int h = ResolutionValues[clampedRes][1];

        Debug.Log($"[CameraSettings] Applying: device='{targetDevice}', res={w}×{h}, fps={fps}");

        source.Configure(targetDevice, w, h, fps);
    }

    private void InitStyles()
    {
        if (_stylesInitialized) return;
        _stylesInitialized = true;

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        _subHeaderStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };

        _boxStyle = new GUIStyle(GUI.skin.box);
    }

    #endregion
}
