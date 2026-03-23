using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Keeps an object below the camera, following only the Y rotation (yaw).
    /// Useful for tutorial sheets, HUD panels, etc.
    /// </summary>
    public class FollowCameraYaw : MonoBehaviour
    {
        [Tooltip("Vertical offset from the camera (negative = below)")]
        [SerializeField] private float _heightOffset = -0.6f;

        [Tooltip("Forward offset from the camera (positive = in front)")]
        [SerializeField] private float _forwardOffset = 0.5f;

        [Tooltip("How fast the object follows the camera rotation (0 = instant)")]
        [SerializeField] private float _smoothSpeed = 5f;

        [Tooltip("If true, faces the camera. If false, faces the same direction as camera.")]
        [SerializeField] private bool _faceCamera = true;

        private Transform _cam;
        private float _currentYaw;

        private void Start()
        {
            _cam = Camera.main != null ? Camera.main.transform : null;
            if (_cam == null)
            {
                foreach (var cam in FindObjectsOfType<Camera>())
                {
                    if (cam.isActiveAndEnabled)
                    {
                        _cam = cam.transform;
                        break;
                    }
                }
            }

            if (_cam != null)
                _currentYaw = _cam.eulerAngles.y;
        }

        private void LateUpdate()
        {
            if (_cam == null) return;

            float targetYaw = _cam.eulerAngles.y;

            // Smooth rotation to avoid jittery movement
            if (_smoothSpeed > 0f)
                _currentYaw = Mathf.LerpAngle(_currentYaw, targetYaw, _smoothSpeed * Time.deltaTime);
            else
                _currentYaw = targetYaw;

            Quaternion yawRotation = Quaternion.Euler(0f, _currentYaw, 0f);

            // Position: camera position + offset below + offset forward (in yaw direction)
            Vector3 forward = yawRotation * Vector3.forward;
            Vector3 pos = _cam.position
                        + Vector3.up * _heightOffset
                        + forward * _forwardOffset;

            transform.position = pos;

            // Rotation: face the camera or face same direction
            if (_faceCamera)
                transform.rotation = Quaternion.Euler(0f, _currentYaw + 180f, 0f);
            else
                transform.rotation = yawRotation;
        }
    }
}
