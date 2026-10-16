// The game-drawn outline (2026-10-14): draws a sprite's silhouette grown by a fixed number of SCREEN pixels, in one
// flat colour. SpriteOutline puts this copy behind every piece of an object, so the pieces cover each other's inner
// edges and only the object's outer silhouette shows a line, with the same thickness on every asset at any size.
// Thickness and colour are global (_RavenholmOutlinePx, _RavenholmOutlineColor; from OutlineSettings).
// The sprite needs transparent margin and a full-rect mesh: the line is drawn inside the sprite's rectangle.
Shader "Ravenholm/SpriteOutlineSilhouette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        // Always: a normal outline. NotEqual (6): skip pixels marked by SpriteUnlitStencilMark (trees over trees).
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comparison", Float) = 8
        // A target's highlight (the tree about to be chopped, 2026-10-16): a wider line in its own colour (alpha 1 = use it).
        _HighlightPx ("Extra pixels", Float) = 0
        _HighlightColor ("Highlight colour", Color) = (1, 1, 1, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        Stencil
        {
            Ref 64
            ReadMask 64
            Comp [_StencilComp]
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
            float4 _RavenholmOutlineColor;
            float _RavenholmOutlinePx;
            float _HighlightPx;
            float4 _HighlightColor;

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS);
                o.uv = i.uv;
                o.color = i.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // Texels per screen pixel here, so the radius is the same number of screen pixels on every sprite.
                float2 texels = i.uv * _MainTex_TexelSize.zw;
                float perPixel = max(length(ddx(texels)), length(ddy(texels)));
                float2 r = (_RavenholmOutlinePx + _HighlightPx) * perPixel * _MainTex_TexelSize.xy;
                float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                [unroll] for (int k = 0; k < 16; k++)
                {
                    float ang = k * 0.39269908;   // 2 pi / 16
                    float2 d = float2(cos(ang), sin(ang)) * r;
                    a = max(a, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + d).a);
                    a = max(a, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + d * 0.5).a);
                }
                float m = smoothstep(0.25, 0.6, a);
                half3 rgb = lerp(_RavenholmOutlineColor.rgb, _HighlightColor.rgb, _HighlightColor.a);
                half alpha = lerp(_RavenholmOutlineColor.a, 1.0, _HighlightColor.a);
                return half4(rgb, alpha * m * i.color.a);
            }
            ENDHLSL
        }
    }
}
