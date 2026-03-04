using UnityEditor;
using UnityEngine;

namespace FingerPaint.Editor
{
    /// <summary>
    /// Editor utility that generates example <see cref="VoiceBrushProfile"/> presets.
    /// Menu: Assets > Create > FingerPaint > Generate Example Presets
    /// </summary>
    public static class VoiceBrushPresetFactory
    {
        private const string PresetFolder = "Assets/Presets/VoiceBrush";

        [MenuItem("Assets/Create/FingerPaint/Generate Example Presets")]
        public static void GenerateAllPresets()
        {
            EnsureFolder(PresetFolder);

            CreateWarmReactive();
            CreateWhisperGhost();
            CreateNoisySculptor();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[VoiceBrushPresetFactory] Generated 3 example presets in " + PresetFolder);
        }

        // ─── Warm Reactive ─────────────────────────────────────────────

        private static void CreateWarmReactive()
        {
            var p = ScriptableObject.CreateInstance<VoiceBrushProfile>();

            p.BaseColor             = new Color(1f, 0.35f, 0.15f, 1f); // warm orange
            p.BaseEmissionColor     = new Color(1f, 0.4f, 0.2f, 1f);
            p.BaseEmissionIntensity = 0.3f;
            p.BaseOpacity           = 0.6f;
            p.BaseSizeMultiplier    = 1.0f;
            p.BaseMeshIndex         = 0; // sphere
            p.BaseFresnelScale      = 0.6f;
            p.OutputSmoothing       = 0.25f;

            p.Mappings = new VoiceMapping[]
            {
                // Pitch  → Hue: shifts from red (0.0) through orange to yellow (0.15)
                new VoiceMapping
                {
                    Source        = VoiceChannel.Pitch,
                    Target        = VisualTarget.ColorHue,
                    ResponseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                    OutputMin     = 0.0f,
                    OutputMax     = 0.15f,
                    Influence     = 1.0f
                },
                // Loudness → Size: louder = bigger
                new VoiceMapping
                {
                    Source        = VoiceChannel.Loudness,
                    Target        = VisualTarget.Size,
                    ResponseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
                    OutputMin     = 0.6f,
                    OutputMax     = 2.5f,
                    Influence     = 1.0f
                },
                // Loudness → Emission: louder = brighter glow
                new VoiceMapping
                {
                    Source        = VoiceChannel.Loudness,
                    Target        = VisualTarget.EmissionIntensity,
                    ResponseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
                    OutputMin     = 0.1f,
                    OutputMax     = 2.0f,
                    Influence     = 0.8f
                },
                // Brightness → Fresnel: brighter voice = stronger rim glow
                new VoiceMapping
                {
                    Source        = VoiceChannel.Brightness,
                    Target        = VisualTarget.FresnelStrength,
                    ResponseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                    OutputMin     = 0.2f,
                    OutputMax     = 1.8f,
                    Influence     = 0.7f
                }
            };

            SavePreset(p, "WarmReactive");
        }

        // ─── Whisper Ghost ─────────────────────────────────────────────

        private static void CreateWhisperGhost()
        {
            var p = ScriptableObject.CreateInstance<VoiceBrushProfile>();

            p.BaseColor             = new Color(0.7f, 0.8f, 1f, 1f); // pale blue-white
            p.BaseEmissionColor     = new Color(0.5f, 0.6f, 1f, 1f);
            p.BaseEmissionIntensity = 0.15f;
            p.BaseOpacity           = 0.1f;  // very transparent at rest
            p.BaseSizeMultiplier    = 0.8f;
            p.BaseMeshIndex         = 0;     // sphere
            p.BaseFresnelScale      = 1.2f;  // strong rim for ghostly feel
            p.OutputSmoothing       = 0.15f; // more responsive

            p.Mappings = new VoiceMapping[]
            {
                // Loudness → Opacity: whisper = faint, louder = more opaque
                new VoiceMapping
                {
                    Source        = VoiceChannel.Loudness,
                    Target        = VisualTarget.Opacity,
                    ResponseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
                    OutputMin     = 0.05f,
                    OutputMax     = 0.7f,
                    Influence     = 1.0f
                },
                // Noisiness → Saturation: breathy noise = desaturated
                new VoiceMapping
                {
                    Source        = VoiceChannel.Noisiness,
                    Target        = VisualTarget.ColorSaturation,
                    ResponseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                    OutputMin     = 0.1f,
                    OutputMax     = 0.6f,
                    Influence     = 0.8f
                },
                // Pitch → Size: higher pitch = smaller, daintier orbs
                new VoiceMapping
                {
                    Source        = VoiceChannel.Pitch,
                    Target        = VisualTarget.Size,
                    ResponseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                    OutputMin     = 1.2f,
                    OutputMax     = 0.4f, // inverted: high pitch = small
                    Influence     = 0.9f
                },
                // Pitch → Hue: shifts through cool blue-purple range
                new VoiceMapping
                {
                    Source        = VoiceChannel.Pitch,
                    Target        = VisualTarget.ColorHue,
                    ResponseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                    OutputMin     = 0.55f,  // blue
                    OutputMax     = 0.75f,  // purple
                    Influence     = 0.6f
                }
            };

            SavePreset(p, "WhisperGhost");
        }

