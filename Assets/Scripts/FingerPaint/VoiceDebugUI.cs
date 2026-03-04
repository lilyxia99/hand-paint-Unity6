using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// World-space debug overlay for VoiceDetector — zero external dependencies.
    /// Uses only built-in MeshRenderer (Quads) + TextMesh. No UGUI / Canvas needed.
    /// Floats in front of the player's head so it's visible in VR on Quest.
    /// </summary>
    public class VoiceDebugUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private VoiceDetector _voiceDetector;

        [Header("Positioning")]
        [Tooltip("Distance in front of the camera.")]
        [SerializeField] private float _distance = 0.6f;

        [Tooltip("Vertical offset from eye level (negative = below).")]
        [SerializeField] private float _verticalOffset = -0.2f;

        [Header("Panel Size (world units)")]
        [SerializeField] private float _panelWidth = 0.28f;
        [SerializeField] private float _panelHeight = 0.10f;
        [SerializeField] private float _barHeight = 0.018f;

        // Runtime references
        private Transform _root;
        private Transform _barFill;
        private Transform _rawBarFill;
        private Transform _thresholdLine;
        private Transform _deactLine;
        private Transform _activeDot;
        private TextMesh _statusText;
        private TextMesh _valuesText;
        private Material _barFillMat;
        private Material _activeDotMat;

        private Camera _mainCam;

        // ─── Lifecycle ───────────────────────────────────────────────────

        private void Start()
        {
            _mainCam = Camera.main;
            BuildPanel();
        }

        private void LateUpdate()
        {
            if (_voiceDetector == null || _mainCam == null)
                return;

            FollowHead();
            UpdateVisuals();
        }

        private void OnDestroy()
        {
            // Clean up runtime materials
            if (_barFillMat != null) Destroy(_barFillMat);
            if (_activeDotMat != null) Destroy(_activeDotMat);
        }

        // ─── Head tracking ───────────────────────────────────────────────

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

        // ─── Update visuals ──────────────────────────────────────────────

        private void UpdateVisuals()
        {
            float smoothed = _voiceDetector.NormalizedVolume;
            float raw = _voiceDetector.RawVolume;
            bool active = _voiceDetector.IsActive;
            bool micOk = _voiceDetector.IsMicrophoneAvailable;
            bool permOk = _voiceDetector.IsPermissionGranted;

            // Magnify display: voice RMS is typically 0–0.15
            float mag = 5f;
            float smoothDisp = Mathf.Clamp01(smoothed * mag);
            float rawDisp = Mathf.Clamp01(raw * mag);
            float threshDisp = Mathf.Clamp01(_voiceDetector.ActivationThreshold * mag);
            float deactDisp = Mathf.Clamp01(_voiceDetector.DeactivationThreshold * mag);

            float barW = _panelWidth * 0.82f;

            // Smoothed bar (scale X from left edge)
            SetBarWidth(_barFill, smoothDisp, barW);

            // Raw bar (behind smoothed, dimmer)
            SetBarWidth(_rawBarFill, rawDisp, barW);

            // Threshold lines
            SetLineX(_thresholdLine, threshDisp, barW);
            SetLineX(_deactLine, deactDisp, barW);

            // Bar color
            _barFillMat.color = active
                ? new Color(0.15f, 1f, 0.35f, 0.95f)
                : new Color(0.25f, 0.55f, 1f, 0.95f);

            // Active dot
            _activeDotMat.color = active
                ? new Color(0.1f, 1f, 0.2f)
                : new Color(0.35f, 0.35f, 0.35f);

            // Status text
            string status = !permOk ? "NO PERMISSION"
                          : !micOk ? "MIC UNAVAILABLE"
                          : "MIC OK";

            string device = _voiceDetector.MicDeviceName ?? "none";
            if (device.Length > 24) device = device.Substring(0, 24) + "..";

            _statusText.text = $"{status} | Devices: {_voiceDetector.MicDeviceCount}\n{device}";
            _statusText.color = !permOk || !micOk ? new Color(1f, 0.3f, 0.3f) : Color.white;

            // Values text
            float sizeMul = _voiceDetector.GetSizeMultiplier();
            string activeLabel = active ? ">> ACTIVE <<" : "idle";
            _valuesText.text = $"Raw:{raw:F4} Smooth:{smoothed:F4} Size:{sizeMul:F1}x {activeLabel}";
            _valuesText.color = active ? new Color(0.3f, 1f, 0.5f) : new Color(0.7f, 0.7f, 0.7f);
        }

        private void SetBarWidth(Transform bar, float normalized, float maxWidth)
        {
            float w = Mathf.Max(0.0005f, normalized * maxWidth);
            var s = bar.localScale;
            s.x = w;
            bar.localScale = s;

            // Anchor to left edge
            var p = bar.localPosition;
            p.x = -maxWidth * 0.5f + w * 0.5f;
            bar.localPosition = p;
        }

        private void SetLineX(Transform line, float normalized, float maxWidth)
        {
            var p = line.localPosition;
            p.x = -maxWidth * 0.5f + normalized * maxWidth;
            line.localPosition = p;
        }

        // ─── Build the panel from primitives ─────────────────────────────

        private void BuildPanel()
        {
            _root = new GameObject("VoiceDebugPanel").transform;
            _root.SetParent(transform, false);

            // Shared unlit shader for bars
            var unlitShader = Shader.Find("Unlit/Color")
                           ?? Shader.Find("Universal Render Pipeline/Unlit");

            // --- Background quad ---
            var bg = CreateQuad("BG", _root, _panelWidth, _panelHeight);
            SetColor(bg, new Color(0f, 0f, 0f, 0.75f), true);
            bg.localPosition = Vector3.zero;

            // --- Bar region ---
            float barW = _panelWidth * 0.82f;
            float barLeft = -_panelWidth * 0.5f + (_panelWidth * 0.04f) + barW * 0.5f;
            float barY = -0.005f; // slightly below centre

            // Bar background
            var barBg = CreateQuad("BarBG", _root, barW, _barHeight);
            SetColor(barBg, new Color(0.12f, 0.12f, 0.12f), false);
            barBg.localPosition = new Vector3(barLeft, barY, -0.0005f);

            // Raw volume bar (dim)
            _rawBarFill = CreateQuad("RawBar", _root, 0.001f, _barHeight * 0.9f);
            SetColor(_rawBarFill, new Color(0.4f, 0.4f, 0.6f, 0.45f), false);
            _rawBarFill.localPosition = new Vector3(barLeft, barY, -0.001f);

            // Smoothed volume bar
            _barFill = CreateQuad("SmoothBar", _root, 0.001f, _barHeight * 0.9f);
            _barFillMat = CreateUnlitMat(new Color(0.25f, 0.55f, 1f, 0.95f));
            _barFill.GetComponent<MeshRenderer>().sharedMaterial = _barFillMat;
            _barFill.localPosition = new Vector3(barLeft, barY, -0.0015f);

            // Activation threshold line (yellow)
            _thresholdLine = CreateQuad("ThreshLine", _root, 0.001f, _barHeight * 1.15f);
            SetColor(_thresholdLine, new Color(1f, 0.9f, 0.1f), false);
            _thresholdLine.localPosition = new Vector3(barLeft, barY, -0.002f);

            // Deactivation threshold line (orange)
            _deactLine = CreateQuad("DeactLine", _root, 0.0008f, _barHeight * 1.15f);
            SetColor(_deactLine, new Color(1f, 0.5f, 0.1f, 0.7f), false);
            _deactLine.localPosition = new Vector3(barLeft, barY, -0.002f);

            // Active dot (right of bar)
            float dotX = _panelWidth * 0.5f - _panelWidth * 0.06f;
            _activeDot = CreateQuad("ActiveDot", _root, 0.016f, 0.016f);
            _activeDotMat = CreateUnlitMat(new Color(0.35f, 0.35f, 0.35f));
            _activeDot.GetComponent<MeshRenderer>().sharedMaterial = _activeDotMat;
            _activeDot.localPosition = new Vector3(dotX, barY, -0.001f);

            // --- Status text (top) ---
            var statusGO = new GameObject("StatusText");
            statusGO.transform.SetParent(_root, false);
            _statusText = statusGO.AddComponent<TextMesh>();
            _statusText.fontSize = 36;
            _statusText.characterSize = 0.006f;
            _statusText.anchor = TextAnchor.UpperLeft;
            _statusText.alignment = TextAlignment.Left;
            _statusText.color = Color.white;
            _statusText.text = "Initializing...";
            statusGO.transform.localPosition = new Vector3(
                -_panelWidth * 0.47f, _panelHeight * 0.45f, -0.002f);

            // --- Values text (bottom) ---
            var valuesGO = new GameObject("ValuesText");
            valuesGO.transform.SetParent(_root, false);
            _valuesText = valuesGO.AddComponent<TextMesh>();
            _valuesText.fontSize = 30;
            _valuesText.characterSize = 0.005f;
            _valuesText.anchor = TextAnchor.LowerLeft;
            _valuesText.alignment = TextAlignment.Left;
            _valuesText.color = new Color(0.7f, 0.7f, 0.7f);
            _valuesText.text = "";
            valuesGO.transform.localPosition = new Vector3(
                -_panelWidth * 0.47f, -_panelHeight * 0.45f, -0.002f);

            // Initial position
            if (_mainCam != null)
            {
                _root.position = _mainCam.transform.position
                    + _mainCam.transform.forward * _distance
                    + Vector3.up * _verticalOffset;
            }
        }

        private static Transform CreateQuad(string name, Transform parent, float width, float height)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(width, height, 1f);

            // Remove collider — we don't need it for UI
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            return go.transform;
        }

        private static void SetColor(Transform quad, Color color, bool transparent)
        {
            var mr = quad.GetComponent<MeshRenderer>();
            var mat = CreateUnlitMat(color);
            if (transparent)
            {
                mat.SetFloat("_Mode", 3); // won't work for Unlit/Color, but harmless
                mat.renderQueue = 3000;
            }
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
