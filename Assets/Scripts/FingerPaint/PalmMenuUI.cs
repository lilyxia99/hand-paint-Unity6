using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Shows floating icon labels above each palm when the user looks at them.
    /// Left palm: "Gallery"   Right palm: "Clear"
    /// Uses TextMesh (white, monochromatic) — Quad background + TextMesh foreground.
    ///
    /// Inspired by Meta SDK PalmMenu: positions icons relative to the palm joint
    /// with world-space LookRotation toward the camera.
    ///
    /// Gaze detection: checks that the palm faces the camera AND the camera
    /// looks toward the palm (i.e., user actively looks at their hand).
    /// </summary>
    public class PalmMenuUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HandTrackingManager _handTracking;

        [Header("Gaze Detection")]
        [Tooltip("Min dot(cameraForward, toPalm) to count as 'looking at palm'. 0.7 ≈ 45° cone.")]
        [SerializeField] private float _gazeDotThreshold = 0.7f;

        [Tooltip("Min dot(palmNormal, toCamera) to count as 'palm facing camera'.")]
        [SerializeField] private float _palmFacingDotThreshold = 0.3f;

        [Header("Appearance")]
        [Tooltip("Offset above the palm center in palm-normal direction (meters).")]
        [SerializeField] private float _hoverHeight = 0.08f;

        [SerializeField] private float _iconScale = 0.0004f;
        [SerializeField] private float _bgPadH = 0.015f;
        [SerializeField] private float _bgPadV = 0.006f;

        [Header("Labels")]
        [SerializeField] private string _leftLabel = "Gallery";
        [SerializeField] private string _rightLabel = "Clear";

        // ─── Runtime ────────────────────────────────────────────────────
        private Camera _cam;

        // Left palm icon
        private Transform _leftRoot;
        private TextMesh _leftText;
        private MeshRenderer _leftTextRenderer;
        private Transform _leftBg;
        private MeshRenderer _leftBgRenderer;
        private Material _leftBgMat;

        // Right palm icon
        private Transform _rightRoot;
        private TextMesh _rightText;
        private MeshRenderer _rightTextRenderer;
        private Transform _rightBg;
        private MeshRenderer _rightBgRenderer;
        private Material _rightBgMat;

        // ─── Public state ───────────────────────────────────────────────

        /// <summary>True when the user is looking at their left palm.</summary>
        public bool IsGazingLeftPalm { get; private set; }

        /// <summary>True when the user is looking at their right palm.</summary>
        public bool IsGazingRightPalm { get; private set; }

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Start()
        {
            _cam = Camera.main;
            BuildIcon(ref _leftRoot, ref _leftText, ref _leftTextRenderer,
                      ref _leftBg, ref _leftBgRenderer, ref _leftBgMat,
                      "PalmIcon_Left", _leftLabel);
            BuildIcon(ref _rightRoot, ref _rightText, ref _rightTextRenderer,
                      ref _rightBg, ref _rightBgRenderer, ref _rightBgMat,
                      "PalmIcon_Right", _rightLabel);
        }

        private void LateUpdate()
        {
            if (_handTracking == null) return;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            IsGazingLeftPalm = UpdatePalmIcon(
                isLeft: true,
                _leftRoot, _leftTextRenderer, _leftBgRenderer, _leftBgMat);

            IsGazingRightPalm = UpdatePalmIcon(
                isLeft: false,
                _rightRoot, _rightTextRenderer, _rightBgRenderer, _rightBgMat);
        }

        private void OnDestroy()
        {
            if (_leftBgMat != null) Destroy(_leftBgMat);
            if (_rightBgMat != null) Destroy(_rightBgMat);
        }

        // ─── Per-palm update ────────────────────────────────────────────

        private bool UpdatePalmIcon(bool isLeft, Transform root,
            MeshRenderer textRenderer, MeshRenderer bgRenderer, Material bgMat)
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

                // Always position the icon even if not gazing, so it's ready
                Vector3 iconPos = palmPos + palmNormal * _hoverHeight;
                root.position = iconPos;

                // Face the camera (billboard)
                Vector3 lookDir = iconPos - camPos;
                if (lookDir.sqrMagnitude > 0.001f)
                    root.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }

            // Show/hide
            bool shouldShow = gazing;
            root.gameObject.SetActive(shouldShow);

            return gazing;
        }

        // ─── Build icons ────────────────────────────────────────────────

        private void BuildIcon(ref Transform root, ref TextMesh text,
            ref MeshRenderer textRenderer, ref Transform bg,
            ref MeshRenderer bgRenderer, ref Material bgMat,
            string name, string label)
        {
            // Root container
            var rootGO = new GameObject(name);
            rootGO.transform.SetParent(transform, false);
            root = rootGO.transform;

            // Background quad
            var bgGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgGO.name = name + "_BG";
            bgGO.transform.SetParent(root, false);

            var col = bgGO.GetComponent<Collider>();
            if (col != null) Destroy(col);

            bgMat = CreateUnlitTransparentMat(new Color(0f, 0f, 0f, 0.65f));
            bgRenderer = bgGO.GetComponent<MeshRenderer>();
            bgRenderer.sharedMaterial = bgMat;
            bg = bgGO.transform;

            // TextMesh label
            var textGO = new GameObject(name + "_Text");
            textGO.transform.SetParent(root, false);

            text = textGO.AddComponent<TextMesh>();
            text.fontSize = 48;
            text.characterSize = _iconScale;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            text.text = label;

            textRenderer = textGO.GetComponent<MeshRenderer>();
            textGO.transform.localPosition = new Vector3(0f, 0f, -0.001f);

            // Size background to fit text (approximate)
            // TextMesh bounds aren't available until rendered, so estimate
            float charW = _iconScale * 0.55f; // approximate char width
            float textW = label.Length * charW;
            float textH = _iconScale * 1.2f; // approximate height
            bg.localScale = new Vector3(textW + _bgPadH * 2f, textH + _bgPadV * 2f, 1f);

            // Start hidden
            rootGO.SetActive(false);
        }

        // ─── Material helper ────────────────────────────────────────────

        private static Material CreateUnlitTransparentMat(Color color)
        {
            // Use Unlit/Transparent or fall back
            var shader = Shader.Find("Unlit/Color")
                      ?? Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader);
            mat.color = color;
            mat.renderQueue = 3100; // Render on top
            return mat;
        }
    }
}
