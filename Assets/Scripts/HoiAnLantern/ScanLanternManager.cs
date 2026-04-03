using System;
using UnityEngine;
using DG.Tweening;

namespace HoiAnLantern
{
/// <summary>
/// Manages the scanning flow: moves camera to scan table, triggers texture capture,
/// then returns camera to original position. Replaces the old Kinect-dependent
/// ScanLaternManager with webcam-based pipeline controls.
/// </summary>
public class ScanLanternManager : MonoBehaviour
{
    #region Singleton

    public static ScanLanternManager Instance;

    #endregion

    #region Serialized Fields

    [Header("Camera")]
    [SerializeField] private Camera arCamera;
    [SerializeField] private Transform cameraScanPosition;

    [Header("Scan Table")]
    [SerializeField] private GameObject scanTable;
    [SerializeField] private GameObject foregroundPanel;

    [Header("Lanterns")]
    [SerializeField] private GameObject transparentLantern;
    [SerializeField] private FaceTextureLoader demoLantern;
    [SerializeField] private FaceTextureLoader mainLantern;
    [SerializeField] private Material[] materialVariants;
    [SerializeField] private GameObject mainLanternObject;

    [Header("References")]
    [SerializeField] private ArucoMarkerTracker markerTracker;
    [SerializeField] private CheckARObjectHitTarget checkARObjectHitTarget;

    [Tooltip("RVM RawImage display to hide during scan and restore after")]
    [SerializeField] private GameObject rvmDisplay;

    [Header("Timing")]
    [Tooltip("Duration of camera transition in seconds")]
    [SerializeField] private float cameraTweenDuration = 2f;
    [Tooltip("FOV to use when viewing the scan table")]
    [SerializeField] private float scanFOV = 60f;
    [Tooltip("Delay after scan success before returning camera")]
    [SerializeField] private float postScanDelay = 5f;

    #endregion

    #region Events

    /// <summary>
    /// Fired when the scanning flow begins (camera starts moving to table).
    /// </summary>
    public event Action OnScanningLanternTextureEvent;

    /// <summary>
    /// Fired when scan is successful and camera is returning.
    /// </summary>
    public event Action OnScanningSuccessEvent;

    #endregion

    #region Private Fields

    private Vector3 originalCameraPosition;
    private Quaternion originalCameraRotation;
    private float originalFOV;

    #endregion

    #region Unity Lifecycle

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Begin the scanning flow: apply random material, show scan table,
    /// pause marker tracking, and move camera to scan position.
    /// </summary>
    public void OnScanningLanternTexture()
    {
        ApplyRandomMaterial(demoLantern, mainLantern);
        scanTable.SetActive(true);
        foregroundPanel.SetActive(false);

        // Hide the RVM display while scanning
        rvmDisplay?.SetActive(false);

        // Pause ArUco detection during scan (no Kinect dependency)
        if (markerTracker != null)
        {
            markerTracker.IsTracking = false;
            markerTracker.PauseDetection();
        }

        OnScanningLanternTextureEvent?.Invoke();
        transparentLantern.SetActive(false);
        mainLanternObject.transform.position = Vector3.zero;

        MoveCameraToScanningTable();
    }

    /// <summary>
    /// Called after a successful scan. Refreshes textures, waits, then
    /// returns camera to original position and resumes tracking.
    /// </summary>
    public void OnScanningSuccess()
    {
        Sequence seq = DOTween.Sequence();

        // Refresh textures to pick up newly captured scan image
        seq.AppendCallback(() => TextureManager.Instance?.RefreshTextures());

        // Wait before transitioning back
        seq.AppendInterval(postScanDelay);

        // Hide scan table, show transparent lantern
        seq.AppendCallback(() =>
        {
            scanTable.SetActive(false);
            transparentLantern.SetActive(true);
        });

        // Move camera back and resume gameplay
        seq.AppendCallback(() =>
        {
            MoveCameraBack(() =>
            {
                foregroundPanel.SetActive(true);
                transparentLantern.SetActive(true);

                // Restore the RVM display after scan completes
                rvmDisplay?.SetActive(true);
                Debug.Log("[ScanLanternManager] Scan complete, resuming gameplay");
                mainLanternObject.transform.position = Vector3.zero;
                checkARObjectHitTarget?.DestroyNewLantern();

                // Resume ArUco detection after returning from scan
                if (markerTracker != null)
                {
                    markerTracker.ResumeDetection();
                }


                OnScanningSuccessEvent?.Invoke();
            });
        });
    }

