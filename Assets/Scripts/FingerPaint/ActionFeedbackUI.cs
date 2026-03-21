using TMPro;
using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Reusable world-space popup that shows a brief feedback message
    /// (e.g. "Saved!", "Cleared!") and auto-dismisses.
    /// Uses TextMeshPro with optional SDF glow.
    /// </summary>
    public class ActionFeedbackUI : MonoBehaviour
    {
        [Header("Appearance")]
        [SerializeField] private float _displayDuration = 2.0f;
        [SerializeField] private float _distance = 0.5f;
        [SerializeField] private float _verticalOffset = 0.05f;

        [Header("Text")]
        [Tooltip("Optional TMP font asset. Leave empty for the default TMP font.")]
        [SerializeField] private TMP_FontAsset _font;

        [SerializeField] private Color _baseColor = new Color(0.5f, 1f, 0.6f);

        [Header("Glow (TMP Shader)")]
        [Tooltip("Enable the TMP SDF shader glow effect.")]
        [SerializeField] private bool _enableGlow = true;

        [Tooltip("Drag TMP_SDF.shader here (Assets/TextMesh Pro/Shaders/TMP_SDF.shader).")]
        [SerializeField] private Shader _sdfGlowShader;

        [SerializeField] private Color _glowColor = new Color(0.5f, 1f, 0.6f, 0.5f);
        [SerializeField] [Range(-1f, 1f)] private float _glowOffset = 0f;
        [SerializeField] [Range(0f, 1f)] private float _glowInner = 0.15f;
        [SerializeField] [Range(0f, 1f)] private float _glowOuter = 0.35f;
        [SerializeField] [Range(0f, 1f)] private float _glowPower = 0.6f;

        // ─── Runtime ────────────────────────────────────────────────────
        private Transform _root;
        private TextMeshPro _messageTMP;
        private TMPTextFactory.Result _textResult;
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

            _messageTMP.text = message;
            _messageTMP.color = _baseColor;
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
                _messageTMP.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, alpha);

                // Fade glow alpha too
                if (_enableGlow && _textResult.MaterialInstance != null)
                {
                    Color gc = _glowColor;
                    gc.a = _glowColor.a * alpha;
                    TMPTextFactory.SetGlowColor(_textResult.MaterialInstance, gc);
                }
            }
        }

        // ─── Build panel ────────────────────────────────────────────────

        private void BuildPanel()
        {
            _root = new GameObject("ActionFeedbackPanel").transform;
            _root.SetParent(transform, false);

            var cfg = TMPTextFactory.Config.Default;
            cfg.Name = "FeedbackText";
            cfg.Parent = _root;
            cfg.FontSize = 42f;
            cfg.Color = _baseColor;
            cfg.LocalScale = 0.006f;
            cfg.RectSize = new Vector2(400f, 80f);
            cfg.Font = _font;
            cfg.GlowShader = _sdfGlowShader;
            cfg.EnableGlow = _enableGlow;
            cfg.Glow = GetGlowSettings();

            _textResult = TMPTextFactory.Create(cfg);
            _messageTMP = _textResult.TMP;

            _root.gameObject.SetActive(false);
            _isBuilt = true;
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
