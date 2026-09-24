Shader "romanlee17/MarchingCubes/BlockShader" {

    Properties {
        _BlocksTexture ("Blocks (RGB)", 2D) = "white" { }
        _LightmapTexture ("Lightmap (RGB)", 2D) = "white" { }
        _BreakingTexture ("Breaking (RGB)", 2D) = "white" { }
        _OcclusionTexture ("Occlusion (RGB)", 2D) = "white" { }
        _SkylightColor ("Skylight (RGB)", COLOR) = (1, 1, 1)
    }

    SubShader {
        
        Tags {
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
            "RenderType" = "TransparentCutout"
        }
        
        LOD 100
        Lighting Off

        Pass {
            // ########## SHADER PASS START ##########

            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            // Vertex input.
            struct appdata {
                float4 vertex : POSITION;
                float2 colormapCoord : TEXCOORD0;
                fixed4 blocklightColor : COLOR;
                float2 lightmapIntensity : TEXCOORD1;
                float2 lightmapCoord : TEXCOORD2;
                float2 breakingCoord : TEXCOORD3;
                float2 occlusionCoord : TEXCOORD4;
            };

            // Vertex into Fragment conversion.
            struct v2f {
                float4 vertex : SV_POSITION;
                float2 colormapCoord : TEXCOORD0;
                fixed4 lightmapColor : COLOR;
                float2 lightmapCoord : TEXCOORD1;
                float2 breakingCoord : TEXCOORD2;
                float2 occlusionCoord : TEXCOORD3;
                UNITY_FOG_COORDS(4)
            };

            // Define shader properties.
            sampler2D _BlocksTexture;
            sampler2D _LightmapTexture;
            sampler2D _BreakingTexture;
            sampler2D _OcclusionTexture;
            fixed4 _SkylightColor;

            // Color blending towards maximum brightness.
            fixed4 blendColors(fixed4 colorA, fixed4 colorB) {
                fixed4 outputColor;
                outputColor.r = max(colorA.r, colorB.r);
                outputColor.g = max(colorA.g, colorB.g);
                outputColor.b = max(colorA.b, colorB.b);
                outputColor.a = max(colorA.a, colorB.a);
                return outputColor;
            }

            // Vertex stage.
            v2f vert (appdata v) {
                v2f o;
                // Vertex position.
                o.vertex = UnityObjectToClipPos(v.vertex);
                UNITY_TRANSFER_FOG(o, o.vertex);
                // Block texture UVs.
                o.colormapCoord = v.colormapCoord;
                // Calculate skylight and blocklight lightmap colors.
                fixed4 skylightColor = _SkylightColor * v.lightmapIntensity.x;
                fixed4 blocklightColor = v.blocklightColor * v.lightmapIntensity.y;
                // Blended lightmap color.
                o.lightmapColor = blendColors(skylightColor, blocklightColor);
                // Lightmap texture UVs.
                o.lightmapCoord = v.lightmapCoord;
                // Breaking texture UVs.
                o.breakingCoord = v.breakingCoord;
                // Occlusion texture UVs.
                o.occlusionCoord = v.occlusionCoord;
                return o;
            }

            // Fragment stage.
            fixed4 frag (v2f i) : SV_Target {
                fixed4 vertexColor = tex2D(_BlocksTexture, i.colormapCoord);
                // Apply block texture transparency cut off.
                clip(vertexColor.a - 0.5);
                // Calculate skylight and blocklight lightmap colors.
                vertexColor = vertexColor * i.lightmapColor;
                // vertexColor = vertexColor *
                    // Blended lightmap.
                    // ; // *
                    // Breaking texture.
                    // tex2D(_BreakingTexture, i.breakingCoord) *
                    // Ambiemt occlusion.
                    // tex2D(_OcclusionTexture, i.occlusionCoord);
                // Finally, apply Unity fog for the output color.
                UNITY_APPLY_FOG(i.fogCoord, vertexColor);
                return vertexColor;
            }

            ENDCG

            // ########## SHADER PASS END ##########
        }

    }
}