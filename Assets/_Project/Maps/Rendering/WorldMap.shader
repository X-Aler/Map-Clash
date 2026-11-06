Shader "MapClash/WorldMap"
{
    Properties
    {
        _MainTex ("Terrain", 2D) = "white" {}
        _Ownership ("Ownership", 2D) = "black" {}
        _TerrainView ("Terrain View", Float) = 0
        _MenuDim ("Menu Dim", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Ownership); SAMPLER(sampler_Ownership);
            float4 _MainTex_TexelSize;
            float4 _CountryColors[64];
            float _TerrainView;
            float _MenuDim;
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half4 terrain = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                int owner = (int)round(SAMPLE_TEXTURE2D(_Ownership, sampler_Ownership, uv).r * 255);
                half3 color = terrain.rgb;
                if (terrain.a > 0.5 && owner > 0 && owner < 64)
                {
                    color = lerp(_CountryColors[owner].rgb * (terrain.g * 0.4 + 0.65), terrain.rgb, _TerrainView * 0.88);
                    float2 dx = float2(_MainTex_TexelSize.x, 0);
                    float2 dy = float2(0, _MainTex_TexelSize.y);
                    int left = (int)round(SAMPLE_TEXTURE2D(_Ownership, sampler_Ownership, uv - dx).r * 255);
                    int right = (int)round(SAMPLE_TEXTURE2D(_Ownership, sampler_Ownership, uv + dx).r * 255);
                    int down = (int)round(SAMPLE_TEXTURE2D(_Ownership, sampler_Ownership, uv - dy).r * 255);
                    int up = (int)round(SAMPLE_TEXTURE2D(_Ownership, sampler_Ownership, uv + dy).r * 255);
                    if (left != owner || right != owner || down != owner || up != owner)
                    {
                        color *= 0.34;
                    }
                }
                if (terrain.a < 0.5)
                {
                    float2 grid = abs(frac(uv * float2(24, 12) + 0.5) - 0.5);
                    float gridLine = 1 - smoothstep(0.002, 0.006, min(grid.x, grid.y));
                    color += gridLine * half3(0.022, 0.028, 0.032);
                }
                return half4(color * (1 - _MenuDim * 0.3), 1);
            }
            ENDHLSL
        }
    }
}
