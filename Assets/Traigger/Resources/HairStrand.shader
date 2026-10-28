Shader "Traigger/HairStrand" {
 Properties {
  _MainTex("Individual strand alpha",2D)="white"{}
  _HairSource("Original shared hair pigment",2D)="white"{}
  _AtlasRect("Strand registration in source atlas",Vector)=(0,0,1,1)
  _Color("Tint",Color)=(1,1,1,1)
  _StrandOverlay("Preserve shared exterior coverage",Float)=0
 }
 SubShader {
  Tags{"Queue"="Transparent" "RenderType"="Transparent"}
  Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
  Pass { CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
   struct v2f {float4 position:SV_POSITION;float2 maskUV:TEXCOORD0;float2 sourceUV:TEXCOORD1;};
   sampler2D _MainTex,_HairSource;float4 _AtlasRect,_Color;float _StrandOverlay;
   v2f vert(appdata v){v2f o;o.position=UnityObjectToClipPos(v.vertex);o.maskUV=v.uv;o.sourceUV=v.uv*_AtlasRect.zw+_AtlasRect.xy;return o;}
   float4 frag(v2f i):SV_Target{
    // Todas las piezas leen el MISMO pigmento en la misma coordenada del dibujo original.
    // El RGB dilatado de cada recorte no puede dibujar líneas o triángulos en sus bordes.
    float4 pigment=tex2D(_HairSource,i.sourceUV);
    // La base contiene el alfa original. Los recortes sólo componen en zonas opacas:
    // no acumulan alfa sobre el borde ni dejan la huella triangular de una máscara interior.
    float originalAlpha=pigment.a;
    pigment.a=tex2D(_MainTex,i.maskUV).a;
    if(_StrandOverlay>.5)pigment.a*=step(.9999,originalAlpha);
    return pigment*_Color;
   }
   ENDCG
  }
 }
}
