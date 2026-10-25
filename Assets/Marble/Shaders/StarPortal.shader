Shader "Marble/StarPortal"
{
    Properties
    {
        [HDR] _SpaceColor ("Space Color", Color) = (0.04, 0.0, 0.1, 1)
        [HDR] _ArmColor ("Spiral Arm Color", Color) = (0.55, 0.15, 1.3, 1)
        [HDR] _CenterColor ("Center Glow Color", Color) = (1.6, 1.0, 2.6, 1)
        [HDR] _RimColor ("Rim Color", Color) = (1.4, 0.45, 2.4, 1)
        [HDR] _StarColor ("Star Color", Color) = (1.8, 1.6, 2.4, 1)
        [IntRange] _Arms ("Spiral Arms", Range(1, 8)) = 3
        _Tightness ("Spiral Tightness", Range(0, 10)) = 4
        _SwirlSpeed ("Swirl Speed", Range(-4, 4)) = 1.2
        _StarDensity ("Star Density", Range(8, 64)) = 28
        _StarSpeed ("Star Inflow Speed", Range(0, 2)) = 0.4
        _RimWidth ("Rim Width", Range(0.005, 0.2)) = 0.045
        _FunnelDepth ("Funnel Depth", Range(0, 3)) = 0.9
        _Wobble ("Edge Wobble", Range(0, 0.2)) = 0.035
        _Intensity ("Intensity", Range(0, 6)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SpaceColor;
                half4 _ArmColor;
                half4 _CenterColor;
                half4 _RimColor;
                half4 _StarColor;
                float _Arms;
                float _Tightness;
                float _SwirlSpeed;
                float _StarDensity;
                float _StarSpeed;
                float _RimWidth;
                float _FunnelDepth;
                float _Wobble;
                half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 disc : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            // The disc lies in the local XY plane with UVs spanning it; the centre is pushed back along +Z
            // so the portal reads as a funnel, and the rim ripples so the silhouette isn't a perfect circle.
            Varyings Vert(Attributes input)
            {
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                float angle = atan2(p.y, p.x);
                float inner = saturate(1.0 - r);

                float3 positionOS = input.positionOS.xyz;
                positionOS.xy *= 1.0 + sin(angle * 5.0 + r * 6.0 + _Time.y * 2.0) * _Wobble * r;
                positionOS.z += _FunnelDepth * inner * inner;

                VertexPositionInputs positions = GetVertexPositionInputs(positionOS);

                Varyings output;
                output.positionCS = positions.positionCS;
                output.disc = p;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            float2 Hash22(float2 p)
            {
                float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Stars on a log-polar grid: cells shrink toward the centre and scroll inward over time, so stars
            // appear to fall into the vortex while getting smaller. Columns must be even so the grid wraps at the atan2 seam.
            half StarLayer(float angle01, float logR, float columns, float speed, float seed)
            {
                float radialScale = columns / (2.0 * PI);
                float2 coord = float2(angle01 * columns, logR * radialScale + _Time.y * speed * radialScale);
                float2 cell = floor(coord);
                cell.x = fmod(cell.x + columns, columns);
                float2 local = frac(coord);

                float2 h = Hash22(cell + seed);
                float present = step(0.55, Hash12(cell + seed * 1.7));
                float2 starPos = 0.2 + 0.6 * h;
                float d = length(local - starPos);

                float twinkle = 0.6 + 0.4 * sin(_Time.y * (2.0 + h.x * 4.0) + h.y * 6.2831);
                return present * twinkle * smoothstep(0.14, 0.0, d);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.disc;
                float r = length(p);
                float angle = atan2(p.y, p.x);
                float logR = log(max(r, 1e-3));
                float t = _Time.y;

                float spiralA = 0.5 + 0.5 * cos(_Arms * angle + _Tightness * logR + t * _SwirlSpeed * 2.0);
                float spiralB = 0.5 + 0.5 * cos(_Arms * 2.0 * angle + _Tightness * 1.6 * logR + t * _SwirlSpeed * 3.1);
                float arms = pow(spiralA, 3.0) + 0.35 * pow(spiralB, 4.0);
                arms *= smoothstep(1.0, 0.15, r);

                // Swirl the star field faster near the centre so it spins with the arms.
                float swirl = t * _SwirlSpeed * 0.05 / (r + 0.25);
                float angle01 = frac(angle / (2.0 * PI) + 0.5 - swirl);
                float columns = max(2.0, 2.0 * floor(_StarDensity * 0.5));
                half stars = StarLayer(angle01, logR, columns, _StarSpeed, 0.0)
                           + 0.6 * StarLayer(frac(angle01 + 0.37), logR, columns * 2.0, _StarSpeed * 1.4, 17.0);
                stars *= smoothstep(0.04, 0.25, r) * smoothstep(1.0, 0.85, r);

                float rimDistance = (r - (1.0 - _RimWidth * 2.0)) / _RimWidth;
                half rim = exp(-rimDistance * rimDistance);
                half center = exp(-r * r * 18.0);

                half3 color = _SpaceColor.rgb
                            + _ArmColor.rgb * arms
                            + _CenterColor.rgb * center
                            + _RimColor.rgb * rim
                            + _StarColor.rgb * stars;

                half alpha = smoothstep(1.0, 1.0 - _RimWidth, r);
                color = MixFog(color * _Intensity, input.fogFactor);
                return half4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
