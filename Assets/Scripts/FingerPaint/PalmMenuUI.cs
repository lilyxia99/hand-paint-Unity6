using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Shows floating icon labels above each palm when the user looks at them.
    /// Left palm: "Gallery"   Right palm: "Clear"
    /// White text with a glow effect (duplicate TextMesh slightly scaled up + blurred).
    /// No black backgrounds — text floats cleanly in VR.
    /// </summary>
    public class PalmMenuUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HandTrackingManager _handTracking;

        [Header("Gaze Detection")]
        [Tooltip("Min dot(cameraForward, toPalm) to count as 'looking at palm'. 0.55 ≈ 56° cone.")]
        [SerializeField] private float _gazeDotThreshold = 0.55f;

        [Tooltip("Min dot(palmNormal, toCamera) to count as 'palm facing camera'.")]
        [SerializeField] private float _palmFacingDotThreshold = 0.2f;

        [Header("Appearance")]
        [Tooltip("Offset above the palm center in palm-normal direction (meters).")]
        [SerializeField] private float _hoverHeight = 0.08f;

        [SerializeField] private float _iconScale = 0.0004f;

        [Header("Labels")]
        [SerializeField] private string _leftLabel = "Gallery";
        [SerializeField] private string _rightLabel = "Clear";

        // ─── Runtime ────────────────────────────────────────────────────
        private Camera _cam;

        // Left palm
        private Transform _leftRoot;
        private MeshRenderer _leftTextRenderer;

        // Right palm
        private Transform _rightRoot;
        private MeshRenderer _rightTextRenderer;

        // ─── Public state ───────────────────────────────────────────────

        /// <summary>True when the user is looking at their left palm.</summary>
        public bool IsGazingLeftPalm { get; private set; }

        /// <summary>True when the user is looking at their right palm.</summary>
        public bool IsGazingRightPalm { get; private set; }

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Start()
        {
            _cam = Camera.main;
            BuildIcon(ref _leftRoot, ref _leftTextRenderer, "PalmIcon_Left", _leftLabel);
            BuildIcon(ref _rightRoot, ref _rightTextRenderer, "PalmIcon_Right", _rightLabel);
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

        private void BuildIcon(ref Transform root, ref MeshRenderer textRenderer,
            string name, string label)
        {
            var rootGO = new GameObject(name);
            rootGO.transform.SetParent(transform, false);
            root = rootGO.transform;

            // Main text layer (crisp white)
            var textGO = new GameObject(name + "_Text");
            textGO.transform.SetParent(root, false);
            var tm = textGO.AddComponent<TextMesh>();
            tm.fontSize = 48;
            tm.characterSize = _iconScale;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            tm.text = label;
            textGO.transform.localPosition = Vector3.zero;
            textRenderer = textGO.GetComponent<MeshRenderer>();

            // Multi-layer glow halo
            TextGlowHelper.AddGlow(root, tm, Color.white);

            // Start hidden
            rootGO.SetActive(false);
        }
    }
}
