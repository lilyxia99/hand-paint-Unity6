using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// ScriptableObject preset defining how voice channels map to visual properties.
    /// Create via Assets > Create > FingerPaint > Voice Brush Profile.
    /// </summary>
    [CreateAssetMenu(
        fileName  = "NewVoiceBrushProfile",
        menuName  = "FingerPaint/Voice Brush Profile",
        order     = 100)]
    public class VoiceBrushProfile : ScriptableObject
    {
        [Header("Base Values (used when voice is silent or as starting point)")]
        [Tooltip("Base color in HSV: mappings shift Hue/Saturation/Value from this.")]
        public Color BaseColor = new Color(1f, 0.4f, 0.2f, 1f);

        [Tooltip("Base emission color.")]
        public Color BaseEmissionColor = new Color(1f, 0.5f, 0.3f, 1f);

        [Range(0f, 3f)]
        public float BaseEmissionIntensity = 0.3f;

        [Range(0f, 1f)]
        public float BaseOpacity = 0.5f;

        [Range(0.1f, 5f)]
        public float BaseSizeMultiplier = 1.0f;

        [Tooltip("Default mesh index (0=Sphere, 1=Cube, 2=Octahedron, 3=Diamond).")]
        [Range(0, 3)]
        public int BaseMeshIndex = 0;

        [Range(0f, 2f)]
        public float BaseFresnelScale = 0.8f;

        [Header("Voice-to-Visual Mappings")]
        [Tooltip("Each entry maps a voice analysis channel to a visual target.")]
        public VoiceMapping[] Mappings = new VoiceMapping[0];

        [Header("Smoothing")]
        [Range(0.01f, 1f)]
        [Tooltip("EMA smoothing for mapped outputs. Lower = smoother but laggier.")]
        public float OutputSmoothing = 0.3f;
    }
}
