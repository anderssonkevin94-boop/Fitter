// Overlap-free outlines (2026-10-15, Kevin): an unlit sprite that also marks its opaque pixels in the stencil buffer
// (bit 64). Trees use it; their outline copies (SpriteOutlineSilhouette with _StencilComp = NotEqual) then skip pixels a
// tree already covers, so where trees overlap no dark line crosses the tree behind. Draws like Sprite-Unlit-Default.
Shader "Ravenholm/SpriteUnlitStencilMark"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
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
            WriteMask 64
            Comp Always
            Pass Replace
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS);
                o.uv = i.uv;
                // The renderer's colour (a tree's tint, its fade) comes as unity_SpriteColor in Unity 6, not in the vertex
                // colour alone (2026-10-17: the trees' fade and tints never showed).
                o.color = i.color * unity_SpriteColor;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(t.a - 0.5);   // only the art's opaque pixels mark the stencil (its alpha is 0 or 1); fading still shows
                return t * i.color;
            }
            ENDHLSL
        }
    }
}
