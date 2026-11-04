Shader "PaperSeizerStone/UI/OpeningEyelids"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _EyeOpening ("Eye Opening", Range(0,1)) = 1
        _UpperShare ("Upper Lid Share", Range(0.5,0.8)) = 0.65
        _Curvature ("Lid Curvature", Range(0,0.35)) = 0.2
        _EdgePixels ("Soft Edge at 1080p", Float) = 24
        _LidColor ("Lid Color", Color) = (0.008,0.006,0.005,1)
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            float _EyeOpening, _UpperShare, _Curvature, _EdgePixels;
            float4 _LidColor;
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float opening = saturate(_EyeOpening);
                if (opening <= 0.0001) return float4(_LidColor.rgb, input.color.a);
                if (opening >= 0.9999) return 0;

                float x = input.uv.x * 2 - 1;
                float center = 1 - _UpperShare;
                // Curved corners tighten the slit, then move completely beyond the viewport.
                float arch = _Curvature * x * x * sin(opening * UNITY_PI);
                float upper = center + opening * (_UpperShare + 0.1) - arch;
                float lower = center - opening * (center + 0.1) + arch * 0.7;
                float feather = max(0.0001, _EdgePixels / 1080.0);
                float visible = smoothstep(lower - feather, lower + feather, input.uv.y)
                    * (1 - smoothstep(upper - feather, upper + feather, input.uv.y));
                return float4(_LidColor.rgb * input.color.rgb, (1 - visible) * input.color.a);
            }
            ENDCG
        }
    }
}
