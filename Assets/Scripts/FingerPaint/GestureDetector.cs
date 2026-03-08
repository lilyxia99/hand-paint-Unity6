using UnityEngine;
using UnityEngine.Events;

namespace FingerPaint
{
    /// <summary>
    /// Detects a "save" gesture: both hands doing thumbs-up simultaneously.
    /// Fires an event after the gesture is held for a configurable duration
    /// to avoid accidental triggers. Includes a cooldown to prevent repeat fires.
    /// </summary>
    public class GestureDetector : MonoBehaviour
    {
        [SerializeField] private HandTrackingManager _handTracking;
        [SerializeField] private MeshExporter _exporter;
        [SerializeField] private ClearGestureDetector _clearDetector;

        [Header("Gesture Timing")]
        [SerializeField] private float _holdDuration = 1.0f;   // seconds to hold before trigger
        [SerializeField] private float _cooldown = 3.0f;        // seconds between triggers

        [Header("Events")]
        public UnityEvent OnSaveGestureDetected;

        private float _holdTimer;
        private float _cooldownTimer;
        private bool _gestureActive;

        private void Update()
        {
            if (_handTracking == null)
                return;

            // Cooldown
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= Time.deltaTime;
                return;
            }

            // Suppress save gesture while clear confirmation is active
            // (thumbs-up means "confirm clear" during that window, not "save")
            if (_clearDetector != null && _clearDetector.IsWaitingForConfirmation)
            {
                _holdTimer = 0f;
                _gestureActive = false;
                return;
            }

            bool bothThumbsUp = _handTracking.IsThumbsUp(true) && _handTracking.IsThumbsUp(false);

            if (bothThumbsUp)
            {
                _holdTimer += Time.deltaTime;

                if (!_gestureActive && _holdTimer >= _holdDuration)
                {
                    _gestureActive = true;
                    TriggerSave();
                }
            }
            else
            {
                _holdTimer = 0f;
                _gestureActive = false;
            }
        }

        private void TriggerSave()
        {
            Debug.Log("[GestureDetector] Save gesture detected — exporting mesh.");
            _cooldownTimer = _cooldown;

            if (_exporter != null)
                _exporter.Export();

            OnSaveGestureDetected?.Invoke();
        }
    }
}
