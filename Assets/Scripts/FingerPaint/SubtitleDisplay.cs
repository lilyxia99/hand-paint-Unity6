using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// World-space subtitle display for VR — uses TextMesh + Quad (same proven
    /// approach as VoiceDebugUI). No Canvas or UGUI dependencies.
    /// Floats in front of the player's head so it's visible on Quest.
    /// </summary>
    public class SubtitleDisplay : MonoBehaviour
    {
        [Header("Positioning")]
        [Tooltip("Follow the VR camera each frame so the subtitle stays in view.")]
        [SerializeField] private bool _followCamera = true;

        [Tooltip("Distance in front of the camera (metres).")]
        [SerializeField] private float _distance = 1.5f;

        [Tooltip("Vertical offset from eye level (negative = below).")]
        [SerializeField] private float _verticalOffset = -0.25f;

        [Tooltip("Horizontal offset (positive = right).")]
        [SerializeField] private float _horizontalOffset = 0f;

        [Tooltip("Smoothing speed for position tracking (higher = snappier).")]
        [SerializeField] [Range(1f, 20f)] private float _followSpeed = 5f;

        [Header("Panel")]
        [Tooltip("Width of the background panel in world units.")]
        [SerializeField] private float _panelWidth = 0.5f;

        [Tooltip("Height of the background panel in world units.")]
        [SerializeField] private float _panelHeight = 0.08f;

        [Tooltip("Background color (set alpha to 0 to hide background).")]
        [SerializeField] private Color _bgColor = new Color(0f, 0f, 0f, 0.65f);

        [Tooltip("Horizontal padding around text (world units).")]
        [SerializeField] private float _paddingH = 0.02f;

        [Tooltip("Vertical padding around text (world units).")]
        [SerializeField] private float _paddingV = 0.01f;

        [Header("Text")]
        [SerializeField] private int _fontSize = 48;
        [SerializeField] private float _characterSize = 0.005f;
        [SerializeField] private Color _textColor = Color.white;

        // ─── Runtime ────────────────────────────────────────────────────
        private Transform _root;
        private TextMesh _textMesh;
        private MeshRenderer _textRenderer;
        private Transform _bgQuad;
        private Material _bgMat;
        private Camera _cam;

        private string _currentText;

        // ─── Public API ─────────────────────────────────────────────────

        /// <summary>Show subtitle text. Pass null or empty to clear.</summary>
        public void SetText(string text)
        {
            if (text == _currentText) return;
            _currentText = text;

            if (_textMesh == null) return;

            if (string.IsNullOrEmpty(text))
            {
                _textMesh.text = "";
                if (_bgQuad != null) _bgQuad.gameObject.SetActive(false);
            }
            else
            {
                _textMesh.text = text;
                if (_bgQuad != null)
                {
                    _bgQuad.gameObject.SetActive(true);
                    ResizeBackground();
                }
            }
        }

        /// <summary>Clear the subtitle.</summary>
        public void Clear() => SetText(null);

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Start()
        {
            _cam = Camera.main;
            BuildPanel();
        }

        private void LateUpdate()
        {
            if (!_followCamera || _root == null) return;

            if (_cam == null)
                _cam = Camera.main;
            if (_cam == null) return;

            FollowHead();
        }

        private void OnDestroy()
        {
            if (_bgMat != null) Destroy(_bgMat);
        }

        // ─── Head tracking (same approach as VoiceDebugUI) ──────────────

        private void FollowHead()
        {
            var camT = _cam.transform;
            Vector3 forward = camT.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = camT.forward;
            forward.Normalize();

            Vector3 right = camT.right;
            right.y = 0f;
            right.Normalize();

            Vector3 target = camT.position
                           + forward * _distance
                           + Vector3.up * _verticalOffset
                           + right * _horizontalOffset;

            _root.position = Vector3.Lerp(_root.position, target, Time.deltaTime * _followSpeed);
            _root.rotation = Quaternion.LookRotation(
                _root.position - camT.position, Vector3.up);
        }

        // ─── Build the panel from primitives ────────────────────────────

        private void BuildPanel()
        {
            _root = new GameObject("SubtitlePanel").transform;
            _root.SetParent(transform, false);

            // --- Background quad ---
            var bgGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgGO.name = "SubtitleBG";
            bgGO.transform.SetParent(_root, false);
            bgGO.transform.localScale = new Vector3(_panelWidth, _panelHeight, 1f);
            bgGO.transform.localPosition = Vector3.zero;

            // Remove collider
            var col = bgGO.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Transparent unlit material
            var shader = Shader.Find("Unlit/Color")
                      ?? Shader.Find("Universal Render Pipeline/Unlit");
            _bgMat = new Material(shader);
            _bgMat.color = _bgColor;
            _bgMat.renderQueue = 3000; // render on top
            bgGO.GetComponent<MeshRenderer>().sharedMaterial = _bgMat;

            _bgQuad = bgGO.transform;
            _bgQuad.gameObject.SetActive(false); // hidden until first subtitle

            // --- Text (TextMesh — same as VoiceDebugUI) ---
            var textGO = new GameObject("SubtitleText");
            textGO.transform.SetParent(_root, false);

            _textMesh = textGO.AddComponent<TextMesh>();
            _textRenderer = textGO.GetComponent<MeshRenderer>();
            _textMesh.fontSize = _fontSize;
            _textMesh.characterSize = _characterSize;
            _textMesh.anchor = TextAnchor.MiddleCenter;
            _textMesh.alignment = TextAlignment.Center;
            _textMesh.color = _textColor;
            _textMesh.text = "";

            // Place text slightly in front of the background
            textGO.transform.localPosition = new Vector3(0f, 0f, -0.002f);

            // Initial position
            if (_cam != null)
            {
                _root.position = _cam.transform.position
                    + _cam.transform.forward * _distance
                    + Vector3.up * _verticalOffset;
            }
        }

        // ─── Dynamic background sizing ──────────────────────────────────

        private void ResizeBackground()
        {
            if (_textRenderer == null || _bgQuad == null) return;

            // Force mesh rebuild so bounds are up-to-date
            _textMesh.text = _textMesh.text;

            // Get text bounds in world space, then convert size to root-local
            Bounds bounds = _textRenderer.bounds;
            Vector3 size = _root.InverseTransformVector(bounds.size);

            float w = Mathf.Abs(size.x) + _paddingH * 2f;
            float h = Mathf.Abs(size.y) + _paddingV * 2f;

            // Enforce minimum so the panel doesn't collapse for very short text
            w = Mathf.Max(w, 0.05f);
            h = Mathf.Max(h, 0.02f);

            _bgQuad.localScale = new Vector3(w, h, 1f);
        }
    }
}
