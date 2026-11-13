Shader "Rhythmancy/PaperGround"
{
    Properties { [PerRendererData] _MainTex ("Drawing", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Cull Off ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.texcoord; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                // At an edge use the middle of the same drawing. Adjacent tiles
                // meet on identical samples without mirroring the pencil strokes.
                float2 uv=saturate(i.uv);
                float2 shifted=frac(uv+.5);
                float2 weight=smoothstep(0,.18,uv)*smoothstep(0,.18,1-uv);
                fixed4 a=lerp(tex2D(_MainTex,shifted),tex2D(_MainTex,float2(uv.x,shifted.y)),weight.x);
                fixed4 b=lerp(tex2D(_MainTex,float2(shifted.x,uv.y)),tex2D(_MainTex,uv),weight.x);
                return lerp(a,b,weight.y);
            }
            ENDCG
        }
    }
}
