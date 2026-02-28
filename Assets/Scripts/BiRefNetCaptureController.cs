using System;
using System.IO;
using UnityEngine;

/// <summary>
/// BiRefNet Capture Controller for High-Quality Still Background Removal
/// 
/// Real-time photobooth flow:
/// - Preview: RVM runs continuously (fast, moderate quality)
/// - Capture: Space key triggers BiRefNet (slow, high quality)
/// 
/// Usage:
///   1. Attach to a GameObject
///   2. Assign RVMCore and BiRefNetCapture references
///   3. Press Space to capture
///   4. Listen to OnCaptureComplete for final composited image
/// </summary>
public class BiRefNetCaptureController : MonoBehaviour
{
    #region Events
    /// <summary>Fired when capture and compositing completes successfully</summary>
    public event Action<Texture2D> OnCaptureComplete;
    
    /// <summary>Fired when an error occurs during capture</summary>
    public event Action<string> OnCaptureError;
    #endregion

    #region Serialized Fields
    [Header("References")]
    [Tooltip("Reference to RVMCore for getting source texture")]
    public RVMCore rvmCore;
    
    [Tooltip("Reference to BiRefNetCapture for high-quality background removal")]
    public BiRefNetCapture birefnetCapture;
    
    [Tooltip("Camera to capture scene background (should exclude RVM UI layer)")]
    public Camera sceneCamera;

    [Header("Input")]
    [Tooltip("Key to trigger capture")]
    public KeyCode captureKey = KeyCode.Space;

    [Header("Output")]
    [Tooltip("Save captured image to disk")]
    public bool saveToFile = false;
    
    [Tooltip("Output directory name inside StreamingAssets/BiRefNet/")]
    public string outputFolder = "captures";

    [Header("Debug")]
    [Tooltip("Log verbose output to console")]
    public bool verboseLogging = false;
    #endregion

    #region Private Fields
    private bool isCapturing;
    private Texture2D capturedSceneBackground;
    private RenderTexture sceneRT;
    private RenderTexture compositeRT;
    private Material compositeMaterial;
    private static readonly string LOG_PREFIX = "[BiRefNetCapture]";
    #endregion

    #region Properties
    /// <summary>Whether capture is currently in progress</summary>
    public bool IsCapturing => isCapturing;
    #endregion

