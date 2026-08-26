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
        _Triplanar ("Steep Triplanar Blend", Range(0,1)) = 0.8
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

        // Global weather channel driven by PrecipitationFx (Shader.SetGlobalFloat).
        float _WB_Wetness;

        CBUFFER_START(UnityPerMaterial)
            float4 _Control_ST;
            float _NormalScale;
            float _Smoothness;
            float _Triplanar;
        CBUFFER_END

        half3 SampleTriplanar(TEXTURE2D_PARAM(textureHandle, textureSampler),
            float3 positionWS, float3 normalWS, float tiling)
        {
            float3 absN = abs(normalize(normalWS));
            half3 w = pow(absN, 4.0);
            w /= max(w.x + w.y + w.z, 1e-4);

            half2 uvX = positionWS.zy * tiling;
            half2 uvY = positionWS.xz * tiling;
            half2 uvZ = positionWS.xy * tiling;

            return SAMPLE_TEXTURE2D(textureHandle, textureSampler, uvX).rgb * w.x +
                   SAMPLE_TEXTURE2D(textureHandle, textureSampler, uvY).rgb * w.y +
                   SAMPLE_TEXTURE2D(textureHandle, textureSampler, uvZ).rgb * w.z;
        }

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

            float3 normalWSn = normalize(IN.normalWS);
            float steep = 1.0 - saturate(abs(normalWSn).y);          // 0 flat · 1 vertical
            half triBlend = saturate(steep * 1.6) * saturate(_Triplanar);

            half3 c0 = lerp(SAMPLE_TEXTURE2D(_Splat0, sampler_Splat0, IN.uvDetail).rgb,
                SampleTriplanar(TEXTURE2D_ARGS(_Splat0, sampler_Splat0), IN.positionWS, normalWSn, _NormalScale), triBlend);
            half3 c1 = lerp(SAMPLE_TEXTURE2D(_Splat1, sampler_Splat1, IN.uvDetail).rgb,
                SampleTriplanar(TEXTURE2D_ARGS(_Splat1, sampler_Splat1), IN.positionWS, normalWSn, _NormalScale), triBlend);
            half3 c2 = lerp(SAMPLE_TEXTURE2D(_Splat2, sampler_Splat2, IN.uvDetail).rgb,
                SampleTriplanar(TEXTURE2D_ARGS(_Splat2, sampler_Splat2), IN.positionWS, normalWSn, _NormalScale), triBlend);
            half3 c3 = lerp(SAMPLE_TEXTURE2D(_Splat3, sampler_Splat3, IN.uvDetail).rgb,
                SampleTriplanar(TEXTURE2D_ARGS(_Splat3, sampler_Splat3), IN.positionWS, normalWSn, _NormalScale), triBlend);

            half3 albedo = c0 * weights.r + c1 * weights.g +
                           c2 * weights.b + c3 * weights.a;

            // Eroded areas (low vertex-color green channel) expose rock automatically.
            half rockBlend = saturate(1.0 - IN.color.g);
            albedo = lerp(albedo, c2.rgb, rockBlend * 0.65);

            // Rain wetness: darken + gloss up.
            half wet = saturate(_WB_Wetness);
            albedo *= lerp(1.0h, 0.62h, wet);
            float smoothnessOut = _Smoothness + wet * 0.45;

#if defined(_VERTEXCOLORS_ON)
            // When vertex colors are present they tint the terrain slightly.
#endif

            SurfaceData surfaceData = (SurfaceData)0;
            surfaceData.albedo = albedo;
            surfaceData.metallic = 0.0h;
            surfaceData.smoothness = saturate(smoothnessOut);
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
