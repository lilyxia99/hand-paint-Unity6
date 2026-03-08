using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// World-space confirmation panel that appears when the clear gesture is triggered.
    /// Shows "Really cleaning?" with a countdown timer bar.
    /// Uses Quad + TextMesh (no Canvas/UGUI), follows head like VoiceDebugUI.
    /// </summary>
    public class ClearConfirmationUI : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private float _panelWidth = 0.35f;
        [SerializeField] private float _panelHeight = 0.12f;
        [SerializeField] private float _distance = 0.5f;
        [SerializeField] private float _verticalOffset = 0.05f;

        // ─── Public state ───────────────────────────────────────────────

        /// <summary>Set by ClearGestureDetector to drive the timer bar.</summary>
        public float TimeoutDuration { get; set; } = 5f;

        /// <summary>Set each frame by ClearGestureDetector.</summary>
        public float ElapsedTime { get; set; }

        // ─── Private state ──────────────────────────────────────────────

        private Transform _root;
        private TextMesh _messageText;
        private TextMesh _instructionText;
        private Transform _timerBarFill;
        private Material _timerBarMat;
        private Camera _mainCam;
        private bool _isBuilt;

        // ─── Colors ─────────────────────────────────────────────────────

        private static readonly Color ColorBg      = new Color(0.2f, 0.02f, 0.02f, 0.88f);
        private static readonly Color ColorTimer   = new Color(1f, 0.3f, 0.2f, 0.95f);
        private static readonly Color ColorMessage = new Color(1f, 0.85f, 0.7f);
        private static readonly Color ColorInstruction = new Color(0.8f, 0.8f, 0.6f);

        // ─── Public API ─────────────────────────────────────────────────

        public void Show()
        {
            _mainCam = Camera.main;

            if (!_isBuilt)
                BuildPanel();

            ElapsedTime = 0f;
            _root.gameObject.SetActive(true);

            // Snap to position immediately
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

            // Anchor to left
            var p = _timerBarFill.localPosition;
            p.x = -maxWidth * 0.5f + barWidth * 0.5f;
            _timerBarFill.localPosition = p;

            // Color shifts to darker as time runs out
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

            // Background
            var bg = CreateQuad("BG", _root, _panelWidth, _panelHeight);
            SetColor(bg, ColorBg);
            bg.localPosition = Vector3.zero;

            // Message text
            var msgGO = new GameObject("MessageText");
            msgGO.transform.SetParent(_root, false);
            _messageText = msgGO.AddComponent<TextMesh>();
            _messageText.fontSize = 42;
            _messageText.characterSize = 0.006f;
            _messageText.anchor = TextAnchor.MiddleCenter;
            _messageText.alignment = TextAlignment.Center;
            _messageText.color = ColorMessage;
            _messageText.text = "Really cleaning?";
            msgGO.transform.localPosition = new Vector3(0f, 0.025f, -0.002f);

            // Instruction text
            var instGO = new GameObject("InstructionText");
            instGO.transform.SetParent(_root, false);
            _instructionText = instGO.AddComponent<TextMesh>();
            _instructionText.fontSize = 30;
            _instructionText.characterSize = 0.005f;
            _instructionText.anchor = TextAnchor.MiddleCenter;
            _instructionText.alignment = TextAlignment.Center;
            _instructionText.color = ColorInstruction;
            _instructionText.text = "Thumbs up = YES    Wait = Cancel";
            instGO.transform.localPosition = new Vector3(0f, 0.002f, -0.002f);

            // Timer bar background
            float barY = -0.035f;
            float barH = 0.012f;
            float barW = _panelWidth * 0.85f;

            var barBg = CreateQuad("TimerBG", _root, barW, barH);
            SetColor(barBg, new Color(0.08f, 0.08f, 0.08f));
            barBg.localPosition = new Vector3(0f, barY, -0.0005f);

            // Timer bar fill
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

        private static void SetColor(Transform quad, Color color)
        {
            var mr = quad.GetComponent<MeshRenderer>();
            var mat = CreateUnlitMat(color);
            mat.renderQueue = 3000;
            mr.sharedMaterial = mat;
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