    /// <summary>
    /// Apply a random material variant to both demo and main lanterns.
    /// Sets face materials and point light colors.
    /// </summary>
    public void ApplyRandomMaterial(FaceTextureLoader demoLoader, FaceTextureLoader mainLoader)
    {
        if (materialVariants == null || materialVariants.Length == 0) return;

        int idx = UnityEngine.Random.Range(0, materialVariants.Length);
        Material mat = new Material(materialVariants[idx]);

        // Set point light colors
        if (demoLoader?.pointLightObject != null)
            demoLoader.pointLightObject.GetComponent<Light>().color = mat.color;
        if (mainLoader?.pointLightObject != null)
            mainLoader.pointLightObject.GetComponent<Light>().color = mat.color;

        // Apply to demo lantern faces
        if (demoLoader != null)
        {
            foreach (var face in demoLoader.faces)
            {
                Renderer rend = face.GetComponent<Renderer>();
                if (rend != null) rend.material = mat;
            }
        }

        // Apply to main lantern faces
        if (mainLoader != null)
        {
            foreach (var face in mainLoader.faces)
            {
                Renderer rend = face.GetComponent<Renderer>();
                if (rend != null) rend.material = mat;
            }
        }
    }

    #endregion

    #region Camera Transitions

    /// <summary>
    /// Smoothly move camera from current position to the scan table position.
    /// Stores original transform for later restoration.
    /// </summary>
    private void MoveCameraToScanningTable()
    {
        originalCameraPosition = arCamera.transform.position;
        originalCameraRotation = arCamera.transform.rotation;
        originalFOV = arCamera.fieldOfView;

        Sequence seq = DOTween.Sequence();
        seq.Append(arCamera.transform.DOMove(cameraScanPosition.position, cameraTweenDuration))
            .SetEase(Ease.InOutCubic);
        seq.Join(arCamera.transform.DORotateQuaternion(cameraScanPosition.rotation, cameraTweenDuration))
            .SetEase(Ease.InOutCubic);
        seq.Join(DOTween.To(() => arCamera.fieldOfView, x => arCamera.fieldOfView = x, scanFOV, cameraTweenDuration))
            .SetEase(Ease.InOutCubic);
        seq.JoinCallback(() =>
        {
            AudioManager.Instance?.PlaySFX("Camera Swish");
        });
    }

    /// <summary>
    /// Smoothly return camera to its original position before the scan.
    /// </summary>
    private void MoveCameraBack(Action onComplete = null)
    {
        Sequence seq = DOTween.Sequence();
        seq.Append(arCamera.transform.DOMove(originalCameraPosition, cameraTweenDuration))
            .SetEase(Ease.InOutCubic);
        seq.Join(arCamera.transform.DORotateQuaternion(originalCameraRotation, cameraTweenDuration))
            .SetEase(Ease.InOutCubic);
        seq.Join(DOTween.To(() => arCamera.fieldOfView, x => arCamera.fieldOfView = x, originalFOV, cameraTweenDuration))
            .SetEase(Ease.InOutCubic);
        seq.JoinCallback(() =>
        {
            AudioManager.Instance?.PlaySFX("Camera Swish");
        });
        seq.OnComplete(() =>
        {
            onComplete?.Invoke();
        });
    }

    #endregion
}
} // namespace HoiAnLantern
