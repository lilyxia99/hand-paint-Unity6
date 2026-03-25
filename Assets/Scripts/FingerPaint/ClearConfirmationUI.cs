using TMPro;
using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// World-space confirmation panel that appears when the clear gesture is triggered.
    /// Shows "Really cleaning?" with a countdown timer bar.
    /// Uses TextMeshPro with optional SDF glow.
    /// </summary>
    public class ClearConfirmationUI : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private float _panelWidth = 0.35f;
        [SerializeField] private float _distance = 0.5f;
        [SerializeField] private float _verticalOffset = 0.05f;

        [Header("Text")]
        [Tooltip("Optional TMP font asset. Leave empty for the default TMP font.")]
        [SerializeField] private TMP_FontAsset _font;

        [Header("Messages")]
        [SerializeField] [TextArea(1, 3)] private string _confirmMessage = "Really cleaning?";
        [SerializeField] [TextArea(1, 3)] private string _instructionMessage = "Thumbs up = YES    Wait = Cancel";

        [Header("Glow (TMP Shader)")]
        [Tooltip("Enable the TMP SDF shader glow effect.")]
        [SerializeField] private bool _enableGlow = true;

        [Tooltip("Drag TMP_SDF.shader here (Assets/TextMesh Pro/Shaders/TMP_SDF.shader).")]
        [SerializeField] private Shader _sdfGlowShader;

        [SerializeField] private Color _glowColor = new Color(1f, 0.85f, 0.7f, 0.5f);
        [SerializeField] [Range(-1f, 1f)] private float _glowOffset = 0f;
        [SerializeField] [Range(0f, 1f)] private float _glowInner = 0.15f;
        [SerializeField] [Range(0f, 1f)] private float _glowOuter = 0.35f;
        [SerializeField] [Range(0f, 1f)] private float _glowPower = 0.6f;

        // ─── Public state ───────────────────────────────────────────────

        /// <summary>Set by ClearGestureDetector to drive the timer bar.</summary>
        public float TimeoutDuration { get; set; } = 5f;

        /// <summary>Set each frame by ClearGestureDetector.</summary>
        public float ElapsedTime { get; set; }

        // ─── Private state ──────────────────────────────────────────────

        private Transform _root;
        private TextMeshPro _messageTMP;
        private TextMeshPro _instructionTMP;
        private Transform _timerBarFill;
        private Material _timerBarMat;
        private Camera _mainCam;
        private bool _isBuilt;

        // ─── Colors ─────────────────────────────────────────────────────

        private static readonly Color ColorTimer       = new Color(1f, 0.3f, 0.2f, 0.95f);
        private static readonly Color ColorMessage     = new Color(1f, 0.85f, 0.7f);
        private static readonly Color ColorInstruction = new Color(0.8f, 0.8f, 0.6f);

        // ─── Public API ─────────────────────────────────────────────────

        public void Show()
        {
            _mainCam = Camera.main;

            if (!_isBuilt)
                BuildPanel();

            ElapsedTime = 0f;
            _root.gameObject.SetActive(true);

            if (_mainCam != null)
            {
                var camT = _mainCam.transform;
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

        public void Hide()
        {
            if (_root != null)
                _root.gameObject.SetActive(false);
        }

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            _mainCam = Camera.main;
        }

        private void LateUpdate()
        {
            if (_root == null || !_root.gameObject.activeSelf || _mainCam == null)
                return;

            FollowHead();
            UpdateTimerBar();
        }

        private void OnDestroy()
        {
            if (_timerBarMat != null) Destroy(_timerBarMat);
        }

        // ─── Head tracking ──────────────────────────────────────────────

        private void FollowHead()
        {
            var camT = _mainCam.transform;
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

        // ─── Timer bar ──────────────────────────────────────────────────

        private void UpdateTimerBar()
        {
            float remaining = Mathf.Clamp01(1f - ElapsedTime / TimeoutDuration);
            float maxWidth = _panelWidth * 0.85f;
            float barWidth = Mathf.Max(0.001f, remaining * maxWidth);

            var s = _timerBarFill.localScale;
            s.x = barWidth;
            _timerBarFill.localScale = s;

            var p = _timerBarFill.localPosition;
            p.x = -maxWidth * 0.5f + barWidth * 0.5f;
            _timerBarFill.localPosition = p;

            _timerBarMat.color = Color.Lerp(
                new Color(0.4f, 0.1f, 0.1f, 0.7f),
                ColorTimer,
                remaining);
        }

        // ─── Build panel ────────────────────────────────────────────────

        private void BuildPanel()
        {
            _root = new GameObject("ClearConfirmPanel").transform;
            _root.SetParent(transform, false);

            // Message text
            var msgCfg = TMPTextFactory.Config.Default;
            msgCfg.Name = "MessageText";
            msgCfg.Parent = _root;
            msgCfg.FontSize = 42f;
            msgCfg.Color = ColorMessage;
            msgCfg.LocalScale = 0.006f;
            msgCfg.LocalPosition = new Vector3(0f, 0.025f, 0f);
            msgCfg.RectSize = new Vector2(400f, 60f);
            msgCfg.Font = _font;
            msgCfg.GlowShader = _sdfGlowShader;
            msgCfg.EnableGlow = _enableGlow;
            msgCfg.Glow = GetGlowSettings();
            msgCfg.Glow.Color = new Color(ColorMessage.r, ColorMessage.g, ColorMessage.b, 0.5f);

            var msgResult = TMPTextFactory.Create(msgCfg);
            _messageTMP = msgResult.TMP;
            _messageTMP.text = _confirmMessage;

            // Instruction text
            var instCfg = TMPTextFactory.Config.Default;
            instCfg.Name = "InstructionText";
            instCfg.Parent = _root;
            instCfg.FontSize = 30f;
            instCfg.Color = ColorInstruction;
            instCfg.LocalScale = 0.005f;
            instCfg.LocalPosition = new Vector3(0f, 0.002f, 0f);
            instCfg.RectSize = new Vector2(500f, 50f);
            instCfg.Font = _font;
            instCfg.GlowShader = _sdfGlowShader;
            instCfg.EnableGlow = _enableGlow;
            instCfg.Glow = GetGlowSettings();
            instCfg.Glow.Color = new Color(ColorInstruction.r, ColorInstruction.g, ColorInstruction.b, 0.5f);

            var instResult = TMPTextFactory.Create(instCfg);
            _instructionTMP = instResult.TMP;
            _instructionTMP.text = _instructionMessage;

            // Timer bar fill (no background — just the bar itself)
            float barY = -0.035f;
            float barH = 0.012f;
            float barW = _panelWidth * 0.85f;

            _timerBarFill = CreateQuad("TimerFill", _root, barW, barH * 0.85f);
            _timerBarMat = CreateUnlitMat(ColorTimer);
            _timerBarFill.GetComponent<MeshRenderer>().sharedMaterial = _timerBarMat;
            _timerBarFill.localPosition = new Vector3(0f, barY, -0.001f);

            _root.gameObject.SetActive(false);
            _isBuilt = true;
        }

        // ─── UI helpers ─────────────────────────────────────────────────

        private static Transform CreateQuad(string name, Transform parent, float width, float height)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(width, height, 1f);

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            return go.transform;
        }

        private static Material CreateUnlitMat(Color color)
        {
            var shader = Shader.Find("Unlit/Color")
                      ?? Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader);
            mat.color = color;
            return mat;
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
