Shader "Traigger/TongueAlpha" {
 Properties { [PerRendererData] _MainTex("Sprite",2D)="white"{} _Color("Color",Color)=(1,1,1,1) }
 SubShader {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
  Cull Off Lighting Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   sampler2D _MainTex;fixed4 _Color;
   struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
   struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
   v2f vert(appdata v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color*_Color;return o;}
   fixed4 frag(v2f i):SV_Target {fixed4 c=tex2D(_MainTex,i.uv)*i.color;
    // Coordenada propia del recorte v2: punta 0, raíz 1.
    float along=saturate((i.uv.y-.267145)/.45933);
    float3 cavity=float3(.592157,.239216,.227451);
    #ifndef UNITY_COLORSPACE_GAMMA
    cavity=GammaToLinearSpace(cavity);
    #endif
    // La raíz entra en la cavidad: compartir su sombra elimina la línea de "pieza pegada".
    // La punta y su sombreado kemono se conservan; sólo se suaviza el tramo proximal.
    c.rgb=lerp(c.rgb,cavity,smoothstep(.43,.95,along)*.96);
    c.a*=1-smoothstep(.68,1,along);return c;}
   ENDCG
  }
 }
}
