using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Reusable world-space popup that shows a brief feedback message
    /// (e.g. "Saved!", "Cleared!") and auto-dismisses.
    /// Multi-layer glow text — no black background.
    /// </summary>
    public class ActionFeedbackUI : MonoBehaviour
    {
        [Header("Appearance")]
        [SerializeField] private float _displayDuration = 2.0f;
        [SerializeField] private float _distance = 0.5f;
        [SerializeField] private float _verticalOffset = 0.05f;

        private static readonly Color _baseColor = new Color(0.5f, 1f, 0.6f);

        // ─── Runtime ────────────────────────────────────────────────────
        private Transform _root;
        private TextMesh _messageText;
        private TextMesh[] _glows;
        private Camera _cam;
        private bool _isBuilt;
        private float _showTimer;

        // ─── Public API ─────────────────────────────────────────────────

        /// <summary>Show a message that auto-dismisses after _displayDuration seconds.</summary>
        public void Show(string message)
        {
            _cam = Camera.main;

            if (!_isBuilt)
                BuildPanel();

            _messageText.text = message;
            TextGlowHelper.SetText(_glows, message);
            _messageText.color = _baseColor;
            TextGlowHelper.SetColor(_glows, _baseColor);
            _showTimer = 0f;
            _root.gameObject.SetActive(true);

            // Snap to position
            if (_cam != null)
            {
                var camT = _cam.transform;
                Vector3 forward = camT.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f)
                    forward = camT.forward;
                forward.Normalize();

                _root.position = camT.position
                    + forward * _distance
                    + Vector3.up * _verticalOffset;
                _root.rotation = Quaternion.LookRotation(
                    _root.position - camT.position, Vector3.up);
            }
        }

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (_root == null || !_root.gameObject.activeSelf)
                return;

            _showTimer += Time.deltaTime;

            // Follow head
            if (_cam != null)
            {
                var camT = _cam.transform;
                Vector3 forward = camT.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f)
                    forward = camT.forward;
                forward.Normalize();

                Vector3 target = camT.position
                    + forward * _distance
                    + Vector3.up * _verticalOffset;

                _root.position = Vector3.Lerp(_root.position, target, Time.deltaTime * 5f);
                _root.rotation = Quaternion.LookRotation(
                    _root.position - camT.position, Vector3.up);
            }

            // Fade out in the last 0.5s, then hide
            float fadeStart = _displayDuration - 0.5f;
            if (_showTimer >= _displayDuration)
            {
                _root.gameObject.SetActive(false);
            }
            else if (_showTimer > fadeStart)
            {
                float alpha = 1f - (_showTimer - fadeStart) / 0.5f;
                _messageText.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, alpha);
                TextGlowHelper.SetAlphaMultiplier(_glows, _baseColor, alpha);
            }
        }

        // ─── Build panel ────────────────────────────────────────────────

        private void BuildPanel()
        {
            _root = new GameObject("ActionFeedbackPanel").transform;
            _root.SetParent(transform, false);

            // Main text
            var textGO = new GameObject("FeedbackText");
            textGO.transform.SetParent(_root, false);
            _messageText = textGO.AddComponent<TextMesh>();
            _messageText.fontSize = 42;
            _messageText.characterSize = 0.006f;
            _messageText.anchor = TextAnchor.MiddleCenter;
            _messageText.alignment = TextAlignment.Center;
            _messageText.color = _baseColor;
            _messageText.text = "";
            textGO.transform.localPosition = Vector3.zero;

            // Multi-layer glow
            _glows = TextGlowHelper.AddGlow(_root, _messageText, _baseColor);

            _root.gameObject.SetActive(false);
            _isBuilt = true;
        }
    }
}
