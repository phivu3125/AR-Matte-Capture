using System;
using System.Diagnostics;
using System.IO;
using System.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// BiRefNet Background Removal Integration
/// Calls external Python script/exe to perform high-quality background removal on still images.
/// 
/// Usage:
///   1. Attach to a GameObject
///   2. Call CaptureAndProcess(texture) with a Texture2D
///   3. Listen to OnCaptureComplete or OnCaptureError events
/// </summary>
public class BiRefNetCapture : MonoBehaviour
{
    #region Events
    /// <summary>Fired when background removal completes successfully</summary>
    public event Action<Texture2D> OnCaptureComplete;
    
    /// <summary>Fired when an error occurs during processing</summary>
    public event Action<string> OnCaptureError;
    #endregion

    #region Serialized Fields
    [Header("Settings")]
    [Tooltip("Processing size for BiRefNet (larger = better quality, slower)")]
    [Range(512, 2048)]
    public int processingSize = 1024;
    
    [Tooltip("Use FP16 precision (faster, slightly lower quality)")]
    public bool useFP16 = true;
    
    [Tooltip("Maximum time to wait for processing (seconds)")]
    [Range(30, 300)]
    public float timeoutSeconds = 120f;
    
    [Tooltip("Number of retry attempts on failure")]
    [Range(0, 5)]
    public int retryAttempts = 3;
    
    [Header("Debug")]
    [Tooltip("Log verbose output to console")]
    public bool verboseLogging = false;
    #endregion

    #region Private Fields
    private bool isProcessing;
    private string birefnetPath;
    private string tempInputPath;
    private string tempOutputPath;
    #endregion

    #region Properties
    /// <summary>Whether processing is currently in progress</summary>
    public bool IsProcessing => isProcessing;
    #endregion

    #region Unity Lifecycle
    void Awake()
    {
        // Determine BiRefNet executable/script path
        birefnetPath = GetBiRefNetPath();
        
        // Setup temp file paths
        string tempDir = Path.Combine(Application.temporaryCachePath, "BiRefNet");
        Directory.CreateDirectory(tempDir);
        tempInputPath = Path.Combine(tempDir, "input.png");
        tempOutputPath = Path.Combine(tempDir, "output.png");
        
        if (verboseLogging)
        {
            Debug.Log($"[BiRefNet] Executable path: {birefnetPath}");
            Debug.Log($"[BiRefNet] Temp directory: {tempDir}");
        }
    }
    #endregion

    #region Public API
    /// <summary>
    /// Capture and process an image to remove its background
    /// </summary>
    /// <param name="sourceTexture">Source texture to process</param>
    public void CaptureAndProcess(Texture2D sourceTexture)
    {
        if (isProcessing)
        {
            Debug.LogWarning("[BiRefNet] Already processing, please wait");
            return;
        }

        if (sourceTexture == null)
        {
            OnCaptureError?.Invoke("Source texture is null");
            return;
        }

        StartCoroutine(ProcessCoroutine(sourceTexture));
    }

