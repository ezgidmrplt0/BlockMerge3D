Shader "Custom/LiquidIce"
{
    Properties
    {
        _Color ("Liquid Deep Color", Color) = (0.04, 0.28, 0.52, 0.90)
        _BaseColor ("Base Color", Color) = (0.04, 0.28, 0.52, 0.90)
        _ShallowColor ("Liquid Surface Color", Color) = (0.12, 0.42, 0.68, 0.92)
        _RimColor ("Fresnel Rim Color", Color) = (0.40, 0.70, 0.88, 0.85)
        _EmissionColor ("Emission Color", Color) = (0.01, 0.06, 0.15, 1.0)
        
        [Header(Liquid Surface Caustics)]
        _CausticScale ("Caustic Scale", Range(1, 20)) = 5.0
        _CausticSpeed ("Caustic Speed", Range(0, 5)) = 1.2
        _CausticIntensity ("Caustic Intensity", Range(0, 3)) = 0.3
        _CausticPower ("Caustic Sharpness", Range(1, 10)) = 2.5
        
        [Header(Fresnel Glass Rim)]
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.0
        _SpecularPower ("Gloss Specular Power", Range(4, 128)) = 32.0
    }

    SubShader
    {
        Tags 
        { 
            "Queue"="Transparent" 
            "RenderType"="Transparent" 
            "IgnoreProjector"="True"
        }
        
        LOD 200
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Cull Off

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
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
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _BaseColor;
                half4 _ShallowColor;
                half4 _RimColor;
                half4 _EmissionColor;

                float _CausticScale;
                float _CausticSpeed;
                float _CausticIntensity;
                float _CausticPower;

                float _FresnelPower;
                float _SpecularPower;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.uv = input.uv;

                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                
                Light mainLight = GetMainLight();
                float3 lightDirWS = normalize(mainLight.direction);

                // Combine _BaseColor and _Color for property block compatibility
                half4 baseCol = _BaseColor * _Color;

                // Fresnel (Liquid Rim Translucency)
                float NdotV = saturate(dot(normalWS, viewDirWS));
                float fresnel = pow(1.0 - NdotV, _FresnelPower);

                // Dark Liquid Depth Color Gradient
                half4 liquidCol = lerp(baseCol, _ShallowColor, fresnel * 0.5);
                liquidCol.rgb = lerp(liquidCol.rgb, _RimColor.rgb, fresnel * 0.5);

                // Procedural Caustic Liquid Ripples on Texture Surface
                float2 cUV = input.positionWS.xz * _CausticScale;
                float cTime = _Time.y * _CausticSpeed;
                float c1 = sin(cUV.x * 3.14 + cTime) + cos(cUV.y * 3.14 + cTime * 1.2);
                float c2 = sin((cUV.x + cUV.y) * 2.5 - cTime * 0.9);
                float causticPattern = pow(saturate((c1 + c2) * 0.25 + 0.5), _CausticPower) * _CausticIntensity;

                liquidCol.rgb += float3(0.08, 0.30, 0.50) * causticPattern;

                // Liquid Specular Highlight (Soft Gloss Shine)
                float3 halfDir = normalize(lightDirWS + viewDirWS);
                float NdotH = saturate(dot(normalWS, halfDir));
                float spec = pow(NdotH, _SpecularPower);
                liquidCol.rgb += float3(0.70, 0.85, 0.95) * spec * 0.35;

                // Soft Emission Depth Glow
                liquidCol.rgb += _EmissionColor.rgb;

                // Gentle Liquid Surface Shimmer
                float pulse = 0.5 + 0.5 * sin(_Time.y * 1.5);
                liquidCol.rgb += float3(0.005, 0.02, 0.04) * pulse;

                liquidCol.a = saturate(liquidCol.a + fresnel * 0.1);

                return liquidCol;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
