using UnityEngine;

/// <summary>
/// Simple FPS display overlay - attach to any GameObject
/// Shows Game FPS and optionally Camera FPS
/// Does NOT modify or depend on RVMCore
/// </summary>
public class FPSDisplay : MonoBehaviour
{
    [Header("Display Settings")]
    public bool showFPS = true;
    public KeyCode toggleKey = KeyCode.F1;

    [Header("Position")]
    public TextAnchor anchor = TextAnchor.UpperLeft;
    public Vector2 offset = new Vector2(10, 10);

    [Header("Style")]
    public int fontSize = 18;
    public Color goodColor = Color.green;
    public Color warningColor = Color.yellow;
    public Color badColor = Color.red;

    [Header("Thresholds")]
    public float goodFPS = 50f;
    public float warningFPS = 30f;

    // FPS calculation
    private float deltaTime;
    private float fps;
    private float updateInterval = 0.5f;
    private float timeSinceUpdate;
    private int frameCount;

    // Cached
    private GUIStyle style;
    private Rect rect;

    void Update()
    {
        // Toggle with key
        if (Input.GetKeyDown(toggleKey))
            showFPS = !showFPS;

        // Accumulate
        deltaTime += Time.unscaledDeltaTime;
        frameCount++;
        timeSinceUpdate += Time.unscaledDeltaTime;

        // Update FPS every interval
        if (timeSinceUpdate >= updateInterval)
        {
            fps = frameCount / timeSinceUpdate;
            frameCount = 0;
            timeSinceUpdate = 0f;
        }
    }

    void OnGUI()
    {
        if (!showFPS) return;

        // Init style once
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = anchor
            };
        }

        // Update style
        style.fontSize = fontSize;
        style.normal.textColor = fps >= goodFPS ? goodColor : fps >= warningFPS ? warningColor : badColor;

        // Calculate position based on anchor
        float w = 150, h = 30;
        float x = offset.x, y = offset.y;

        switch (anchor)
        {
            case TextAnchor.UpperRight:
            case TextAnchor.MiddleRight:
            case TextAnchor.LowerRight:
                x = Screen.width - w - offset.x;
                break;
        }

        switch (anchor)
        {
            case TextAnchor.LowerLeft:
            case TextAnchor.LowerCenter:
            case TextAnchor.LowerRight:
                y = Screen.height - h - offset.y;
                break;
            case TextAnchor.MiddleLeft:
            case TextAnchor.MiddleCenter:
            case TextAnchor.MiddleRight:
                y = (Screen.height - h) / 2;
                break;
        }

        rect = new Rect(x, y, w, h);

        // Draw shadow
        var shadowStyle = new GUIStyle(style) { normal = { textColor = Color.black } };
        GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), $"FPS: {fps:F1}", shadowStyle);

        // Draw text
        GUI.Label(rect, $"FPS: {fps:F1}", style);
    }
}
