Shader "reromanlee/BlockyMesher/Crack"
{
    // Drawn over a block that is being broken. Multiplies what is already on screen, so white
    // leaves the block as it is and the dark crack lines darken it, whatever its light.
    Properties
    {
        [NoScaleOffset] _Cracks ("Crack Stages", 2DArray) = "" {}
        _Stage ("Stage", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "BlockyCrack"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Stage;
            CBUFFER_END

            TEXTURE2D_ARRAY(_Cracks);
            SAMPLER(sampler_Cracks);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return SAMPLE_TEXTURE2D_ARRAY(_Cracks, sampler_Cracks, input.uv, _Stage);
            }
            ENDHLSL
        }
    }
}
