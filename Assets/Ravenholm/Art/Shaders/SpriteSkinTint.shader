// Modular characters (Astra's standard rig v1, 2026-10-18): an unlit sprite whose skin-masked pixels take this
// character's skin. _SkinMask is data on the sprite's own canvas (R = base skin, G = skin shadow, B = 0; point filtered,
// linear); masked pixels become _SkinBase or _SkinShadow, everything else (hair, clothes, armour) stays as painted.
Shader "Ravenholm/SpriteSkinTint"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        [PerRendererData] _SkinMask ("Skin mask", 2D) = "black" {}
        [PerRendererData] _SkinBase ("Skin", Color) = (0.855, 0.529, 0.357, 1)
        [PerRendererData] _SkinShadow ("Skin shadow", Color) = (0.792, 0.4, 0.259, 1)
        [PerRendererData] _CapA ("Shoulder cap A (x, y px from the bottom-left, radius px, on)", Vector) = (0, 0, 0, 0)
        [PerRendererData] _CapB ("Shoulder cap B", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_SkinMask);
            SAMPLER(sampler_point_clamp);
            half4 _SkinBase;
            half4 _SkinShadow;
            float4 _MainTex_TexelSize;
            // The shoulder caps (2026-10-19): a copy of the torso drawn over the arms, cut to circles round the shoulders,
            // so each arm comes out from under its sleeve. Off (w = 0): the whole sprite.
            float4 _CapA, _CapB;

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS);
                o.uv = i.uv;
                o.color = i.color * unity_SpriteColor;   // the renderer's colour and fades (Unity 6)
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                if (_CapA.w > 0.5 || _CapB.w > 0.5)
                {
                    float2 px = i.uv * _MainTex_TexelSize.zw;
                    float2 da = px - _CapA.xy, db = px - _CapB.xy;
                    bool inA = _CapA.w > 0.5 && dot(da, da) <= _CapA.z * _CapA.z;
                    bool inB = _CapB.w > 0.5 && dot(db, db) <= _CapB.z * _CapB.z;
                    if (!inA && !inB) discard;
                }
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half4 m = SAMPLE_TEXTURE2D(_SkinMask, sampler_point_clamp, i.uv);   // data: always point sampled
                half cover = m.r + m.g;
                if (m.a > 0.5h && cover > 0.5h) t.rgb = m.r >= m.g ? _SkinBase.rgb : _SkinShadow.rgb;
                return t * i.color;
            }
            ENDHLSL
        }
    }
}
