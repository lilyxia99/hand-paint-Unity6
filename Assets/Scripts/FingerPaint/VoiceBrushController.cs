using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Runtime evaluator: reads <see cref="VoiceFeatures"/> from
    /// <see cref="VoiceAnalyzer"/>, applies the active
    /// <see cref="VoiceBrushProfile"/> mappings, and produces a
    /// <see cref="BrushState"/> each frame for <see cref="FingerPainter"/>.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class VoiceBrushController : MonoBehaviour
    {
        // ─── Configuration ──────────────────────────────────────────────

        [Header("References")]
        [SerializeField] private VoiceAnalyzer _analyzer;

        [Header("Active Profile")]
        [Tooltip("The current voice brush preset. Swap at runtime to change behavior.")]
        [SerializeField] private VoiceBrushProfile _activeProfile;

        /// <summary>Swap profiles at runtime via script or Inspector.</summary>
        public VoiceBrushProfile ActiveProfile
        {
            get => _activeProfile;
            set { _activeProfile = value; ResetSmoothing(); }
        }

        // ─── Public state ───────────────────────────────────────────────

        /// <summary>Fully resolved visual properties for the current frame.</summary>
        public BrushState CurrentBrush { get; private set; }

        // ─── Smoothed accumulators ──────────────────────────────────────

        private float _sHue;
        private float _sSat;
        private float _sVal;
        private float _sEmission;
        private float _sOpacity;
        private float _sSize;
        private float _sMeshIdx;
        private float _sFresnel;

        private bool _wasActive;   // track inactive→active transition
        private bool _initialized; // ensure Awake inits accumulators

        // ─── Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            ResetSmoothing();
            _initialized = true;
        }

        private void OnEnable()
        {
            if (_initialized) ResetSmoothing();
        }

        private void Update()
        {
            if (_analyzer == null || _activeProfile == null)
            {
                CurrentBrush = default;
                _wasActive = false;
                return;
            }

            VoiceFeatures features = _analyzer.Features;

            // Start from base values
            Color.RGBToHSV(_activeProfile.BaseColor, out float baseH, out float baseS, out float baseV);

            float hue      = baseH;
            float sat      = baseS;
            float val      = baseV;
            float emission  = _activeProfile.BaseEmissionIntensity;
            float opacity   = _activeProfile.BaseOpacity;
            float size      = _activeProfile.BaseSizeMultiplier;
            float meshIdx   = _activeProfile.BaseMeshIndex;
            float fresnel   = _activeProfile.BaseFresnelScale;

            if (features.IsActive && _activeProfile.Mappings != null)
            {
                // Accumulate mappings per-target
                // Track total influence per target for normalization
                float hueInf = 0f, satInf = 0f, valInf = 0f;
                float emiInf = 0f, opaInf = 0f, sizInf = 0f;
                float mshInf = 0f, freInf = 0f;

                float hueDelta = 0f, satDelta = 0f, valDelta = 0f;
                float emiDelta = 0f, opaDelta = 0f, sizDelta = 0f;
                float mshDelta = 0f, freDelta = 0f;

                for (int i = 0; i < _activeProfile.Mappings.Length; i++)
                {
                    ref VoiceMapping m = ref _activeProfile.Mappings[i];
                    if (m.Influence <= 0f) continue;

                    float mapped = EvaluateMapping(in m, in features);

                    switch (m.Target)
                    {
                        case VisualTarget.ColorHue:
                            hueDelta += (mapped - baseH) * m.Influence;
                            hueInf   += m.Influence;
                            break;
                        case VisualTarget.ColorSaturation:
                            satDelta += (mapped - baseS) * m.Influence;
                            satInf   += m.Influence;
                            break;
                        case VisualTarget.ColorValue:
                            valDelta += (mapped - baseV) * m.Influence;
                            valInf   += m.Influence;
                            break;
                        case VisualTarget.EmissionIntensity:
                            emiDelta += (mapped - _activeProfile.BaseEmissionIntensity) * m.Influence;
                            emiInf   += m.Influence;
                            break;
                        case VisualTarget.Opacity:
                            opaDelta += (mapped - _activeProfile.BaseOpacity) * m.Influence;
                            opaInf   += m.Influence;
                            break;
                        case VisualTarget.Size:
                            sizDelta += (mapped - _activeProfile.BaseSizeMultiplier) * m.Influence;
                            sizInf   += m.Influence;
                            break;
                        case VisualTarget.MeshShape:
                            mshDelta += (mapped - _activeProfile.BaseMeshIndex) * m.Influence;
                            mshInf   += m.Influence;
                            break;
                        case VisualTarget.FresnelStrength:
                            freDelta += (mapped - _activeProfile.BaseFresnelScale) * m.Influence;
                            freInf   += m.Influence;
                            break;
                    }
                }

                // Apply deltas (normalize if total influence > 1)
                if (hueInf > 0f) hue += hueDelta / Mathf.Max(1f, hueInf);
                if (satInf > 0f) sat += satDelta / Mathf.Max(1f, satInf);
                if (valInf > 0f) val += valDelta / Mathf.Max(1f, valInf);
                if (emiInf > 0f) emission += emiDelta / Mathf.Max(1f, emiInf);
                if (opaInf > 0f) opacity  += opaDelta / Mathf.Max(1f, opaInf);
                if (sizInf > 0f) size     += sizDelta / Mathf.Max(1f, sizInf);
                if (mshInf > 0f) meshIdx  += mshDelta / Mathf.Max(1f, mshInf);
                if (freInf > 0f) fresnel  += freDelta / Mathf.Max(1f, freInf);
            }

            // ── Voice onset: snap accumulators to avoid smoothing lag ──
            if (features.IsActive && !_wasActive)
            {
                // First active frame: skip lerp, jump straight to target
                _sHue      = hue;
                _sSat      = Mathf.Clamp01(sat);
                _sVal      = Mathf.Clamp01(val);
                _sEmission = Mathf.Max(0f, emission);
                _sOpacity  = Mathf.Clamp01(opacity);
                _sSize     = Mathf.Max(0.05f, size);
                _sMeshIdx  = meshIdx;
                _sFresnel  = Mathf.Max(0f, fresnel);
            }
            else
            {
                // Smooth all outputs
                float sm = _activeProfile.OutputSmoothing;
                _sHue      = Mathf.Lerp(_sHue,      hue,      sm);
                _sSat      = Mathf.Lerp(_sSat,      Mathf.Clamp01(sat), sm);
                _sVal      = Mathf.Lerp(_sVal,      Mathf.Clamp01(val), sm);
                _sEmission = Mathf.Lerp(_sEmission, Mathf.Max(0f, emission), sm);
                _sOpacity  = Mathf.Lerp(_sOpacity,  Mathf.Clamp01(opacity),  sm);
                _sSize     = Mathf.Lerp(_sSize,     Mathf.Max(0.05f, size),  sm);
                _sMeshIdx  = Mathf.Lerp(_sMeshIdx,  meshIdx,  sm);
                _sFresnel  = Mathf.Lerp(_sFresnel,  Mathf.Max(0f, fresnel), sm);
            }
            _wasActive = features.IsActive;

            // Hue wraps around [0, 1]
            float finalHue = Mathf.Repeat(_sHue, 1f);

            // Build color from HSV
            Color baseColor = Color.HSVToRGB(finalHue, _sSat, _sVal);
            baseColor.a = _sOpacity;

            // Emission color follows hue but at full brightness
            Color emColor = Color.HSVToRGB(finalHue, _sSat, 1f);

            CurrentBrush = new BrushState
            {
                BaseColor         = baseColor,
                EmissionColor     = emColor,
                EmissionIntensity = _sEmission,
                Opacity           = _sOpacity,
                SizeMultiplier    = _sSize,
                MeshIndex         = Mathf.RoundToInt(_sMeshIdx),
                FresnelScale      = _sFresnel,
                IsValid           = features.IsActive
            };
        }

        // ─── Mapping evaluation ─────────────────────────────────────────

        private static float EvaluateMapping(in VoiceMapping mapping, in VoiceFeatures features)
        {
            float input = mapping.Source switch
            {
                VoiceChannel.Loudness   => features.Loudness,
                VoiceChannel.Pitch      => features.Pitch,
                VoiceChannel.Brightness => features.Brightness,
                VoiceChannel.Noisiness  => features.Noisiness,
                _ => 0f
            };

            float curved = mapping.ResponseCurve != null
                ? mapping.ResponseCurve.Evaluate(input)
                : input;

            return Mathf.Lerp(mapping.OutputMin, mapping.OutputMax, curved);
        }

        // ─── Helpers ────────────────────────────────────────────────────

        private void ResetSmoothing()
        {
            if (_activeProfile == null) return;

            Color.RGBToHSV(_activeProfile.BaseColor, out _sHue, out _sSat, out _sVal);
            _sEmission = _activeProfile.BaseEmissionIntensity;
            _sOpacity  = _activeProfile.BaseOpacity;
            _sSize     = _activeProfile.BaseSizeMultiplier;
            _sMeshIdx  = _activeProfile.BaseMeshIndex;
            _sFresnel  = _activeProfile.BaseFresnelScale;
        }
    }
}
