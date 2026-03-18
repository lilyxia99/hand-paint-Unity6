Shader "FingerPaint/Gradient Skybox"
{
    Properties
    {
        [Header(Top Colors)]
        _TopColorA   ("Top Color A",   Color) = (0.05, 0.05, 0.20, 1)
        _TopColorB   ("Top Color B",   Color) = (0.15, 0.02, 0.25, 1)

        [Header(Bottom Colors)]
        _BotColorA   ("Bottom Color A", Color) = (0.02, 0.10, 0.15, 1)
        _BotColorB   ("Bottom Color B", Color) = (0.10, 0.05, 0.12, 1)

        [Header(Animation)]
        _Speed       ("Cycle Speed",   Range(0.01, 2.0)) = 0.15
        _Exponent    ("Gradient Curve", Range(0.5, 4.0))  = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Background"
            "Queue" = "Background"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Skybox"
        }

        Pass
        {
            Name "GradientSkybox"
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColorA;
                half4 _TopColorB;
                half4 _BotColorA;
                half4 _BotColorB;
                half  _Speed;
                half  _Exponent;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDirOS  : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Pass object-space position as view direction for skybox
                output.viewDirOS  = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Normalise the view direction and use Y for vertical gradient
                float3 dir = normalize(input.viewDirOS);
                half   t   = saturate(dir.y * 0.5 + 0.5);        // 0 at bottom, 1 at top
                t = pow(t, _Exponent);                            // shape the falloff

                // Smooth sine-based blend factor (0-1 ping-pong)
                half blend = sin(_Time.y * _Speed) * 0.5 + 0.5;

                // Lerp between the two colour sets
                half3 topColor = lerp(_TopColorA.rgb, _TopColorB.rgb, blend);
                half3 botColor = lerp(_BotColorA.rgb, _BotColorB.rgb, blend);

                // Final vertical gradient
                half3 color = lerp(botColor, topColor, t);

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
