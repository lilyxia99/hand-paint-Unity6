using TMPro;
using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Shows floating icon labels above each palm when the user looks at them.
    /// Left palm: "Gallery"   Right palm: "Clear"
    /// Uses TextMeshPro with optional SDF glow.
    /// </summary>
    public class PalmMenuUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HandTrackingManager _handTracking;

        [Header("Gaze Detection")]
        [Tooltip("Min dot(cameraForward, toPalm) to count as 'looking at palm'. 0.45 ≈ 63° cone.")]
        [SerializeField] private float _gazeDotThreshold = 0.45f;

        [Tooltip("Min dot(palmNormal, toCamera) to count as 'palm facing camera'. Set very low to accept any palm angle.")]
        [SerializeField] private float _palmFacingDotThreshold = -0.1f;

        [Header("Appearance")]
        [Tooltip("Offset above the palm center in palm-normal direction (meters).")]
        [SerializeField] private float _hoverHeight = 0.08f;

        [SerializeField] private float _textScale = 0.004f;

        [Header("Labels")]
        [SerializeField] private string _leftLabel = "Gallery";
        [SerializeField] private string _rightLabel = "Clear";

        [Header("Text")]
        [Tooltip("Optional TMP font asset. Leave empty for the default TMP font.")]
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
        private Camera _cam;

        // Left palm
        private Transform _leftRoot;

        // Right palm
        private Transform _rightRoot;

        // ─── Public state ───────────────────────────────────────────────

        /// <summary>True when the user is looking at their left palm.</summary>
        public bool IsGazingLeftPalm { get; private set; }

        /// <summary>True when the user is looking at their right palm.</summary>
        public bool IsGazingRightPalm { get; private set; }

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Start()
        {
            _cam = Camera.main;
            BuildIcon(ref _leftRoot, "PalmIcon_Left", _leftLabel);
            BuildIcon(ref _rightRoot, "PalmIcon_Right", _rightLabel);
        }

        private void LateUpdate()
        {
            if (_handTracking == null) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            IsGazingLeftPalm = UpdatePalmIcon(true, _leftRoot);
            IsGazingRightPalm = UpdatePalmIcon(false, _rightRoot);
        }

        // ─── Per-palm update ────────────────────────────────────────────

        private bool UpdatePalmIcon(bool isLeft, Transform root)
        {
            bool gazing = false;

            if (_handTracking.TryGetPalmPose(isLeft, out Vector3 palmPos, out Vector3 palmNormal))
            {
                Vector3 camPos = _cam.transform.position;
                Vector3 camFwd = _cam.transform.forward;

                // Palm facing toward the camera?
                Vector3 dirToCamera = (camPos - palmPos).normalized;
                float palmDot = Vector3.Dot(palmNormal, dirToCamera);

                // Camera looking toward the palm?
                Vector3 dirToPalm = (palmPos - camPos).normalized;
                float gazeDot = Vector3.Dot(camFwd, dirToPalm);

                gazing = palmDot > _palmFacingDotThreshold && gazeDot > _gazeDotThreshold;

                // Always position so it's ready
                Vector3 iconPos = palmPos + palmNormal * _hoverHeight;
                root.position = iconPos;

                Vector3 lookDir = iconPos - camPos;
                if (lookDir.sqrMagnitude > 0.001f)
                    root.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }

            root.gameObject.SetActive(gazing);
            return gazing;
        }

        // ─── Build icons ────────────────────────────────────────────────

        private void BuildIcon(ref Transform root, string name, string label)
        {
            var rootGO = new GameObject(name);
            rootGO.transform.SetParent(transform, false);
            root = rootGO.transform;

            var cfg = TMPTextFactory.Config.Default;
            cfg.Name = name + "_Text";
            cfg.Parent = root;
            cfg.FontSize = 48f;
            cfg.Color = Color.white;
            cfg.LocalScale = _textScale;
            cfg.RectSize = new Vector2(200f, 50f);
            cfg.Font = _font;
            cfg.GlowShader = _sdfGlowShader;
            cfg.EnableGlow = _enableGlow;
            cfg.Glow = GetGlowSettings();

            var result = TMPTextFactory.Create(cfg);
            result.TMP.text = label;

            // Start hidden
            rootGO.SetActive(false);
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
