using UnityEngine;
using ARMatteCapture.Webcam;

/// <summary>
/// Runtime settings panel for webcams.
/// Press Tab to toggle. Compact IMGUI layout with collapsible sections.
/// Model selection is loaded at startup from StreamingAssets/rvm-config.json (see RVMCore).
/// </summary>
public class CameraSettingsPanel : MonoBehaviour
{
    #region Serialized Fields

    [Header("Toggle")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    [Header("Panel")]
    [SerializeField] private float panelWidth = 380f;
    [SerializeField] private float panelHeight = 480f;

    #endregion

    #region Private State

    private bool _showPanel;
    private WebCamDevice[] _devices;
    private string[] _deviceNames;
    private Vector2 _scrollPos;

    // Cached refs
    private WebcamSource _portraitSource;
    private WebcamSource _paperScanSource;

    // Foldouts
    private bool _portraitOpen = true;
    private bool _paperScanOpen = true;
    private bool _devicesOpen = false;

    // Resolution presets
    private static readonly string[] ResolutionLabels = { "640×480", "720p", "1080p", "1440p" };
    private static readonly int[][] ResolutionValues = {
        new[] { 640, 480 },
        new[] { 1280, 720 },
        new[] { 1920, 1080 },
        new[] { 2560, 1440 }
    };
    private static readonly string[] FpsLabels = { "15", "30", "60" };
    private static readonly int[] FpsValues = { 15, 30, 60 };

    // Per-source state
    private int _portraitDeviceIdx;
    private int _portraitResIdx = 2;
    private int _portraitFpsIdx = 1;

    private int _paperScanDeviceIdx;
    private int _paperScanResIdx = 2;
    private int _paperScanFpsIdx = 1;

    // GUI styles
    private GUIStyle _headerStyle;
    private GUIStyle _sectionHeaderStyle;
    private GUIStyle _labelDim;
    private GUIStyle _boxBg;
    private bool _stylesInit;

    #endregion

    #region Unity Lifecycle

    void Start()
    {
        RefreshAll();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            _showPanel = !_showPanel;
            if (_showPanel) RefreshAll();
        }
    }

