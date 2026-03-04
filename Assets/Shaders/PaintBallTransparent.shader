Shader "FingerPaint/PaintBall Transparent"
{
    Properties
    {
        [Header(Color)]
        _BaseColor ("Base Color", Color) = (1, 0.4, 0.2, 0.5)
        _Opacity   ("Opacity",   Range(0, 1)) = 0.5

        [Header(Fresnel Rim)]
        [Toggle] _EnableFresnel ("Enable Fresnel Rim", Float) = 1
        _FresnelColor  ("Fresnel Color",    Color) = (1, 1, 1, 1)
        _FresnelPower  ("Fresnel Power",    Range(0.5, 8)) = 2.5
        _FresnelScale  ("Fresnel Strength", Range(0, 2)) = 0.8

        [Header(Emission)]
        [Toggle] _EnableEmission ("Enable Emission", Float) = 1
        _EmissionColor    ("Emission Color",    Color) = (1, 0.4, 0.2, 1)
        _EmissionIntensity ("Emission Intensity", Range(0, 3)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // URP core includes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // SRP Batcher compatibility: all material properties in one CBUFFER
            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                half   _Opacity;
                half   _EnableFresnel;
                half4  _FresnelColor;
                half   _FresnelPower;
                half   _FresnelScale;
                half   _EnableEmission;
                half4  _EmissionColor;
                half   _EmissionIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewDirWS   : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS   = nrmInputs.normalWS;
                output.viewDirWS  = GetWorldSpaceNormalizeViewDir(posInputs.positionWS);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Renormalise after interpolation
                float3 N = normalize(input.normalWS);
                float3 V = normalize(input.viewDirWS);

                // --- Simple directional lighting (main light) ---
                Light mainLight = GetMainLight();
                half  NdotL     = saturate(dot(N, mainLight.direction));
                half3 diffuse   = _BaseColor.rgb * mainLight.color * (NdotL * 0.6 + 0.4);
                // ↑ wrap-lighting: 60 % Lambert + 40 % ambient fill

                // --- Fresnel rim (makes edges of the sphere glow) ---
                half3 fresnel = half3(0, 0, 0);
                if (_EnableFresnel > 0.5)
                {
                    half  rim = 1.0 - saturate(dot(N, V));
                    fresnel   = _FresnelColor.rgb * _FresnelScale * pow(rim, _FresnelPower);
                }

                // --- Emission (inner glow) ---
                half3 emission = half3(0, 0, 0);
                if (_EnableEmission > 0.5)
                {
                    emission = _EmissionColor.rgb * _EmissionIntensity;
                }

                half3 finalColor = diffuse + fresnel + emission;
                half  finalAlpha = _Opacity;

                return half4(finalColor, finalAlpha);
            }
            ENDHLSL
        }

        // Depth-only pass so transparent objects still write to the depth
        // pre-pass when needed (e.g. for SSAO, decals)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
