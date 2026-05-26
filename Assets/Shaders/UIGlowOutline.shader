Shader "UI/GlowOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        _OutlineColor ("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.05)) = 0.015
        _GlowPower ("Glow Power", Range(1, 5)) = 2.0
        
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma shader_feature_local _ _USEUIALPHACLIP_ON

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            
            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _GlowPower;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;
                
                // Outline/Glow sampling
                float alpha = color.a;
                float outlineAlpha = 0.0;
                
                // Sample surrounding pixels to detect edges
                float2 offsets[8] = {
                    float2(-1, 0), float2(1, 0), float2(0, -1), float2(0, 1),
                    float2(-0.7, -0.7), float2(0.7, 0.7), float2(-0.7, 0.7), float2(0.7, -0.7)
                };
                
                for (int i = 0; i < 8; i++)
                {
                    float2 uvOffset = IN.texcoord + offsets[i] * _OutlineWidth;
                    // Clamp to make sure we don't bleed out of UV bounds
                    if (uvOffset.x >= 0.0 && uvOffset.x <= 1.0 && uvOffset.y >= 0.0 && uvOffset.y <= 1.0)
                    {
                        outlineAlpha += tex2D(_MainTex, uvOffset).a;
                    }
                }
                
                outlineAlpha /= 8.0;
                
                // If it is inside the solid part of the sprite, use the original color.
                // If it is on the edge/transparent part, draw the outline with glow.
                if (alpha < 0.9)
                {
                    float glow = pow(outlineAlpha, _GlowPower);
                    fixed4 outline = _OutlineColor;
                    outline.a *= glow;
                    
                    // Blend outline with original sprite pixel
                    color = lerp(outline, color, alpha);
                }

                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                
                #ifdef _USEUIALPHACLIP_ON
                clip (color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