    void OnGUI()
    {
        if (!_showPanel) return;
        InitStyles();

        float x = (Screen.width - panelWidth) / 2f;
        float y = (Screen.height - panelHeight) / 2f;
        Rect rect = new Rect(x, y, panelWidth, panelHeight);

        GUI.Box(rect, GUIContent.none, _boxBg);

        GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, rect.height - 16));

        // Header
        GUILayout.BeginHorizontal();
        GUILayout.Label("⚙  Camera Settings", _headerStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label($"[{toggleKey}] close", _labelDim);
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        _scrollPos = GUILayout.BeginScrollView(_scrollPos);

        // Portrait
        if (Foldout(ref _portraitOpen, "Portrait (RVM)"))
        {
            DrawSourceSection(_portraitSource, ref _portraitDeviceIdx, ref _portraitResIdx, ref _portraitFpsIdx);
        }

        // Paper scan
        if (Foldout(ref _paperScanOpen, "Paper Scan"))
        {
            DrawSourceSection(_paperScanSource, ref _paperScanDeviceIdx, ref _paperScanResIdx, ref _paperScanFpsIdx);
        }

        // Devices (collapsed by default)
        if (Foldout(ref _devicesOpen, $"Devices ({(_devices != null ? _devices.Length : 0)})"))
        {
            DrawDeviceList();
        }

        GUILayout.Space(6);
        if (GUILayout.Button("Refresh", GUILayout.Height(24)))
        {
            RefreshAll();
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    #endregion

    #region Drawing

    private bool Foldout(ref bool open, string title)
    {
        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        string arrow = open ? "▼" : "▶";
        if (GUILayout.Button($"{arrow}  {title}", _sectionHeaderStyle, GUILayout.Height(22)))
            open = !open;
        GUILayout.EndHorizontal();
        return open;
    }

    private void DrawDeviceList()
    {
        if (_devices == null || _devices.Length == 0)
        {
            GUILayout.Label("  No webcam devices found.", _labelDim);
            return;
        }
        for (int i = 0; i < _devices.Length; i++)
        {
            string front = _devices[i].isFrontFacing ? "  (front)" : "";
            GUILayout.Label($"  [{i}]  {_devices[i].name}{front}", _labelDim);
        }
    }

    private void DrawSourceSection(WebcamSource source, ref int deviceIdx, ref int resIdx, ref int fpsIdx)
    {
        if (source == null)
        {
            GUILayout.Label("  Not assigned in WebcamManager.", _labelDim);
            return;
        }

        // Status line
        string statusColor = source.IsPlaying ? "#6bd16b" : "#d16b6b";
        string statusDot = source.IsPlaying ? "● Playing" : "● Stopped";
        var rich = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11 };
        GUILayout.Label($"  <color={statusColor}>{statusDot}</color>  {source.ActualWidth}×{source.ActualHeight}  ·  {TrimDevice(source.DeviceName)}", rich);

        // Device compact dropdown via selection grid (2 columns to save vertical space)
        if (_devices != null && _devices.Length > 0)
        {
            int cols = _devices.Length >= 2 ? 2 : 1;
            deviceIdx = GUILayout.SelectionGrid(Mathf.Clamp(deviceIdx, 0, _devices.Length - 1), _deviceNames, cols);
        }

        // Resolution + FPS on same row-pair
        GUILayout.BeginHorizontal();
        GUILayout.Label("  Res:", GUILayout.Width(42));
        resIdx = GUILayout.Toolbar(resIdx, ResolutionLabels);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("  FPS:", GUILayout.Width(42));
        fpsIdx = GUILayout.Toolbar(fpsIdx, FpsLabels);
        GUILayout.EndHorizontal();

        GUILayout.Space(2);
        if (GUILayout.Button("Apply", GUILayout.Height(22)))
        {
            ApplySettings(source, deviceIdx, resIdx, fpsIdx);
        }
    }

    private string TrimDevice(string name)
    {
        if (string.IsNullOrEmpty(name)) return "(no device)";
        return name.Length > 28 ? name.Substring(0, 28) + "…" : name;
    }

    #endregion

    #region Logic

    private void RefreshAll()
    {
        RefreshDeviceList();
        CacheSources();
        SyncStateFromSources();
    }

    private void RefreshDeviceList()
    {
        _devices = WebCamTexture.devices;
        _deviceNames = new string[_devices.Length];
        for (int i = 0; i < _devices.Length; i++)
        {
            string front = _devices[i].isFrontFacing ? " (front)" : "";
            string trimmed = _devices[i].name.Length > 22
                ? _devices[i].name.Substring(0, 22) + "…"
                : _devices[i].name;
            _deviceNames[i] = $"[{i}] {trimmed}{front}";
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
        SyncSourceState(_portraitSource, ref _portraitDeviceIdx, ref _portraitResIdx, ref _portraitFpsIdx);
        SyncSourceState(_paperScanSource, ref _paperScanDeviceIdx, ref _paperScanResIdx, ref _paperScanFpsIdx);
    }

    private void SyncSourceState(WebcamSource source, ref int deviceIdx, ref int resIdx, ref int fpsIdx)
    {
        if (source == null || _devices == null) return;

        string currentName = source.DeviceName;
        deviceIdx = 0;
        for (int i = 0; i < _devices.Length; i++)
        {
            if (_devices[i].name == currentName) { deviceIdx = i; break; }
        }

        int w = source.RequestedWidth, h = source.RequestedHeight;
        resIdx = 2;
        for (int i = 0; i < ResolutionValues.Length; i++)
        {
            if (ResolutionValues[i][0] == w && ResolutionValues[i][1] == h) { resIdx = i; break; }
        }

        fpsIdx = 1;
        int fps = source.RequestedFPS;
        for (int i = 0; i < FpsValues.Length; i++)
        {
            if (FpsValues[i] == fps) { fpsIdx = i; break; }
        }
    }

    private void ApplySettings(WebcamSource source, int deviceIdx, int resIdx, int fpsIdx)
    {
        if (source == null || _devices == null || _devices.Length == 0) return;

        int di = Mathf.Clamp(deviceIdx, 0, _devices.Length - 1);
        int ri = Mathf.Clamp(resIdx, 0, ResolutionValues.Length - 1);
        int fi = Mathf.Clamp(fpsIdx, 0, FpsValues.Length - 1);

        string device = _devices[di].name;
        int w = ResolutionValues[ri][0];
        int h = ResolutionValues[ri][1];
        int fps = FpsValues[fi];

        Debug.Log($"[CameraSettings] Apply webcam: device='{device}' {w}×{h}@{fps}");
        source.Configure(device, w, h, fps);
    }

    private void InitStyles()
    {
        if (_stylesInit) return;
        _stylesInit = true;

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };

        _sectionHeaderStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(8, 8, 4, 4)
        };

        _labelDim = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            normal = { textColor = new Color(0.75f, 0.75f, 0.75f) }
        };

        // Opaque dark background so IMGUI box doesn't look like transparent glass.
        var bgTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.09f, 0.94f));
        bgTex.Apply();
        _boxBg = new GUIStyle(GUI.skin.box) { normal = { background = bgTex } };
    }

    #endregion
}
