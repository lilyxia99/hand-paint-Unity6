using UnityEngine;

namespace FingerPaint
{
    /// <summary>
    /// Creates a multi-layer glow halo behind a TextMesh.
    /// Uses 3 progressively larger, more transparent copies to simulate soft glow.
    /// No shaders or post-processing needed — works in VR on Quest.
    /// </summary>
    public static class TextGlowHelper
    {
        /// <summary>
        /// Adds glow layers behind an existing TextMesh.
        /// Call once during build — returns the glow TextMeshes so you can update their text later.
        /// </summary>
        /// <param name="parent">Parent transform (same as the main text's parent).</param>
        /// <param name="mainText">The main TextMesh to create glow for.</param>
        /// <param name="glowColor">Base glow color (alpha will be overridden per layer).</param>
        /// <returns>Array of glow TextMeshes (update their .text when main text changes).</returns>
        public static TextMesh[] AddGlow(Transform parent, TextMesh mainText, Color glowColor)
        {
            // 3 layers: close+bright → far+faint
            float[] scaleMultipliers = { 1.15f, 1.35f, 1.6f };
            float[] alphas           = { 0.25f, 0.12f, 0.05f };
            float[] zOffsets         = { 0.001f, 0.002f, 0.003f };

            var glows = new TextMesh[scaleMultipliers.Length];

            for (int i = 0; i < scaleMultipliers.Length; i++)
            {
                var go = new GameObject(mainText.gameObject.name + $"_Glow{i}");
                go.transform.SetParent(parent, false);

                var tm = go.AddComponent<TextMesh>();
                tm.fontSize = mainText.fontSize;
                tm.characterSize = mainText.characterSize * scaleMultipliers[i];
                tm.anchor = mainText.anchor;
                tm.alignment = mainText.alignment;
                tm.color = new Color(glowColor.r, glowColor.g, glowColor.b, alphas[i]);
                tm.text = mainText.text;

                // Position behind main text
                Vector3 pos = mainText.transform.localPosition;
                pos.z += zOffsets[i];
                go.transform.localPosition = pos;

                glows[i] = tm;
            }

            return glows;
        }

        /// <summary>
        /// Update text on all glow layers at once.
        /// </summary>
        public static void SetText(TextMesh[] glows, string text)
        {
            if (glows == null) return;
            for (int i = 0; i < glows.Length; i++)
            {
                if (glows[i] != null)
                    glows[i].text = text;
            }
        }

        /// <summary>
        /// Update color on all glow layers (preserves per-layer alpha).
        /// </summary>
        public static void SetColor(TextMesh[] glows, Color baseColor)
        {
            if (glows == null) return;
            for (int i = 0; i < glows.Length; i++)
            {
                if (glows[i] != null)
                {
                    float a = glows[i].color.a; // preserve layer alpha
                    glows[i].color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
                }
            }
        }

        /// <summary>
        /// Fade all glow layers by a multiplier (e.g. for fade-out animations).
        /// </summary>
        public static void SetAlphaMultiplier(TextMesh[] glows, Color baseColor, float multiplier)
        {
            if (glows == null) return;
            float[] baseAlphas = { 0.25f, 0.12f, 0.05f };
            for (int i = 0; i < glows.Length && i < baseAlphas.Length; i++)
            {
                if (glows[i] != null)
                    glows[i].color = new Color(baseColor.r, baseColor.g, baseColor.b, baseAlphas[i] * multiplier);
            }
        }
    }
}
