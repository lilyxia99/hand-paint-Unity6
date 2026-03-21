using TMPro;
using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Shared factory for creating world-space TextMeshPro text with optional SDF glow.
    /// Replaces the legacy TextMesh + TextGlowHelper approach across all UI components.
    /// </summary>
    public static class TMPTextFactory
    {
        // ─── Shader property IDs ────────────────────────────────────────
        private static readonly int ID_GlowColor  = Shader.PropertyToID("_GlowColor");
        private static readonly int ID_GlowOffset = Shader.PropertyToID("_GlowOffset");
        private static readonly int ID_GlowInner  = Shader.PropertyToID("_GlowInner");
        private static readonly int ID_GlowOuter  = Shader.PropertyToID("_GlowOuter");
        private static readonly int ID_GlowPower  = Shader.PropertyToID("_GlowPower");

        /// <summary>
        /// Configuration for creating a TMP text instance.
        /// </summary>
        public struct Config
        {
            public string Name;
            public Transform Parent;
            public float FontSize;
            public Color Color;
            public TextAlignmentOptions Alignment;
            public Vector2 RectSize;
            public float LocalScale;
            public Vector3 LocalPosition;
            public TMP_FontAsset Font;        // null = TMP default
            public Shader GlowShader;         // null = no glow possible
            public bool EnableGlow;
            public GlowSettings Glow;

            /// <summary>Sensible defaults for world-space VR text.</summary>
            public static Config Default => new Config
            {
                Name = "TMPText",
                FontSize = 36f,
                Color = Color.white,
                Alignment = TextAlignmentOptions.Center,
                RectSize = new Vector2(400f, 100f),
                LocalScale = 0.01f,
                LocalPosition = Vector3.zero,
                EnableGlow = false,
                Glow = GlowSettings.Default,
            };
        }

        /// <summary>
        /// Glow parameters matching TMP SDF shader properties.
        /// </summary>
        public struct GlowSettings
        {
            public Color Color;
            public float Offset;
            public float Inner;
            public float Outer;
            public float Power;

            public static GlowSettings Default => new GlowSettings
            {
                Color = new Color(0.5f, 0.8f, 1f, 0.5f),
                Offset = 0f,
                Inner = 0.15f,
                Outer = 0.35f,
                Power = 0.6f,
            };
        }

        /// <summary>
        /// Result returned after creating a TMP text object.
        /// </summary>
        public struct Result
        {
            public TextMeshPro TMP;
            public Material MaterialInstance;
        }

        /// <summary>
        /// Creates a world-space TextMeshPro object with optional glow.
        /// </summary>
        public static Result Create(Config cfg)
        {
            var go = new GameObject(cfg.Name);
            if (cfg.Parent != null)
                go.transform.SetParent(cfg.Parent, false);

            var tmp = go.AddComponent<TextMeshPro>();

            if (cfg.Font != null)
                tmp.font = cfg.Font;

            tmp.fontSize = cfg.FontSize;
            tmp.color = cfg.Color;
            tmp.alignment = cfg.Alignment;
            tmp.enableWordWrapping = true;

            // Scale for world space (1 TMP unit = 1 metre by default)
            go.transform.localScale = Vector3.one * cfg.LocalScale;
            go.transform.localPosition = cfg.LocalPosition;

            var rect = tmp.rectTransform;
            rect.sizeDelta = cfg.RectSize;

            tmp.text = "";

            // Create material instance (isolates glow changes from other TMP objects)
            var mat = new Material(tmp.fontSharedMaterial);
            tmp.fontMaterial = mat;

            var result = new Result { TMP = tmp, MaterialInstance = mat };

            ApplyGlow(result, cfg.GlowShader, cfg.EnableGlow, cfg.Glow);

            return result;
        }

        /// <summary>
        /// Apply or remove glow on an existing TMP material instance.
        /// </summary>
        public static void ApplyGlow(Result result, Shader glowShader, bool enable, GlowSettings glow)
        {
            if (result.MaterialInstance == null) return;

            if (enable && glowShader != null)
            {
                result.MaterialInstance.shader = glowShader;
                result.MaterialInstance.EnableKeyword("GLOW_ON");
                result.MaterialInstance.SetColor(ID_GlowColor,  glow.Color);
                result.MaterialInstance.SetFloat(ID_GlowOffset, glow.Offset);
                result.MaterialInstance.SetFloat(ID_GlowInner,  glow.Inner);
                result.MaterialInstance.SetFloat(ID_GlowOuter,  glow.Outer);
                result.MaterialInstance.SetFloat(ID_GlowPower,  glow.Power);
            }
            else
            {
                result.MaterialInstance.DisableKeyword("GLOW_ON");
            }
        }

        /// <summary>
        /// Update only the glow color on an existing material (e.g. for fade animations).
        /// </summary>
        public static void SetGlowColor(Material mat, Color color)
        {
            if (mat == null) return;
            mat.SetColor(ID_GlowColor, color);
        }
    }
}
