using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Reusable world-space popup that shows a brief feedback message
    /// (e.g. "✓ Saved!", "✓ Cleared!") and auto-dismisses.
    /// Uses TextMesh + Quad — same pattern as other VR panels.
    /// </summary>
    public class ActionFeedbackUI : MonoBehaviour
    {
        [Header("Appearance")]
        [SerializeField] private float _displayDuration = 2.0f;
        [SerializeField] private float _distance = 0.5f;
        [SerializeField] private float _verticalOffset = 0.05f;
        [SerializeField] private float _panelWidth = 0.28f;
        [SerializeField] private float _panelHeight = 0.07f;

        // ─── Runtime ────────────────────────────────────────────────────
        private Transform _root;
        private TextMesh _messageText;
        private Material _bgMat;
        private Camera _cam;
        private bool _isBuilt;
        private float _showTimer;
        private float _fadeAlpha;

        private static readonly Color ColorBgSuccess = new Color(0.02f, 0.15f, 0.05f, 0.88f);
        private static readonly Color ColorTextSuccess = new Color(0.5f, 1f, 0.6f);

        // ─── Public API ─────────────────────────────────────────────────

        /// <summary>Show a message that auto-dismisses after _displayDuration seconds.</summary>
        public void Show(string message)
        {
            _cam = Camera.main;

            if (!_isBuilt)
                BuildPanel();

            _messageText.text = message;
            _showTimer = 0f;
            _fadeAlpha = 1f;
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

            // Fade out in the last 0.5s
            float fadeStart = _displayDuration - 0.5f;
            if (_showTimer >= _displayDuration)
            {
                _root.gameObject.SetActive(false);
            }
            else if (_showTimer > fadeStart)
            {
                _fadeAlpha = 1f - (_showTimer - fadeStart) / 0.5f;
                _messageText.color = new Color(
                    ColorTextSuccess.r, ColorTextSuccess.g, ColorTextSuccess.b, _fadeAlpha);
                _bgMat.color = new Color(
                    ColorBgSuccess.r, ColorBgSuccess.g, ColorBgSuccess.b,
                    ColorBgSuccess.a * _fadeAlpha);
            }
        }

        private void OnDestroy()
        {
            if (_bgMat != null) Destroy(_bgMat);
        }

        // ─── Build panel ────────────────────────────────────────────────

        private void BuildPanel()
        {
            _root = new GameObject("ActionFeedbackPanel").transform;
            _root.SetParent(transform, false);

            // Background
            var bgGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgGO.name = "FeedbackBG";
            bgGO.transform.SetParent(_root, false);
            bgGO.transform.localScale = new Vector3(_panelWidth, _panelHeight, 1f);
            bgGO.transform.localPosition = Vector3.zero;

            var col = bgGO.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _bgMat = CreateUnlitMat(ColorBgSuccess);
            _bgMat.renderQueue = 3000;
            bgGO.GetComponent<MeshRenderer>().sharedMaterial = _bgMat;

            // Message text
            var textGO = new GameObject("FeedbackText");
            textGO.transform.SetParent(_root, false);

            _messageText = textGO.AddComponent<TextMesh>();
            _messageText.fontSize = 42;
            _messageText.characterSize = 0.006f;
            _messageText.anchor = TextAnchor.MiddleCenter;
            _messageText.alignment = TextAlignment.Center;
            _messageText.color = ColorTextSuccess;
            _messageText.text = "";

            textGO.transform.localPosition = new Vector3(0f, 0f, -0.002f);

            _root.gameObject.SetActive(false);
            _isBuilt = true;
        }

        private static Material CreateUnlitMat(Color color)
        {
            var shader = Shader.Find("Unlit/Color")
                      ?? Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader);
            mat.color = color;
            return mat;
        }
    }
}
