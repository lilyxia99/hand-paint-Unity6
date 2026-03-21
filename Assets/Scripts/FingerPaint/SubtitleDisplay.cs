using TMPro;
using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// World-space subtitle display for VR — uses TextMeshPro with optional SDF glow.
    /// Floats in front of the player's head so it's always visible on Quest.
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

        [Header("Text")]
        [SerializeField] private float _fontSize = 36f;
        [SerializeField] private Color _textColor = Color.white;

        [Tooltip("Optional TMP font asset. Leave empty to use the TMP default font.")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("Glow (TMP Shader)")]
        [Tooltip("Enable the TMP SDF shader glow effect.")]
        [SerializeField] private bool _enableGlow = true;

        [Tooltip("Drag TMP_SDF.shader here (Assets/TextMesh Pro/Shaders/TMP_SDF.shader).")]
        [SerializeField] private Shader _sdfGlowShader;

        [SerializeField] private Color _glowColor = new Color(0.5f, 0.8f, 1f, 0.5f);
        [SerializeField] [Range(-1f, 1f)] private float _glowOffset = 0f;
        [SerializeField] [Range(0f, 1f)] private float _glowInner = 0.15f;
        [SerializeField] [Range(0f, 1f)] private float _glowOuter = 0.35f;
        [SerializeField] [Range(0f, 1f)] private float _glowPower = 0.6f;

        // ─── Runtime ────────────────────────────────────────────────────
        private Transform _root;
        private TextMeshPro _tmp;
        private TMPTextFactory.Result _textResult;
        private Camera _cam;
        private string _currentText;
        private bool _glowStateCached;

        // ─── Public API ─────────────────────────────────────────────────

        /// <summary>Show subtitle text. Pass null or empty to clear.</summary>
        public void SetText(string text)
        {
            if (text == _currentText) return;
            _currentText = text;

            if (_tmp == null) return;

            if (string.IsNullOrEmpty(text))
            {
                _tmp.text = "";
                _root.gameObject.SetActive(false);
            }
            else
            {
                _tmp.text = text;
                _root.gameObject.SetActive(true);
            }
        }

        /// <summary>Clear the subtitle.</summary>
        public void Clear() => SetText(null);

        /// <summary>Turn glow on or off at runtime.</summary>
        public void SetGlow(bool enabled)
        {
            _enableGlow = enabled;
            ApplyGlow();
        }

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

            // Detect inspector toggle changes at runtime
            if (_enableGlow != _glowStateCached)
                ApplyGlow();
        }

        // ─── Head tracking ──────────────────────────────────────────────

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

        // ─── Build ──────────────────────────────────────────────────────

        private void BuildPanel()
        {
            _root = new GameObject("SubtitlePanel").transform;
            _root.SetParent(transform, false);

            var cfg = TMPTextFactory.Config.Default;
            cfg.Name = "SubtitleText";
            cfg.Parent = _root;
            cfg.FontSize = _fontSize;
            cfg.Color = _textColor;
            cfg.LocalScale = 0.01f;
            cfg.RectSize = new Vector2(400f, 100f);
            cfg.Font = _font;
            cfg.GlowShader = _sdfGlowShader;
            cfg.EnableGlow = _enableGlow;
            cfg.Glow = GetGlowSettings();

            _textResult = TMPTextFactory.Create(cfg);
            _tmp = _textResult.TMP;
            _glowStateCached = _enableGlow;

            // Start hidden
            _root.gameObject.SetActive(false);

            // Initial position
            if (_cam != null)
            {
                _root.position = _cam.transform.position
                    + _cam.transform.forward * _distance
                    + Vector3.up * _verticalOffset;
            }
        }

        // ─── Glow ───────────────────────────────────────────────────────

        private void ApplyGlow()
        {
            _glowStateCached = _enableGlow;
            TMPTextFactory.ApplyGlow(_textResult, _sdfGlowShader, _enableGlow, GetGlowSettings());
        }

        private TMPTextFactory.GlowSettings GetGlowSettings()
        {
            return new TMPTextFactory.GlowSettings
            {
                Color = _glowColor,
                Offset = _glowOffset,
                Inner = _glowInner,
                Outer = _glowOuter,
                Power = _glowPower,
            };
        }
    }
}
