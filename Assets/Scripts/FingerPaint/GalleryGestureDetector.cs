using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Detects a "gallery toggle" gesture: look at LEFT palm + thumb-middle pinch.
    /// Requires PalmMenuUI.IsGazingLeftPalm to be true (palm gaze guard).
    /// </summary>
    public class GalleryGestureDetector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HandTrackingManager _handTracking;
        [SerializeField] private GalleryUI _galleryUI;
        [SerializeField] private PalmMenuUI _palmMenu;

        [Header("Gesture Settings")]
        [Tooltip("Maximum distance between thumb and middle finger tips for pinch (meters).")]
        [SerializeField] private float _pinchThreshold = 0.03f;

        [Tooltip("How long the pinch must be held before triggering (seconds).")]
        [SerializeField] private float _holdDuration = 0.6f;

        [Tooltip("Cooldown after toggle (seconds).")]
        [SerializeField] private float _cooldown = 2.0f;

        // ─── State ──────────────────────────────────────────────────────

        private float _holdTimer;
        private float _cooldownTimer;
        private bool _triggered; // prevents re-trigger while still pinching

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Update()
        {
            if (_handTracking == null || _galleryUI == null)
                return;

            // Cooldown
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= Time.deltaTime;
                return;
            }

            if (DetectGalleryPinch())
            {
                _holdTimer += Time.deltaTime;

                if (!_triggered && _holdTimer >= _holdDuration)
                {
                    _triggered = true;
                    Debug.Log("[GalleryGesture] Gallery gesture detected — toggling gallery.");
                    _galleryUI.Toggle();
                    _cooldownTimer = _cooldown;
                }
            }
            else
            {
                _holdTimer = 0f;
                _triggered = false;
            }
        }

        // ─── Gesture detection ──────────────────────────────────────────

        private bool DetectGalleryPinch()
        {
            // If PalmMenuUI is set, require gaze at left palm
            if (_palmMenu != null && !_palmMenu.IsGazingLeftPalm)
                return false;

            // Fallback: if no PalmMenuUI assigned, do our own basic palm check
            if (_palmMenu == null)
            {
                Camera cam = Camera.main;
                if (cam == null) return false;

                if (!_handTracking.TryGetPalmPose(true, out Vector3 palmPos, out Vector3 palmNormal))
                    return false;

                Vector3 dirToCamera = (cam.transform.position - palmPos).normalized;
                if (Vector3.Dot(palmNormal, dirToCamera) < 0.3f)
                    return false;

                Vector3 dirToPalm = (palmPos - cam.transform.position).normalized;
                if (Vector3.Dot(cam.transform.forward, dirToPalm) < 0.7f)
                    return false;
            }

            // Left hand thumb + middle finger pinch
            int thumbIdx = (int)HandTrackingManager.FingerID.LeftThumb;
            int middleIdx = (int)HandTrackingManager.FingerID.LeftMiddle;

            ref var thumb = ref _handTracking.Fingers[thumbIdx];
            ref var middle = ref _handTracking.Fingers[middleIdx];

            if (!thumb.IsTracked || !middle.IsTracked)
                return false;

            float dist = Vector3.Distance(thumb.TipPosition, middle.TipPosition);
            return dist < _pinchThreshold;
        }
    }
}
