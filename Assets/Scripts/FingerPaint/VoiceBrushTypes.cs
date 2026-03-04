using UnityEngine;

namespace FingerPaint
{
    // ----------------------------------------------------------------
    //  Enums
    // ----------------------------------------------------------------

    /// <summary>
    /// Voice analysis channels produced by VoiceAnalyzer.
    /// </summary>
    public enum VoiceChannel
    {
        /// <summary>RMS loudness [0,1].</summary>
        Loudness,
        /// <summary>Fundamental frequency mapped to [0,1] within a configured Hz range.</summary>
        Pitch,
        /// <summary>Spectral centroid [0,1]. Higher = more treble / brighter timbre.</summary>
        Brightness,
        /// <summary>Spectral flatness [0,1]. 0 = pure tone, 1 = noise-like.</summary>
        Noisiness
    }

    /// <summary>
    /// Visual properties that can be driven by voice.
    /// </summary>
    public enum VisualTarget
    {
        ColorHue,
        ColorSaturation,
        ColorValue,
        EmissionIntensity,
        Opacity,
        Size,
        MeshShape,
        FresnelStrength
    }

    // ----------------------------------------------------------------
    //  Serializable mapping entry
    // ----------------------------------------------------------------

    /// <summary>
    /// One mapping: voice channel -> visual property, with a response curve.
    /// </summary>
    [System.Serializable]
    public struct VoiceMapping
    {
        [Tooltip("Which voice analysis channel to read.")]
        public VoiceChannel Source;

        [Tooltip("Which visual property to drive.")]
        public VisualTarget Target;

        [Tooltip("Remapping curve. X = source value [0,1], Y = normalized output [0,1].")]
        public AnimationCurve ResponseCurve;

        [Tooltip("Output value when curve evaluates to 0.")]
        public float OutputMin;

        [Tooltip("Output value when curve evaluates to 1.")]
        public float OutputMax;

        [Range(0f, 1f)]
        [Tooltip("Blend weight. 0 = no effect, 1 = full effect.")]
        public float Influence;

        /// <summary>
        /// Convenience factory for common linear mappings.
        /// </summary>
        public static VoiceMapping Linear(VoiceChannel src, VisualTarget tgt,
                                          float min, float max, float influence = 1f)
        {
            return new VoiceMapping
            {
                Source        = src,
                Target        = tgt,
                ResponseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                OutputMin     = min,
                OutputMax     = max,
                Influence     = influence
            };
        }
    }

    // ----------------------------------------------------------------
    //  Runtime data structs (not serialized, passed between components)
    // ----------------------------------------------------------------

    /// <summary>
    /// Raw voice analysis outputs, all normalized to [0, 1].
    /// Produced by <see cref="VoiceAnalyzer"/> each frame.
    /// </summary>
    public struct VoiceFeatures
    {
        /// <summary>RMS loudness, smoothed [0,1].</summary>
        public float Loudness;
        /// <summary>Fundamental frequency normalized to [0,1] within configured range.</summary>
        public float Pitch;
        /// <summary>Spectral centroid [0,1]. Higher = brighter timbre.</summary>
        public float Brightness;
        /// <summary>Spectral flatness [0,1]. 0 = tonal, 1 = noisy.</summary>
        public float Noisiness;
        /// <summary>True when voice is detected above threshold.</summary>
        public bool IsActive;
    }

    /// <summary>
    /// Fully resolved visual properties for one paint spawn.
    /// Produced by <see cref="VoiceBrushController"/> each frame.
    /// </summary>
    public struct BrushState
    {
        public Color BaseColor;
        public Color EmissionColor;
        public float EmissionIntensity;
        public float Opacity;
        public float SizeMultiplier;
        public int   MeshIndex;
        public float FresnelScale;
        /// <summary>False when voice brush is inactive or unconfigured.</summary>
        public bool  IsValid;
    }
}
