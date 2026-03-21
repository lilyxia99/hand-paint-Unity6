using TMPro;
using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// World-space subtitle display for VR — uses TextMeshPro with optional shader glow.
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

        [Tooltip("Drag the 'TextMeshPro/Distance Field' shader here (Assets/TextMesh Pro/Shaders/TMP_SDF.shader). Required for glow to work.")]
        [SerializeField] private Shader _sdfGlowShader;

        [Tooltip("Glow color.")]
        [SerializeField] private Color _glowColor = new Color(0.5f, 0.8f, 1f, 0.5f);

        [Tooltip("Glow offset along the SDF. Negative = inside, positive = outside.")]
        [SerializeField] [Range(-1f, 1f)] private float _glowOffset = 0f;

        [Tooltip("Inner softness of the glow.")]
        [SerializeField] [Range(0f, 1f)] private float _glowInner = 0.15f;

        [Tooltip("Outer softness of the glow.")]
        [SerializeField] [Range(0f, 1f)] private float _glowOuter = 0.35f;

        [Tooltip("Falloff power of the glow (lower = softer).")]
        [SerializeField] [Range(0f, 1f)] private float _glowPower = 0.6f;

        // ─── Runtime ────────────────────────────────────────────────────
        private Transform _root;
        private TextMeshPro _tmp;
        private Material _matInstance;
        private Camera _cam;
        private string _currentText;
        private bool _glowStateCached;

        // ─── Shader property IDs ────────────────────────────────────────
        private static readonly int ID_GlowColor  = Shader.PropertyToID("_GlowColor");
        private static readonly int ID_GlowOffset = Shader.PropertyToID("_GlowOffset");
        private static readonly int ID_GlowInner  = Shader.PropertyToID("_GlowInner");
        private static readonly int ID_GlowOuter  = Shader.PropertyToID("_GlowOuter");
        private static readonly int ID_GlowPower  = Shader.PropertyToID("_GlowPower");

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

            // TextMeshPro (world-space 3D text)
            var textGO = new GameObject("SubtitleText");
            textGO.transform.SetParent(_root, false);
            _tmp = textGO.AddComponent<TextMeshPro>();

            // Apply custom font if assigned
            if (_font != null)
                _tmp.font = _font;

            _tmp.fontSize = _fontSize;
            _tmp.color = _textColor;
            _tmp.alignment = TextAlignmentOptions.Center;
            _tmp.enableWordWrapping = true;

            // Scale down — TMP world space is 1 unit = 1 metre; 0.01 brings it
            // to a readable subtitle size at ~1.5 m viewing distance.
            textGO.transform.localScale = Vector3.one * 0.01f;

            var rect = _tmp.rectTransform;
            rect.sizeDelta = new Vector2(400f, 100f); // large in local units, tiny after scale
            rect.localPosition = Vector3.zero;

            _tmp.text = "";

            // Create a material instance so glow changes don't affect other TMP objects
            _matInstance = new Material(_tmp.fontSharedMaterial);
            _tmp.fontMaterial = _matInstance;

            ApplyGlow();

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
            if (_matInstance == null) return;

            if (_enableGlow && _sdfGlowShader != null)
            {
                // Swap to the full SDF shader which supports GLOW_ON
                _matInstance.shader = _sdfGlowShader;
                _matInstance.EnableKeyword("GLOW_ON");
                _matInstance.SetColor(ID_GlowColor,  _glowColor);
                _matInstance.SetFloat(ID_GlowOffset, _glowOffset);
                _matInstance.SetFloat(ID_GlowInner,  _glowInner);
                _matInstance.SetFloat(ID_GlowOuter,  _glowOuter);
                _matInstance.SetFloat(ID_GlowPower,  _glowPower);
            }
            else
            {
                _matInstance.DisableKeyword("GLOW_ON");
            }
        }
    }
}
