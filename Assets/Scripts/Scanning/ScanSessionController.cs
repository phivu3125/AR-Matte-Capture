using System;
using UnityEngine;

namespace ARMatteCapture.Scanning
{
    /// <summary>
    /// Scan lifecycle states.
    /// </summary>
    public enum ScanState
    {
        Idle,
        Scanning,
        AllMarkersDetected,
        Exporting,
        Success,
        Cooldown
    }

    /// <summary>
    /// Owns the scan lifecycle as a finite state machine.
    /// Fires C# events on each state transition.
    /// No direct references to PaperScan, MarkerManager, or OpenCV.
    /// </summary>
    public class ScanSessionController : MonoBehaviour
    {
        #region Singleton

        public static ScanSessionController Instance { get; private set; }

        #endregion

        #region Serialized Fields

        [Header("Timing")]
        [Tooltip("Seconds all markers must be held before triggering export")]
        [SerializeField] private float allMarkersHoldTime = 3f;

        [Tooltip("Cooldown seconds after success before returning to Idle")]
        [SerializeField] private float cooldownDuration = 0f;

        #endregion

        #region Events

        /// <summary>
        /// Fired on every state transition with (oldState, newState).
        /// </summary>
        public event Action<ScanState, ScanState> OnStateChanged;

        /// <summary>
        /// Fired when entering Scanning from Idle (first marker detected).
        /// </summary>
        public event Action OnScanStarted;

        /// <summary>
        /// Fired when all markers held long enough → Exporting.
        /// </summary>
        public event Action OnExportRequested;

        /// <summary>
        /// Fired when export completes → Success.
        /// </summary>
        public event Action OnScanSuccess;

        /// <summary>
        /// Fired when cycle resets → Idle (ready for new scan).
        /// </summary>
        public event Action OnScanReset;

        #endregion

        #region Private Fields

        private ScanState _state = ScanState.Idle;
        private float _allMarkersTimer;
        private float _cooldownTimer;
        private int _expectedMarkerCount = 4;

        #endregion

        #region Properties

        public ScanState CurrentState => _state;
        public bool IsReadyToScan => _state == ScanState.Idle;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(this);
        }

        void Update()
        {
            switch (_state)
            {
                case ScanState.AllMarkersDetected:
                    _allMarkersTimer += Time.deltaTime;
                    if (_allMarkersTimer >= allMarkersHoldTime)
                    {
                        TransitionTo(ScanState.Exporting);
                    }
                    break;

                case ScanState.Cooldown:
                    _cooldownTimer += Time.deltaTime;
                    if (_cooldownTimer >= cooldownDuration)
                    {
                        TransitionTo(ScanState.Idle);
                    }
                    break;
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Report marker detection status each frame.
        /// Called by the detection pipeline (via orchestrator wiring).
        /// </summary>
        public void ReportMarkerStatus(int detectedCount, int totalCount)
        {
            _expectedMarkerCount = totalCount;
            bool allDetected = (detectedCount == totalCount && totalCount > 0);

            // Idle → Scanning on first detection
            if (_state == ScanState.Idle && detectedCount > 0)
            {
                TransitionTo(ScanState.Scanning);
            }

            // Scanning → AllMarkersDetected when all found
            if (_state == ScanState.Scanning && allDetected)
            {
                TransitionTo(ScanState.AllMarkersDetected);
            }

            // AllMarkersDetected → Scanning if markers lost
            if (_state == ScanState.AllMarkersDetected && !allDetected)
            {
                _allMarkersTimer = 0f;
                TransitionTo(ScanState.Scanning);
            }
        }

        /// <summary>
        /// Notify that the image export completed successfully.
        /// Called by the detection pipeline after saving an image.
        /// </summary>
        public void NotifyExportComplete()
        {
            if (_state == ScanState.Exporting)
            {
                TransitionTo(ScanState.Success);
            }
        }

        /// <summary>
        /// Reset the scan cycle for a new user.
        /// Called externally when the experience is ready for the next scan
        /// (e.g. after a lantern hang interaction completes).
        /// </summary>
        public void ResetForNewScan()
        {
            if (_state == ScanState.Idle) return;
            TransitionTo(ScanState.Idle);
        }

        #endregion

        #region State Machine

        private void TransitionTo(ScanState newState)
        {
            if (_state == newState) return;

            ScanState old = _state;
            _state = newState;

            // Reset state-specific timers on entry
            switch (newState)
            {
                case ScanState.AllMarkersDetected:
                    _allMarkersTimer = 0f;
                    break;
                case ScanState.Cooldown:
                    _cooldownTimer = 0f;
                    break;
                case ScanState.Idle:
                    _allMarkersTimer = 0f;
                    _cooldownTimer = 0f;
                    break;
            }

            Debug.Log($"[ScanSession] {old} → {newState}");
            OnStateChanged?.Invoke(old, newState);

            // Fire specific events after general OnStateChanged
            switch (newState)
            {
                case ScanState.Scanning:
                    if (old == ScanState.Idle)
                        OnScanStarted?.Invoke();
                    break;
                case ScanState.Exporting:
                    OnExportRequested?.Invoke();
                    break;
                case ScanState.Success:
                    OnScanSuccess?.Invoke();
                    // If cooldown is 0, go directly to Cooldown → Idle
                    if (cooldownDuration <= 0f)
                    {
                        // Don't auto-transition to Idle; wait for external ResetForNewScan
                        // The Success state means "scan done, waiting for experience to finish"
                    }
                    break;
                case ScanState.Idle:
                    if (old != ScanState.Idle)
                        OnScanReset?.Invoke();
                    break;
            }
        }

        #endregion
    }
}
