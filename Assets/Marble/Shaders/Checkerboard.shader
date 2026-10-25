Shader "Marble/Checkerboard"
{
    Properties
    {
        _ColorA ("Color A", Color) = (1, 1, 1, 1)
        _ColorB ("Color B", Color) = (0.02, 0.02, 0.02, 1)
        _Tiling ("Tiling (XY)", Vector) = (1, 1, 0, 0)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
        _SpecularStrength ("Specular Strength", Range(0, 1)) = 0.15
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        _OcclusionCutout ("Occlusion Cutout", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _ColorA;
            half4 _ColorB;
            float4 _Tiling;
            half _Smoothness;
            half _SpecularStrength;
            half _OcclusionCutout;
        CBUFFER_END

        // Set globally by MarbleOcclusionCutout.
        float4 _MarbleCutoutPosition; // xyz = ball world position, w = 1 when active
        float4 _MarbleCutoutParams;   // x = radius at ball distance, y = edge softness, z = depth margin, w = depth fade

        // Dithered see-through hole around the ball for geometry between the camera and the ball.
        // Must run identically in every pass that writes depth, or the depth prepass still hides the ball.
        void ClipOcclusionCutout(float3 positionWS, float2 pixel)
        {
            if (_MarbleCutoutPosition.w < 0.5 || _OcclusionCutout <= 0.0)
                return;

            float3 cameraWS = GetCameraPositionWS();
            float3 toBall = _MarbleCutoutPosition.xyz - cameraWS;
            float ballDistance = length(toBall);
            float3 axis = toBall / max(ballDistance, 1e-4);

            float3 toFragment = positionWS - cameraWS;
            float along = dot(toFragment, axis);
            if (along <= 1e-3)
                return;

            // Perpendicular offset rescaled to the ball's distance, so the hole is a cone (a circle on screen).
            float radial = length(toFragment - axis * along) * ballDistance / along;
            float radius = _MarbleCutoutParams.x;
            float edge = max(radius * _MarbleCutoutParams.y, 1e-3);
            float radialMask = 1.0 - smoothstep(radius - edge, radius, radial);

            float nearLimit = ballDistance - _MarbleCutoutParams.z;
            float depthMask = 1.0 - smoothstep(nearLimit - _MarbleCutoutParams.w, nearLimit, along);

            float cut = radialMask * depthMask * _OcclusionCutout;
            float noise = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            clip(noise - cut);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);

                Varyings output;
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                output.uv = input.uv * _Tiling.xy;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            // Box-filtered checker (Inigo Quilez): integrates the pattern over the pixel footprint,
            // which keeps edges crisp up close and fades to grey instead of aliasing in the distance.
            half FilteredChecker(float2 p)
            {
                float2 w = max(fwidth(p), 1e-4);
                float2 i = 2.0 * (abs(frac((p - 0.5 * w) * 0.5) - 0.5) - abs(frac((p + 0.5 * w) * 0.5) - 0.5)) / w;
                return 0.5 - 0.5 * i.x * i.y;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                ClipOcclusionCutout(input.positionWS, input.positionCS.xy);

                half3 albedo = lerp(_ColorA.rgb, _ColorB.rgb, FilteredChecker(input.uv));

                float3 normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(frontFace, 1.0, -1.0);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half attenuation = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half nDotL = saturate(dot(normalWS, mainLight.direction));

                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                half specPower = exp2(10.0 * _Smoothness + 1.0);
                half specular = pow(saturate(dot(normalWS, halfDir)), specPower) * _SpecularStrength * nDotL;

                half3 color = albedo * SampleSH(normalWS)
                            + (albedo * nDotL + specular) * mainLight.color * attenuation;

                return half4(MixFog(color, input.fogFactor), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 ShadowFrag(float4 positionCS : SV_POSITION) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            DepthVaryings DepthVert(float4 positionOS : POSITION)
            {
                DepthVaryings output;
                output.positionWS = TransformObjectToWorld(positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                ClipOcclusionCutout(input.positionWS, input.positionCS.xy);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
