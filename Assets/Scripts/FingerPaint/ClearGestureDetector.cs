using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Detects a "clear canvas" gesture: thumb tip + middle finger tip pinch together
    /// with the palm facing toward the user. Shows a confirmation panel; thumbs-up
    /// (both hands) confirms the clear, or it auto-cancels after a timeout.
    /// </summary>
    public class ClearGestureDetector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HandTrackingManager _handTracking;
        [SerializeField] private FingerPainter _painter;
        [SerializeField] private ClearConfirmationUI _confirmUI;

        [Header("Gesture Settings")]
        [Tooltip("Maximum distance between thumb and middle finger tips to count as pinch (meters).")]
        [SerializeField] private float _pinchThreshold = 0.025f;

        [Tooltip("Minimum dot product between palm normal and direction-to-camera for palm-facing-self check.")]
        [SerializeField] private float _palmFacingDotThreshold = 0.4f;

        [Tooltip("How long the pinch must be held before triggering the confirmation (seconds).")]
        [SerializeField] private float _holdDuration = 0.5f;

        [Tooltip("Cooldown after a clear or cancel (seconds).")]
        [SerializeField] private float _cooldown = 2.0f;

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

            // Check for thumbs-up (both hands) to confirm
            bool bothThumbsUp = _handTracking.IsThumbsUp(true)
                             && _handTracking.IsThumbsUp(false);

            if (bothThumbsUp)
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
        /// Detects thumb + middle finger pinch with palm facing toward the user.
        /// Checks either hand.
        /// </summary>
        private bool DetectClearPinch()
        {
            Camera cam = Camera.main;
            if (cam == null) return false;

            for (int hand = 0; hand < 2; hand++)
            {
                bool isLeft = hand == 0;
                int offset = isLeft ? 0 : 5;

                int thumbIdx = offset + 0;  // Thumb
                int middleIdx = offset + 2; // Middle

                ref var thumb = ref _handTracking.Fingers[thumbIdx];
                ref var middle = ref _handTracking.Fingers[middleIdx];

                if (!thumb.IsTracked || !middle.IsTracked)
                    continue;

                // Check pinch distance
                float dist = Vector3.Distance(thumb.TipPosition, middle.TipPosition);
                if (dist > _pinchThreshold)
                    continue;

                // Check palm facing toward the user (camera)
                if (!_handTracking.TryGetPalmNormal(isLeft, out Vector3 palmNormal))
                    continue;

                Vector3 handCenter = (thumb.TipPosition + middle.TipPosition) * 0.5f;
                Vector3 dirToCamera = (cam.transform.position - handCenter).normalized;

                float dot = Vector3.Dot(palmNormal, dirToCamera);
                if (dot < _palmFacingDotThreshold)
                    continue;

                return true;
            }

            return false;
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
        }
    }
}