        // ─── Noisy Sculptor ────────────────────────────────────────────

        private static void CreateNoisySculptor()
        {
            var p = ScriptableObject.CreateInstance<VoiceBrushProfile>();

            p.BaseColor             = new Color(0.3f, 0.9f, 0.5f, 1f); // green
            p.BaseEmissionColor     = new Color(0.2f, 1f, 0.4f, 1f);
            p.BaseEmissionIntensity = 0.4f;
            p.BaseOpacity           = 0.7f;
            p.BaseSizeMultiplier    = 1.0f;
            p.BaseMeshIndex         = 0;     // sphere default
            p.BaseFresnelScale      = 0.5f;
            p.OutputSmoothing       = 0.2f;

            // Create a staircase curve for mesh index transitions
            var meshCurve = new AnimationCurve();
            meshCurve.AddKey(new Keyframe(0.00f, 0.0f) { outTangent = 0f });
            meshCurve.AddKey(new Keyframe(0.24f, 0.0f) { inTangent = 0f, outTangent = 0f });
            meshCurve.AddKey(new Keyframe(0.26f, 0.33f) { inTangent = 0f, outTangent = 0f });
            meshCurve.AddKey(new Keyframe(0.49f, 0.33f) { inTangent = 0f, outTangent = 0f });
            meshCurve.AddKey(new Keyframe(0.51f, 0.66f) { inTangent = 0f, outTangent = 0f });
            meshCurve.AddKey(new Keyframe(0.74f, 0.66f) { inTangent = 0f, outTangent = 0f });
            meshCurve.AddKey(new Keyframe(0.76f, 1.0f)  { inTangent = 0f, outTangent = 0f });
            meshCurve.AddKey(new Keyframe(1.00f, 1.0f)  { inTangent = 0f });

            p.Mappings = new VoiceMapping[]
            {
                // Noisiness → MeshShape: clean voice = sphere, noisy = diamond
                new VoiceMapping
                {
                    Source        = VoiceChannel.Noisiness,
                    Target        = VisualTarget.MeshShape,
                    ResponseCurve = meshCurve,
                    OutputMin     = 0f,    // sphere
                    OutputMax     = 3f,    // diamond
                    Influence     = 1.0f
                },
                // Pitch → Hue: full rainbow sweep
                new VoiceMapping
                {
                    Source        = VoiceChannel.Pitch,
                    Target        = VisualTarget.ColorHue,
                    ResponseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                    OutputMin     = 0.0f,
                    OutputMax     = 0.85f,  // almost full hue range
                    Influence     = 1.0f
                },
                // Loudness → Size: louder = bigger
                new VoiceMapping
                {
                    Source        = VoiceChannel.Loudness,
                    Target        = VisualTarget.Size,
                    ResponseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
                    OutputMin     = 0.5f,
                    OutputMax     = 2.0f,
                    Influence     = 1.0f
                },
                // Brightness → Value: brighter voice = brighter color
                new VoiceMapping
                {
                    Source        = VoiceChannel.Brightness,
                    Target        = VisualTarget.ColorValue,
                    ResponseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
                    OutputMin     = 0.4f,
                    OutputMax     = 1.0f,
                    Influence     = 0.7f
                }
            };

            SavePreset(p, "NoisySculptor");
        }

        // ─── Helpers ───────────────────────────────────────────────────

        private static void SavePreset(VoiceBrushProfile profile, string name)
        {
            string path = $"{PresetFolder}/{name}.asset";

            // Check if asset already exists — overwrite fields, keep GUID
            var existing = AssetDatabase.LoadAssetAtPath<VoiceBrushProfile>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(profile, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(profile);
                Debug.Log($"  Updated: {path}");
            }
            else
            {
                AssetDatabase.CreateAsset(profile, path);
                Debug.Log($"  Created: {path}");
            }
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0]; // "Assets"

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
