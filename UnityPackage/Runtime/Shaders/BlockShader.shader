Shader "reromanlee/BlockyMesher/Blocks"
{
    Properties
    {
        [NoScaleOffset] _Textures ("Block Textures", 2DArray) = "" {}
        [NoScaleOffset] _Occlusion ("Ambient Occlusion Tiles", 2DArray) = "" {}
        _OcclusionStrength ("Ambient Occlusion Strength", Range(0, 1)) = 1
        _MinimumLight ("Minimum Light", Range(0, 1)) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        // Render states, set per render pass by BlockRegistry.
        [HideInInspector] _SrcBlend ("Source Blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite ("Depth Write", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half _OcclusionStrength;
            half _MinimumLight;
            half _Cutoff;
        CBUFFER_END

        TEXTURE2D_ARRAY(_Textures);
        SAMPLER(sampler_Textures);
        TEXTURE2D_ARRAY(_Occlusion);
        SAMPLER(sampler_Occlusion);

        // Set from C# through BlockLighting.SkyColor, so a day/night cycle never rebuilds meshes.
        half4 _BlockySkyColor;

        // Every value is a small whole number packed into a normalized byte, see SectionVertex.cs.
        struct Attributes
        {
            float4 positionAndCorner : POSITION;
            half4 blockLight : COLOR;
            float4 face : TEXCOORD0;
        };

        float3 UnpackPosition(Attributes input)
        {
            return round(input.positionAndCorner.xyz * 255.0);
        }

        float2 UnpackUV(Attributes input)
        {
            uint corner = (uint)round(input.positionAndCorner.w * 255.0);
            return float2(corner & 1u, corner >> 1u);
        }
        ENDHLSL

        Pass
        {
            Name "BlockyForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_local _ _ALPHATEST_ON

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float2 layers : TEXCOORD1;
                half3 light : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(UnpackPosition(input));
                output.uv = UnpackUV(input);
                output.layers = round(input.face.xy * 255.0);

                // Each color channel takes the brighter of skylight and block light.
                half skyLevel = input.face.z * (255.0 / 7.0);
                half3 light = max(_BlockySkyColor.rgb * skyLevel, input.blockLight.rgb);
                output.light = max(light, _MinimumLight);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D_ARRAY(_Textures, sampler_Textures, input.uv, input.layers.x);
                #if defined(_ALPHATEST_ON)
                    clip(albedo.a - _Cutoff);
                #endif
                half occlusion = SAMPLE_TEXTURE2D_ARRAY(_Occlusion, sampler_Occlusion, input.uv, input.layers.y).r;
                half3 color = albedo.rgb * input.light * lerp(1.0h, occlusion, _OcclusionStrength);
                return half4(MixFog(color, input.fogFactor), albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_local _ _ALPHATEST_ON

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float layer : TEXCOORD1;
            };

            DepthVaryings DepthVert(Attributes input)
            {
                DepthVaryings output;
                output.positionCS = TransformObjectToHClip(UnpackPosition(input));
                output.uv = UnpackUV(input);
                output.layer = round(input.face.x * 255.0);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    clip(SAMPLE_TEXTURE2D_ARRAY(_Textures, sampler_Textures, input.uv, input.layer).a - _Cutoff);
                #endif
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
