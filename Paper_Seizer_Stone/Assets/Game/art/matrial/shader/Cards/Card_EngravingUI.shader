Shader "PaperSeizerStone/Cards/EngravingUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Card", 2D) = "white" {}
        _TagData ("Type / strength / seed / scale", Vector) = (0,0,0,1)
        _TagColor ("Ink", Color) = (1,1,1,1)
        _TagArtworkRect ("Artwork region", Vector) = (0.07,0.19,0.93,0.78)
        _TagMotion ("Motion speed / strength", Vector) = (0,0,0,0)
        _TagView ("Card-local view direction", Vector) = (0,1,0,0)
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
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "Card_Engraving.hlsl"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; float4 world:TEXCOORD1; };
            sampler2D _MainTex;
            float4 _TagData, _TagColor, _TagArtworkRect, _TagMotion, _TagView, _ClipRect;
            v2f vert(appdata v)
            {
                v2f o; o.world=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float4 c;
                CardEngraving_float(tex2D(_MainTex,i.uv),i.uv,_TagData,_TagColor,_TagArtworkRect,_TagView.xyz,_TagMotion,c);
                c *= i.color;
                #ifdef UNITY_UI_CLIP_RECT
                    c.a *= UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                    clip(c.a-0.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
