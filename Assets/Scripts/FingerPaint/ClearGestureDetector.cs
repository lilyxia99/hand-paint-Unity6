using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Detects a "clear canvas" gesture: thumb tip + middle finger tip pinch together
    /// with the palm facing toward the user. Shows a confirmation panel; thumbs-up
    /// (either hand) confirms the clear, or it auto-cancels after a timeout.
    /// </summary>
    public class ClearGestureDetector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HandTrackingManager _handTracking;
        [SerializeField] private FingerPainter _painter;
        [SerializeField] private ClearConfirmationUI _confirmUI;
        [SerializeField] private PalmMenuUI _palmMenu;
        [SerializeField] private ActionFeedbackUI _feedbackUI;

        [Header("Gesture Settings")]
        [Tooltip("Maximum distance between thumb and middle finger tips to count as pinch (meters).")]
        [SerializeField] private float _pinchThreshold = 0.025f;

        [Tooltip("Minimum dot product between palm normal and direction-to-camera for palm-facing-self check.")]
        [SerializeField] private float _palmFacingDotThreshold = 0.4f;

        [Tooltip("How long the pinch must be held before triggering the confirmation (seconds).")]
        [SerializeField] private float _holdDuration = 0.5f;

        [Tooltip("Cooldown after a clear or cancel (seconds).")]
        [SerializeField] private float _cooldown = 2.0f;

        [Header("Messages")]
        [SerializeField] [TextArea(1, 3)] private string _clearedMessage = "\u2713 Cleared!";

        [Header("Confirmation")]
        [Tooltip("Time before the confirmation auto-cancels (seconds).")]
        [SerializeField] private float _confirmationTimeout = 5.0f;

        // ─── State machine ──────────────────────────────────────────────

        private enum State { Idle, WaitingForConfirmation }

        private State _state = State.Idle;
        private float _holdTimer;
        private float _cooldownTimer;
        private float _confirmationTimer;

        // ─── Public state ───────────────────────────────────────────────

        /// <summary>
        /// True when the confirmation panel is visible and waiting for thumbs-up.
        /// Used by GestureDetector to suppress the save gesture.
        /// </summary>
        public bool IsWaitingForConfirmation => _state == State.WaitingForConfirmation;

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Update()
        {
            if (_handTracking == null)
                return;

            switch (_state)
            {
                case State.Idle:
                    UpdateIdle();
                    break;

                case State.WaitingForConfirmation:
                    UpdateWaitingForConfirmation();
                    break;
            }
        }

        // ─── Idle state: detect pinch gesture ───────────────────────────

        private void UpdateIdle()
        {
            // Cooldown
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= Time.deltaTime;
                return;
            }

            if (DetectClearPinch())
            {
                _holdTimer += Time.deltaTime;

                if (_holdTimer >= _holdDuration)
                {
                    // Trigger confirmation
                    _state = State.WaitingForConfirmation;
                    _confirmationTimer = 0f;
                    _holdTimer = 0f;
                    ShowConfirmation();
                }
            }
            else
            {
                _holdTimer = 0f;
            }
        }

        // ─── Waiting state: thumbs-up to confirm, timeout to cancel ────

        private void UpdateWaitingForConfirmation()
        {
            _confirmationTimer += Time.deltaTime;

            // Update UI timer
            if (_confirmUI != null)
            {
                _confirmUI.ElapsedTime = _confirmationTimer;
            }

            // Timeout → cancel
            if (_confirmationTimer >= _confirmationTimeout)
            {
                Debug.Log("[ClearGesture] Confirmation timed out — cancelled.");
                HideConfirmation();
                _state = State.Idle;
                _cooldownTimer = _cooldown;
                return;
            }

            // Check for thumbs-up (either hand) to confirm
            bool eitherThumbsUp = _handTracking.IsThumbsUp(true)
                              || _handTracking.IsThumbsUp(false);

            if (eitherThumbsUp)
            {
                Debug.Log("[ClearGesture] Confirmed — clearing canvas.");
                ExecuteClear();
                HideConfirmation();
                _state = State.Idle;
                _cooldownTimer = _cooldown;
            }
        }

        // ─── Gesture detection ──────────────────────────────────────────

        /// <summary>
        /// Detects thumb + middle finger pinch on the RIGHT hand only,
        /// guarded by palm gaze (user must be looking at the palm).
        /// Falls back to own palm check if PalmMenuUI is not assigned.
        /// </summary>
        private bool DetectClearPinch()
        {
            // If PalmMenuUI is set, use its gaze state
            if (_palmMenu != null)
            {
                if (!_palmMenu.IsGazingRightPalm)
                    return false;
            }
            else
            {
                // Fallback: do our own palm gaze check
                Camera cam = Camera.main;
                if (cam == null) return false;

                if (!_handTracking.TryGetPalmPose(false, out Vector3 palmPos, out Vector3 palmNormal))
                    return false;

                Vector3 dirToCamera = (cam.transform.position - palmPos).normalized;
                if (Vector3.Dot(palmNormal, dirToCamera) < _palmFacingDotThreshold)
                    return false;

                Vector3 dirToPalm = (palmPos - cam.transform.position).normalized;
                if (Vector3.Dot(cam.transform.forward, dirToPalm) < 0.7f)
                    return false;
            }

            // Right hand only
            int thumbIdx = (int)HandTrackingManager.FingerID.RightThumb;
            int middleIdx = (int)HandTrackingManager.FingerID.RightMiddle;

            ref var thumb = ref _handTracking.Fingers[thumbIdx];
            ref var middle = ref _handTracking.Fingers[middleIdx];

            if (!thumb.IsTracked || !middle.IsTracked)
                return false;

            // Check pinch distance
            float dist = Vector3.Distance(thumb.TipPosition, middle.TipPosition);
            return dist < _pinchThreshold;
        }

        // ─── Actions ────────────────────────────────────────────────────

        private void ShowConfirmation()
        {
            if (_confirmUI != null)
            {
                _confirmUI.TimeoutDuration = _confirmationTimeout;
                _confirmUI.Show();
            }
        }

        private void HideConfirmation()
        {
            if (_confirmUI != null)
                _confirmUI.Hide();
        }

        private void ExecuteClear()
        {
            if (_painter != null)
                _painter.ClearAll();

            if (_feedbackUI != null)
                _feedbackUI.Show(_clearedMessage);
        }
    }
}