    #region Unity Lifecycle
    void Start()
    {
        ValidateReferences();
        CreateCompositeMaterial();
        
        // Subscribe to BiRefNetCapture events
        if (birefnetCapture != null)
        {
            birefnetCapture.OnCaptureComplete += HandleBiRefNetComplete;
            birefnetCapture.OnCaptureError += HandleBiRefNetError;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(captureKey))
        {
            Debug.Log($"{LOG_PREFIX} Space pressed! isCapturing={isCapturing}");
            if (!isCapturing)
            {
                StartCapture();
            }
            else
            {
                Debug.LogWarning($"{LOG_PREFIX} Already capturing, please wait...");
            }
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        if (birefnetCapture != null)
        {
            birefnetCapture.OnCaptureComplete -= HandleBiRefNetComplete;
            birefnetCapture.OnCaptureError -= HandleBiRefNetError;
        }

        // Cleanup resources
        ReleaseResources();
    }
    #endregion

    #region Capture Flow
    private void StartCapture()
    {
        if (!ValidateReferences())
        {
            OnCaptureError?.Invoke("Missing required references");
            return;
        }

        if (birefnetCapture.IsProcessing)
        {
            LogWarning("BiRefNet is already processing");
            return;
        }

        isCapturing = true;
        Log("Capture started");

        try
        {
            // Get raw webcam frame (before RVM processing)
            Texture sourceTexture = rvmCore.GetSourceTexture();
            if (sourceTexture == null)
            {
                HandleError("Source texture is null - RVM may not be running");
                return;
            }

            // Store scene background at capture time (synchronized)
            CaptureSceneBackground();

            // Convert source to Texture2D and send to BiRefNet
            // Use sRGB RenderTexture to ensure correct color space conversion from HDR
            if (sourceTexture is RenderTexture rt)
            {
                Log($"Sending frame to BiRefNet: {rt.width}x{rt.height}");
                // Convert HDR to sRGB for correct colors
                RenderTexture srgbRT = RenderTexture.GetTemporary(
                    rt.width, rt.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(rt, srgbRT);
                birefnetCapture.CaptureAndProcess(srgbRT);
                RenderTexture.ReleaseTemporary(srgbRT);
            }
            else if (sourceTexture is Texture2D tex)
            {
                Log($"Sending frame to BiRefNet: {tex.width}x{tex.height}");
                birefnetCapture.CaptureAndProcess(tex);
            }
            else
            {
                // Fallback: blit to sRGB RenderTexture first
                RenderTexture tempRT = RenderTexture.GetTemporary(
                    sourceTexture.width, sourceTexture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(sourceTexture, tempRT);
                birefnetCapture.CaptureAndProcess(tempRT);
                RenderTexture.ReleaseTemporary(tempRT);
            }
        }
        catch (Exception e)
        {
            HandleError($"Failed to start capture: {e.Message}");
        }
    }

    private void CaptureSceneBackground()
    {
        // Capture scene from sceneCamera (excludes RVM UI layer)
        if (sceneCamera == null)
        {
            LogWarning("Scene camera not assigned, using fallback background");
            CaptureBackgroundFallback();
            return;
        }

        // WARNING: Don't use Main Camera as sceneCamera - it causes display issues
        if (sceneCamera.CompareTag("MainCamera"))
        {
            LogWarning("sceneCamera is MainCamera - this can cause display issues! Using fallback instead.");
            CaptureBackgroundFallback();
            return;
        }

        // Get dimensions from source texture
        Texture sourceTexture = rvmCore.GetSourceTexture();
        int width = sourceTexture?.width ?? 1920;
        int height = sourceTexture?.height ?? 1080;

        // Create or resize scene render texture
        if (sceneRT == null || sceneRT.width != width || sceneRT.height != height)
        {
            if (sceneRT != null)
            {
                sceneRT.Release();
                Destroy(sceneRT);
            }
            sceneRT = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            sceneRT.Create();
        }

        // Store current RenderTexture.active to restore later
        RenderTexture previousActiveRT = RenderTexture.active;
        
        // Capture scene - temporarily redirect camera output
        RenderTexture previousCameraRT = sceneCamera.targetTexture;
        bool wasEnabled = sceneCamera.enabled;
        
        try
        {
            sceneCamera.targetTexture = sceneRT;
            sceneCamera.Render();
        }
        finally
        {
            // CRITICAL: Restore camera state immediately
            sceneCamera.targetTexture = previousCameraRT;
            sceneCamera.enabled = wasEnabled;
        }

        // Convert to Texture2D
        if (capturedSceneBackground != null)
        {
            Destroy(capturedSceneBackground);
        }
        capturedSceneBackground = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        RenderTexture.active = sceneRT;
        capturedSceneBackground.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        capturedSceneBackground.Apply();
        
        // Restore previous active RenderTexture
        RenderTexture.active = previousActiveRT;

        Log($"Captured scene background: {width}x{height}");
    }

    private void CaptureBackgroundFallback()
    {
        // Fallback: use RVMCore background image or color
        if (rvmCore.backgroundImage != null)
        {
            // Copy background image
            capturedSceneBackground = rvmCore.backgroundImage;
            Log("Using fallback: RVMCore background image");
        }
        else
        {
            // Create solid color texture
            if (capturedSceneBackground == null || 
                capturedSceneBackground.width != 1 || capturedSceneBackground.height != 1)
            {
                if (capturedSceneBackground != null) Destroy(capturedSceneBackground);
                capturedSceneBackground = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            }
            capturedSceneBackground.SetPixel(0, 0, rvmCore.backgroundColor);
            capturedSceneBackground.Apply();
            Log($"Using fallback: background color {rvmCore.backgroundColor}");
        }
    }

    private void HandleBiRefNetComplete(Texture2D maskedUser)
    {
        if (!isCapturing)
        {
            // Not our capture, ignore
            return;
        }

        Log($"BiRefNet complete: {maskedUser.width}x{maskedUser.height}");

        try
        {
            // Composite masked user onto scene background
            Texture2D result = CompositeResult(maskedUser, capturedSceneBackground);

            if (result != null)
            {
                Log("Compositing complete");

                // Save to file if enabled
                if (saveToFile)
                {
                    SaveToFile(result);
                }

                // Fire completion event
                OnCaptureComplete?.Invoke(result);
            }
            else
            {
                HandleError("Compositing failed");
            }
        }
        catch (Exception e)
        {
            HandleError($"Failed to composite: {e.Message}");
        }
        finally
        {
            isCapturing = false;
        }
    }

    private void HandleBiRefNetError(string error)
    {
        if (!isCapturing) return;
        HandleError($"BiRefNet error: {error}");
    }
    #endregion

    #region Compositing
    private void CreateCompositeMaterial()
    {
        // Use Unity's UI/Default shader for simple alpha blending
        // This handles premultiplied alpha correctly
        Shader shader = Shader.Find("UI/Default");
        if (shader != null)
        {
            compositeMaterial = new Material(shader);
        }
        else
        {
            // Fallback to unlit transparent
            shader = Shader.Find("Unlit/Transparent");
            if (shader != null)
            {
                compositeMaterial = new Material(shader);
            }
        }
    }

    private Texture2D CompositeResult(Texture2D foreground, Texture background)
    {
        int width = foreground.width;
        int height = foreground.height;

        // Ensure composite RenderTexture exists and is correct size
        if (compositeRT == null || compositeRT.width != width || compositeRT.height != height)
        {
            if (compositeRT != null)
            {
                compositeRT.Release();
                Destroy(compositeRT);
            }
            compositeRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            compositeRT.Create();
        }

        // Store current active RT
        RenderTexture previousRT = RenderTexture.active;

        try
        {
            // Step 1: Blit background to composite RT (stretched to fill)
            Graphics.Blit(background, compositeRT);

            // Step 2: Draw foreground with alpha blending
            RenderTexture.active = compositeRT;
            
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, width, height, 0);

            // Draw foreground texture with alpha
            if (compositeMaterial != null)
            {
                compositeMaterial.mainTexture = foreground;
                compositeMaterial.SetPass(0);
            }
            
            Graphics.DrawTexture(
                new Rect(0, 0, width, height),
                foreground,
                new Rect(0, 0, 1, 1),
                0, 0, 0, 0,
                Color.white,
                compositeMaterial
            );

            GL.PopMatrix();

            // Step 3: Read result to Texture2D (sRGB for correct colors)
            Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false, false); // linear=false for sRGB
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();

            return result;
        }
        finally
        {
            RenderTexture.active = previousRT;
        }
    }
    #endregion

    #region File Output
    private void SaveToFile(Texture2D image)
    {
        try
        {
            // Save to StreamingAssets/BiRefNet/captures/
            string dir = Path.Combine(Application.streamingAssetsPath, "BiRefNet", outputFolder);
            Directory.CreateDirectory(dir);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = $"capture_{timestamp}.png";
            string path = Path.Combine(dir, filename);

            byte[] pngData = image.EncodeToPNG();
            File.WriteAllBytes(path, pngData);

            Log($"Saved to: {path}");
        }
        catch (Exception e)
        {
            LogWarning($"Failed to save file: {e.Message}");
        }
    }
    #endregion

    #region Validation & Cleanup
    private bool ValidateReferences()
    {
        if (rvmCore == null)
        {
            LogError("RVMCore reference is not assigned!");
            return false;
        }

        if (birefnetCapture == null)
        {
            LogError("BiRefNetCapture reference is not assigned!");
            return false;
        }

        return true;
    }

    private void ReleaseResources()
    {
        if (compositeRT != null)
        {
            compositeRT.Release();
            Destroy(compositeRT);
            compositeRT = null;
        }

        if (sceneRT != null)
        {
            sceneRT.Release();
            Destroy(sceneRT);
            sceneRT = null;
        }

        if (compositeMaterial != null)
        {
            Destroy(compositeMaterial);
            compositeMaterial = null;
        }

        if (capturedSceneBackground != null)
        {
            Destroy(capturedSceneBackground);
            capturedSceneBackground = null;
        }
    }
    #endregion

    #region Logging
    private void HandleError(string message)
    {
        isCapturing = false;
        LogError(message);
        OnCaptureError?.Invoke(message);
    }

    private void Log(string message)
    {
        if (verboseLogging)
        {
            Debug.Log($"{LOG_PREFIX} {message}");
        }
    }

    private void LogWarning(string message)
    {
        Debug.LogWarning($"{LOG_PREFIX} {message}");
    }

    private void LogError(string message)
    {
        Debug.LogError($"{LOG_PREFIX} {message}");
    }
    #endregion
}