    /// <summary>
    /// Capture and process from a RenderTexture
    /// </summary>
    /// <param name="sourceRT">Source RenderTexture to process</param>
    public void CaptureAndProcess(RenderTexture sourceRT)
    {
        if (sourceRT == null)
        {
            OnCaptureError?.Invoke("Source RenderTexture is null");
            return;
        }

        // Convert RenderTexture to Texture2D
        Texture2D tex = new Texture2D(sourceRT.width, sourceRT.height, TextureFormat.RGB24, false);
        RenderTexture.active = sourceRT;
        tex.ReadPixels(new Rect(0, 0, sourceRT.width, sourceRT.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        CaptureAndProcess(tex);
    }
    #endregion

    #region Processing
    private IEnumerator ProcessCoroutine(Texture2D sourceTexture)
    {
        isProcessing = true;
        int attempt = 0;
        bool success = false;
        string lastError = "";

        while (attempt <= retryAttempts && !success)
        {
            attempt++;
            if (verboseLogging) Debug.Log($"[BiRefNet] Attempt {attempt}/{retryAttempts + 1}");

            // Save input texture
            try
            {
                byte[] pngData = sourceTexture.EncodeToPNG();
                File.WriteAllBytes(tempInputPath, pngData);
                if (verboseLogging) Debug.Log($"[BiRefNet] Saved input: {tempInputPath}");
            }
            catch (Exception e)
            {
                lastError = $"Failed to save input image: {e.Message}";
                Debug.LogError($"[BiRefNet] {lastError}");
                continue;
            }

            // Delete previous output if exists
            if (File.Exists(tempOutputPath))
            {
                try { File.Delete(tempOutputPath); }
                catch { /* Ignore */ }
            }

            // Run BiRefNet
            bool completed = false;
            int exitCode = -1;
            string errorOutput = "";

            yield return StartCoroutine(RunBiRefNet((code, error) =>
            {
                completed = true;
                exitCode = code;
                errorOutput = error;
            }));

            if (!completed)
            {
                lastError = "Process timed out";
                Debug.LogWarning($"[BiRefNet] {lastError}");
                continue;
            }

            if (exitCode != 0)
            {
                lastError = GetExitCodeMessage(exitCode, errorOutput);
                Debug.LogWarning($"[BiRefNet] {lastError}");
                continue;
            }

            // Check output exists
            if (!File.Exists(tempOutputPath))
            {
                lastError = "Output file not created";
                Debug.LogWarning($"[BiRefNet] {lastError}");
                continue;
            }

            // Load result
            try
            {
                byte[] resultData = File.ReadAllBytes(tempOutputPath);
                Texture2D resultTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (resultTexture.LoadImage(resultData))
                {
                    success = true;
                    isProcessing = false;
                    OnCaptureComplete?.Invoke(resultTexture);
                    yield break;
                }
                else
                {
                    lastError = "Failed to load result image";
                }
            }
            catch (Exception e)
            {
                lastError = $"Failed to load result: {e.Message}";
            }
        }

        isProcessing = false;
        OnCaptureError?.Invoke(lastError);
    }

    private IEnumerator RunBiRefNet(Action<int, string> onComplete)
    {
        string executable;
        string arguments;

        // Use bundled executable only (dev tools moved to Tools/BiRefNetBuilder)
        string exePath = Path.Combine(Application.streamingAssetsPath, "BiRefNet", "dist", "birefnet_infer", "birefnet_infer.exe");
        
        if (File.Exists(exePath))
        {
            executable = exePath;
            arguments = BuildArguments();
        }
        else
        {
            onComplete?.Invoke(-1, $"BiRefNet not found at: {birefnetPath}");
            yield break;
        }

        if (verboseLogging) Debug.Log($"[BiRefNet] Running: {executable} {arguments}");

        Process process = null;
        string errorOutput = "";
        bool timedOut = false;

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(birefnetPath)
            };

            process = new Process { StartInfo = startInfo };
            process.Start();

            float startTime = Time.realtimeSinceStartup;
            
            // Wait for process with timeout
            while (!process.HasExited)
            {
                if (Time.realtimeSinceStartup - startTime > timeoutSeconds)
                {
                    timedOut = true;
                    try { process.Kill(); } catch { /* Ignore */ }
                    break;
                }
                yield return null;
            }

            if (timedOut)
            {
                onComplete?.Invoke(-1, "Process timed out");
                yield break;
            }

            // Read output
            string stdout = process.StandardOutput.ReadToEnd();
            errorOutput = process.StandardError.ReadToEnd();

            if (verboseLogging && !string.IsNullOrEmpty(stdout))
                Debug.Log($"[BiRefNet] stdout: {stdout}");
            
            if (!string.IsNullOrEmpty(errorOutput) && verboseLogging)
                Debug.Log($"[BiRefNet] stderr: {errorOutput}");

            onComplete?.Invoke(process.ExitCode, errorOutput);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private string BuildArguments()
    {
        string args = $"\"{tempInputPath}\" \"{tempOutputPath}\" --size {processingSize}";
        if (useFP16) args += " --fp16";
        return args;
    }

    private string GetBiRefNetPath()
    {
        // Only look for bundled exe (dev tools are in Tools/BiRefNetBuilder)
        return Path.Combine(Application.streamingAssetsPath, "BiRefNet", "dist", "birefnet_infer", "birefnet_infer.exe");
    }

    private string GetExitCodeMessage(int exitCode, string errorOutput)
    {
        switch (exitCode)
        {
            case 1: return $"General error: {errorOutput}";
            case 2: return "No GPU available";
            case 3: return "Input file not found";
            case 4: return $"Model loading failed: {errorOutput}";
            case 5: return $"Inference failed: {errorOutput}";
            default: return $"Unknown error (exit code {exitCode}): {errorOutput}";
        }
    }
    #endregion

    #region Cleanup
    void OnDestroy()
    {
        // Clean up temp files
        try
        {
            if (File.Exists(tempInputPath)) File.Delete(tempInputPath);
            if (File.Exists(tempOutputPath)) File.Delete(tempOutputPath);
        }
        catch { /* Ignore cleanup errors */ }
    }
    #endregion
}
