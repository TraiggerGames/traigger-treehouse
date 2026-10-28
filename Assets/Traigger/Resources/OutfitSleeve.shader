Shader "Traigger/OutfitSleeve" {
 Properties { _MainTex("Separated artwork",2D)="white"{} _SourceTex("Registered original pigment",2D)="white"{} _Part("Cloth / cuff / lining",Float)=0 _Mirror("Mirrored sleeve",Float)=0 }
 SubShader { Tags {"Queue"="Transparent" "RenderType"="Transparent"} Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
 Pass {CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
 struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;};
 sampler2D _SourceTex;float4 _SourceTex_TexelSize;float _Part,_Mirror;
 v2f vert(appdata v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
 fixed4 frag(v2f i):SV_Target {
  // Pigmento y alfa del dibujo original. La división se hace DESPUÉS del filtrado:
  // dos texturas con alfa filtrada por separado dejarían una raya translúcida en la costura.
  fixed4 color=tex2D(_SourceTex,i.uv);
  float2 p=float2(lerp(i.uv.x,1-i.uv.x,_Mirror),1-i.uv.y)*_SourceTex_TexelSize.zw;
  float2 d=p-float2(151,326);
  float along=dot(d,float2(.841,.541)),across=dot(d,float2(-.541,.841));
  float arc=25*sqrt(saturate(1-pow(along/175,2)));
  bool rear=across > -arc+18;
  bool cuff=across > -59;
  bool covered=_Part>1.5?rear:(_Part>.5?!rear&&cuff:!rear&&!cuff);
  clip(covered?1:-1);
  return color;
 }
 ENDCG}
 }
}
