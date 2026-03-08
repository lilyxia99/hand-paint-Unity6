Shader "FingerPaint/Holographic Particle Hand"
{
    Properties
    {
        [Header(Colors)]
        _ColorCore      ("Core Color (Blue)",      Color) = (0.15, 0.4, 1.0, 1)
        _ColorAccent    ("Accent Color (Purple)",  Color) = (0.7, 0.15, 0.9, 1)
        _ColorHighlight ("Highlight Color (Cyan)", Color) = (0.5, 0.75, 1.0, 1)

        [Header(Glow)]
        _Intensity ("Glow Intensity", Range(0, 20)) = 5

        [Header(Particle Dots)]
        _DotDensity   ("Dot Density",    Range(10, 300)) = 200
        _DotSize      ("Dot Size",       Range(0.02, 0.5)) = 0.28
        _DotMinBright ("Min Brightness", Range(0.1, 1))  = 0.3

        [Header(Sparkle Layer)]
        _SparkleScale ("Sparkle Density Multiplier", Range(1.5, 4)) = 2.2
        _SparkleSize  ("Sparkle Dot Size",           Range(0.02, 0.25)) = 0.16
        _SparkleRate  ("Sparkle Flash Speed",        Range(0.5, 5)) = 2.0

        [Header(Edge Concentration)]
        _EdgeConcentration ("Edge vs Center", Range(0, 1)) = 0.65
        _FresnelPow   ("Fresnel Power",          Range(0.5, 8)) = 2.5
        _FresnelBright ("Edge Dot Boost",         Range(0, 5))  = 2.5

        [Header(Animation)]
        _AnimSpeed     ("Animation Speed",        Range(0, 3))    = 0.8
        _VertexDisplace ("Vertex Dispersion (m)", Range(0, 0.03)) = 0.005
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+10"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HolographicParticles"
            Tags { "LightMode" = "UniversalForward" }

            // Additive-alpha: hand glows on top of whatever is behind
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ─── SRP Batcher: all material props in one CBUFFER ─────────
            CBUFFER_START(UnityPerMaterial)
                half4 _ColorCore;
                half4 _ColorAccent;
                half4 _ColorHighlight;
                half  _Intensity;
                half  _DotDensity;
                half  _DotSize;
                half  _DotMinBright;
                half  _SparkleScale;
                half  _SparkleSize;
                half  _SparkleRate;
                half  _EdgeConcentration;
                half  _FresnelPow;
                half  _FresnelBright;
                half  _AnimSpeed;
                half  _VertexDisplace;
            CBUFFER_END

            // ─── Structs ────────────────────────────────────────────────

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewDirWS   : TEXCOORD1;
                float3 positionOS  : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ─── Hash functions (cheap, GPU-friendly) ───────────────────

            float3 Hash33(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453);
            }

            float Hash13(float3 p)
            {
                return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453);
            }

            // ─── Vertex ─────────────────────────────────────────────────

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 pos = v.positionOS.xyz;

                // Subtle per-vertex jitter along normal (breathing particles)
                float n = Hash13(floor(pos * 40.0)
                                 + floor(_Time.y * _AnimSpeed * 3.0));
                pos += v.normalOS * n * _VertexDisplace;

                o.positionOS  = v.positionOS.xyz;
                o.positionHCS = TransformObjectToHClip(pos);
                float3 posWS  = TransformObjectToWorld(pos);
                o.normalWS    = TransformObjectToWorldNormal(v.normalOS);
                o.viewDirWS   = GetWorldSpaceNormalizeViewDir(posWS);

                return o;
            }

            // ─── Fragment ───────────────────────────────────────────────

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // ── Fresnel first — drives edge concentration ──────

                half3 N       = normalize(i.normalWS);
                half3 V       = normalize(i.viewDirWS);
                half  fresnel = pow(1.0 - saturate(dot(N, V)), _FresnelPow);

                // edgeFactor: 1.0 at silhouette edge, (1-_EdgeConcentration) at center
                // Controls how much dots concentrate toward edges
                half edgeFactor = lerp(1.0 - _EdgeConcentration, 1.0, saturate(fresnel));

                // Size scale: dots are bigger at edges, smaller at center
                half sizeEdge = lerp(0.35, 1.4, edgeFactor);

                // ── Layer 1: Primary particle dots ──────────────────

                float3 p1     = i.positionOS * _DotDensity;
                float3 cellId = floor(p1);
                float3 cellFr = frac(p1);

                float3 pt1   = Hash33(cellId) * 0.8 + 0.1;
                half   dist1 = length(cellFr - pt1);

                // Dot size scales with edge: bigger at silhouette
                half dot1  = 1.0 - smoothstep(0.0, _DotSize * sizeEdge, dist1);
                dot1 = pow(dot1, 1.4);

                half rand1 = Hash13(cellId);
                dot1 *= lerp(_DotMinBright, 1.0, rand1);
                dot1 *= edgeFactor;                              // dim center dots

                // ── Layer 2: Sparkle overlay (smaller, flashing) ────

                float3 p2   = i.positionOS * _DotDensity * _SparkleScale + 17.5;
                float3 id2  = floor(p2);
                float3 fr2  = frac(p2);
                float3 pt2  = Hash33(id2) * 0.8 + 0.1;
                half   dist2 = length(fr2 - pt2);
                half   dot2  = 1.0 - smoothstep(0.0, _SparkleSize * sizeEdge, dist2);
                dot2 = pow(dot2, 1.4);

                half flash = Hash13(id2 + floor(_Time.y * _SparkleRate));
                dot2 *= step(0.35, flash);
                dot2 *= edgeFactor;

                // ── Layer 3: Mid-fill dots (fills gaps between L1) ──

                float3 p3   = i.positionOS * _DotDensity * 0.65 + 37.3;
                float3 id3  = floor(p3);
                float3 fr3  = frac(p3);
                float3 pt3  = Hash33(id3) * 0.8 + 0.1;
                half   dist3 = length(fr3 - pt3);
                half   dot3  = 1.0 - smoothstep(0.0, _DotSize * 0.75 * sizeEdge, dist3);
                dot3 = pow(dot3, 1.4);
                half rand3 = Hash13(id3);
                dot3 *= lerp(_DotMinBright, 1.0, rand3);
                dot3 *= edgeFactor;

                // Combined dot mask — edge-concentrated
                half dotMask = saturate(dot1 + dot2 * 0.8 + dot3 * 0.55);

                // ── Color mixing ────────────────────────────────────

                half  mixRand = saturate(rand1 + rand3 * 0.3);
                half  colorT  = saturate(fresnel * 0.5 + mixRand * 0.4);
                half3 color   = lerp(_ColorCore.rgb, _ColorAccent.rgb, colorT);

                // Cyan/white highlights — more at edges
                color = lerp(color, _ColorHighlight.rgb, step(0.78, mixRand) * 0.6);

                // Edge glow boost on top of concentration
                color *= _Intensity * (1.0 + fresnel * _FresnelBright);

                // ── Final alpha: ONLY from dots ─────────────────────

                half alpha = dotMask;
                clip(alpha - 0.02);

                return half4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
