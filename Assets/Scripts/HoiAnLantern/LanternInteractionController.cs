using UnityEngine;
using ARMatteCapture.Scanning;

namespace HoiAnLantern
{
    /// <summary>
    /// Manages the target-lock → lantern-hang → reset-scan flow.
    /// Subscribes to CheckARObjectHitTarget.OnLanternHangSuccess and
    /// notifies ScanSessionController to reset for a new user.
    /// Decoupled from the scan detection pipeline.
    /// </summary>
    public class LanternInteractionController : MonoBehaviour
    {
        #region Serialized Fields

        [Header("References")]
        [Tooltip("CheckARObjectHitTarget that fires OnLanternHangSuccess")]
        [SerializeField] private CheckARObjectHitTarget checkARObjectHitTarget;

        #endregion

        #region Unity Lifecycle

        void OnEnable()
        {
            if (checkARObjectHitTarget != null)
                checkARObjectHitTarget.OnLanternHangSuccess += HandleLanternHangSuccess;
        }

        void OnDisable()
        {
            if (checkARObjectHitTarget != null)
                checkARObjectHitTarget.OnLanternHangSuccess -= HandleLanternHangSuccess;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// When a lantern hang completes and flies up, reset the scan session
        /// so a new user can scan.
        /// </summary>
        private void HandleLanternHangSuccess()
        {
            Debug.Log("[LanternInteraction] Lantern hang success — resetting scan session for new user");

            if (ScanSessionController.Instance != null)
                ScanSessionController.Instance.ResetForNewScan();
        }

        #endregion
    }
}
