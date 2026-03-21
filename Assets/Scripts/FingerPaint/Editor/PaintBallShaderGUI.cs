using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom ShaderGUI for "FingerPaint/PaintBall Transparent".
/// Automatically sets _SrcBlend / _DstBlend when _BlendMode changes.
/// </summary>
public class PaintBallShaderGUI : ShaderGUI
{
    public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
    {
        base.OnGUI(editor, properties);

        foreach (var target in editor.targets)
        {
            if (target is Material mat)
                ApplyBlendMode(mat);
        }
    }

    private static void ApplyBlendMode(Material mat)
    {
        if (!mat.HasProperty("_BlendMode")) return;

        int mode = (int)mat.GetFloat("_BlendMode");

        if (mode == 1) // Additive
        {
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.EnableKeyword("_BLENDMODE_ADDITIVE");
            mat.DisableKeyword("_BLENDMODE_ALPHA");
        }
        else // Alpha (default)
        {
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_BLENDMODE_ALPHA");
            mat.DisableKeyword("_BLENDMODE_ADDITIVE");
        }
    }
}
