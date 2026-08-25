Shader "WorldBuilder/TerrainSplat"
{
    Properties
    {
        _Control ("Control (Splatmap)", 2D) = "green" {}
        _Splat0 ("Layer 0 (Sand)", 2D) = "white" {}
        _Splat1 ("Layer 1 (Grass)", 2D) = "green" {}
        _Splat2 ("Layer 2 (Rock)", 2D) = "gray" {}
        _Splat3 ("Layer 3 (Seabed)", 2D) = "black" {}
        _NormalScale ("Texture Tiling", Float) = 0.25
        _Smoothness ("Smoothness", Range(0,1)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float4 color : COLOR0;
        };

        struct Varyings
        {
            float4 positionHCS : SV_POSITION;
            float2 uvSplat : TEXCOORD0;
            float2 uvDetail : TEXCOORD1;
            float3 positionWS : TEXCOORD2;
            float3 normalWS : TEXCOORD3;
        };

        TEXTURE2D(_Control);    SAMPLER(sampler_Control);
        TEXTURE2D(_Splat0);     SAMPLER(sampler_Splat0);
        TEXTURE2D(_Splat1);     SAMPLER(sampler_Splat1);
        TEXTURE2D(_Splat2);     SAMPLER(sampler_Splat2);
        TEXTURE2D(_Splat3);     SAMPLER(sampler_Splat3);

        CBUFFER_START(UnityPerMaterial)
            float4 _Control_ST;
            float _NormalScale;
            float _Smoothness;
        CBUFFER_END

        Varyings vert(Attributes IN)
        {
            Varyings OUT;
            VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
            VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS);
            OUT.positionHCS = posInputs.positionCS;
            OUT.positionWS = posInputs.positionWS;
            OUT.normalWS = normInputs.normalWS;
            OUT.uvSplat = IN.uv;
            OUT.uvDetail = IN.uv * _NormalScale;
            return OUT;
        }

        half4 frag(Varyings IN) : SV_Target
        {
            half4 weights = SAMPLE_TEXTURE2D(_Control, sampler_Control, IN.uvSplat);

            // Normalize so the four channels always sum to one.
            half total = weights.r + weights.g + weights.b + weights.a;
            weights /= max(total, 1e-4);

            half4 c0 = SAMPLE_TEXTURE2D(_Splat0, sampler_Splat0, IN.uvDetail);
            half4 c1 = SAMPLE_TEXTURE2D(_Splat1, sampler_Splat1, IN.uvDetail);
            half4 c2 = SAMPLE_TEXTURE2D(_Splat2, sampler_Splat2, IN.uvDetail);
            half4 c3 = SAMPLE_TEXTURE2D(_Splat3, sampler_Splat3, IN.uvDetail);

            half3 albedo = c0.rgb * weights.r + c1.rgb * weights.g +
                           c2.rgb * weights.b + c3.rgb * weights.a;

            // Eroded areas (low vertex-color green channel) expose rock automatically.
            half rockBlend = saturate(1.0 - IN.color.g);
            albedo = lerp(albedo, c2.rgb, rockBlend * 0.65);

#if defined(_VERTEXCOLORS_ON)
            // When vertex colors are present they tint the terrain slightly.
#endif

            SurfaceData surfaceData = (SurfaceData)0;
            surfaceData.albedo = albedo;
            surfaceData.metallic = 0.0h;
            surfaceData.smoothness = _Smoothness;
            surfaceData.occlusion = 1.0h;
            surfaceData.alpha = 1.0h;

            InputData inputData = (InputData)0;
            inputData.positionWS = IN.positionWS;
            inputData.normalWS = normalize(IN.normalWS);
            inputData.viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(IN.positionWS));
            inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
            inputData.fogFactorAndVertexLight = 0;
            inputData.vertexLighting = half3(0, 0, 0);
            inputData.bakedGI = SampleSH(inputData.normalWS);

            half4 color = UniversalFragmentPBR(inputData, surfaceData);
            color.rgb = MixFog(color.rgb, inputData.fogFactorAndVertexLight.x);
            color.a = 1.0;
            return color;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings shadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return OUT;
            }

            half4 shadowFrag(ShadowVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
