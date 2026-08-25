Shader "WorldBuilder/WaterSurface"
{
    Properties
    {
        _DeepColor("Deep Color", Color) = (0.02, 0.10, 0.30, 1)
        _ShallowColor("Shallow Color", Color) = (0.10, 0.45, 0.55, 1)
        _FoamColor("Foam Color", Color) = (0.92, 0.97, 1.0, 1)
        _Alpha("Base Alpha", Range(0.2, 1)) = 0.86
        _WaveAmplitude("Wave Amplitude", Range(0, 1)) = 0.18
        _WaveLength("Wave Length", Range(0.5, 32)) = 9
        _WaveSpeed("Wave Speed", Range(0, 8)) = 1.4
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 3
        _FresnelBoost("Fresnel Sky Boost", Range(0, 1)) = 0.35
        _FoamThreshold("Foam Crest Threshold", Range(0, 1)) = 0.62
        _FoamSharpness("Foam Sharpness", Range(1, 16)) = 6
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
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _ShallowColor;
                float4 _FoamColor;
                float _Alpha;
                float _WaveAmplitude;
                float _WaveLength;
                float _WaveSpeed;
                float _FresnelPower;
                float _FresnelBoost;
                float _FoamThreshold;
                float _FoamSharpness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float crest : TEXCOORD2;
            };

            // Sum of three travelling sine waves; returns height and writes gradient dHdXZ.
            float WaveField(float2 wxz, out float2 gradient)
            {
                const float kPi = 3.14159265;
                float k = 2.0 * kPi / max(_WaveLength, 0.25);
                float t = _TimeParameters.x * _WaveSpeed;

                float2 dir0 = normalize(float2(1.0, 0.35));
                float2 dir1 = normalize(float2(-0.6, 1.0));
                float2 dir2 = normalize(float2(0.3, -1.0));

                float p0 = dot(wxz, dir0) * k + t * 1.00;
                float p1 = dot(wxz, dir1) * k * 1.83 + t * 1.31;
                float p2 = dot(wxz, dir2) * k * 2.61 + t * 0.77;

                float a0 = _WaveAmplitude;
                float a1 = _WaveAmplitude * 0.45;
                float a2 = _WaveAmplitude * 0.22;

                gradient = dir0 * (cos(p0) * k * a0)
                         + dir1 * (cos(p1) * k * a1)
                         + dir2 * (cos(p2) * k * a2);

                return sin(p0) * a0 + sin(p1) * a1 + sin(p2) * a2;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);

                float2 gradient;
                float height = WaveField(positionWS.xz, gradient);
                positionWS.y += height;

                float3 normalWS = normalize(float3(-gradient.x, 1.0, -gradient.y));

                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.positionWS = positionWS;
                OUT.normalWS = normalWS;
                OUT.crest = saturate((height / max(_WaveAmplitude, 0.001)) * 0.5 + 0.5);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float fresnel = pow(saturate(1.0 - saturate(dot(viewDir, IN.normalWS))), _FresnelPower);

                float depthMix = saturate(IN.crest);
                half4 color = lerp(_DeepColor, _ShallowColor, depthMix);

                float foamMask = smoothstep(_FoamThreshold, 1.0,
                    pow(IN.crest, _FoamSharpness * 0.25) + fresnel * 0.15);

                color.rgb += _FresnelBoost * fresnel;
                color.rgb = lerp(color.rgb, _FoamColor.rgb, foamMask);
                color.a = saturate(_Alpha + foamMask * 0.14);
                return color;
            }
            ENDHLSL
        }
    }
}
