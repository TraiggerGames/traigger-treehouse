Shader "Traigger/UnlitAlpha" {
 Properties { _MainTex ("Layer",2D)="white"{} _Color("Tint",Color)=(1,1,1,1) _Volume("Soft volume",Range(0,0.6))=0 _LayerRect("UV region",Vector)=(0,0,1,1) _VertexAlpha("Use vertex alpha",Float)=0
 _SleeveSplit("Sleeve split (-1 front, 1 lining)",Float)=0
 _SleeveTube("Continuous rear tube split",Float)=0
 _TongueOut("Replace painted tongue with mouth cavity",Range(0,1))=0
 _SkinOpaque("Opaque exposed upper arm",Float)=0
 _SkinColorJoin("Opaque skin color transition",Float)=0
 _SkinUnderCenter("Forearm center U",Float)=0
 _HemSplit("Hem split (-1 front, 1 rear)",Float)=0
 _HairSharpness("Hair edge clarity",Range(0,.8))=0
 _HairGradient("Kemono cyan gradient",Range(0,1))=0
 _HairUV("Hair atlas rectangle",Vector)=(0,0,1,1)
 _HeadShadeTex("Head cel shading reference",2D)="white"{}
 _HeadShadeStrength("Head cel shading",Range(0,1))=0
 _JoinMode("Soft internal join (sleeve 1/-1, forearm 2)",Float)=0
 _JoinStrength("Internal join strength",Range(0,1))=0
 _JoinRect("Join source UV region",Vector)=(0,0,1,1)
 _SleeveOpening("Opening center UV / pixel axis",Vector)=(0,0,1,0)
 _SleeveRadii("Opening radii in source pixels",Vector)=(115,26,0,0)
 _SleeveDarkLimit("Lining color upper bounds",Vector)=(0.14,0.26,0.42,0) }
 SubShader { Tags {"Queue"="Transparent" "RenderType"="Transparent"} Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
 Pass { CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "UnityCG.cginc"
 struct appdata { float4 vertex:POSITION;float2 uv:TEXCOORD0;float2 uv2:TEXCOORD1;float4 color:COLOR; };
 struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;float alpha:TEXCOORD1;float2 underUV:TEXCOORD2;};
 sampler2D _MainTex;float4 _Color;float _Volume;float4 _LayerRect;float _VertexAlpha;float _SleeveSplit;float4 _SleeveOpening,_SleeveRadii,_SleeveDarkLimit;float4 _MainTex_TexelSize;
 float _JoinMode,_JoinStrength;float4 _JoinRect;
 float _HemSplit;
 float _SleeveTube,_SkinOpaque,_SkinColorJoin,_SkinUnderCenter;
 sampler2D _HeadShadeTex;float _HeadShadeStrength;float _TongueOut;float _HairSharpness,_HairGradient;float4 _HairUV;
 // Las piezas del cuerpo desvanecen su extremo bajo la articulación; las mallas sin color no se ven afectadas.
 v2f vert(appdata v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.underUV=v.uv2;o.alpha=lerp(1,v.color.a,_VertexAlpha);return o;}
 fixed4 frag(v2f i):SV_Target{fixed4 color=tex2D(_MainTex,i.uv)*_Color;
 // Nitidez local moderada; el exterior completamente transparente permanece vacío.
 if(_HairSharpness>0){
  float2 dx=float2(_MainTex_TexelSize.x,0),dy=float2(0,_MainTex_TexelSize.y);
  float4 a=tex2D(_MainTex,i.uv-dx),b=tex2D(_MainTex,i.uv+dx),c=tex2D(_MainTex,i.uv-dy),d=tex2D(_MainTex,i.uv+dy);
  float3 mean=(a.rgb+b.rgb+c.rgb+d.rgb)*.25;
  float3 lo=min(color.rgb,min(min(a.rgb,b.rgb),min(c.rgb,d.rgb))),hi=max(color.rgb,max(max(a.rgb,b.rgb),max(c.rgb,d.rgb)));
  float safe=step(.95,min(min(a.a,b.a),min(c.a,d.a)))*step(.95,color.a);
  color.rgb=lerp(color.rgb,clamp(color.rgb+(color.rgb-mean)*_HairSharpness,lo,hi),safe);
  float meanAlpha=(a.a+b.a+c.a+d.a)*.25;
  color.a=saturate(color.a+(color.a-meanAlpha)*_HairSharpness*.4);
 }
 float joinDown=1-(i.uv.y-_JoinRect.y)/max(_JoinRect.w,.001);
 float vertexAlpha=i.alpha;
 // La piel que emerge de la manga es opaca. El extremo proximal oculto conserva su recorte.
 if(_SkinOpaque>.5)vertexAlpha=step(.40,joinDown)*step(.001,vertexAlpha);
 // Mantener piel opaca sobre el casquete del antebrazo; fundir después, sobre piel ya continua.
 if(_JoinMode>2.5&&joinDown>.5)vertexAlpha=lerp(vertexAlpha,1-smoothstep(.80,.94,joinDown),_JoinStrength);
 color.a*=vertexAlpha;
 // Mezclar pigmento sobre piel opaca: nunca transparencia sobre el forro o el fondo.
 if(_SkinColorJoin>.5){
   float4 under=tex2D(_MainTex,i.underUV);
   bool unsafeInk=dot(under.rgb,float3(.299,.587,.114))<.35||under.a<.95;
   if(_SkinColorJoin>1.5){
     float3 pigment=under.rgb;
     #ifndef UNITY_COLORSPACE_GAMMA
     pigment=LinearToGammaSpace(pigment);
     #endif
     // El puño abierto deja ver más piel: no proyectar la tinta del casquete del antebrazo en el brazo.
     unsafeInk=dot(pigment,float3(.299,.587,.114))<.63||under.a<.995;
   }
   if(unsafeInk)under=tex2D(_MainTex,float2(_SkinUnderCenter,i.underUV.y));
   float preserveInk=smoothstep(.12,.28,dot(color.rgb,float3(.299,.587,.114)));
   color.rgb=lerp(color.rgb,under.rgb,smoothstep(.60,.79,joinDown)*step(.95,under.a)*preserveInk);
 }
 // Vueltas traseras del bajo: partición geométrica complementaria del mismo atlas, sin clave de color.
 if(abs(_HemSplit)>.5){
   float2 pixel=float2(i.uv.x,1-i.uv.y)*_MainTex_TexelSize.zw;
   float left=step(pixel.x,606)*step(495+.32*(pixel.x-535),pixel.y);
   float right=step(921,pixel.x)*step(494+.23*(1000-pixel.x),pixel.y);
   float rear=saturate(left+right);
   color.a*=(_HemSplit>0)?rear:1-rear;
 }
 // Dos capas complementarias de los mismos píxeles, con la misma malla y pesos.
 // La tela y su borde quedan delante; el forro oscuro se dibuja detrás del brazo.
 if(abs(_SleeveSplit)>.5){
   float2 delta=(i.uv-_SleeveOpening.xy)*_MainTex_TexelSize.zw;
   float2 axis=normalize(_SleeveOpening.zw),normal=float2(-axis.y,axis.x);
   float2 q=float2(dot(delta,axis),dot(delta,normal))/max(_SleeveRadii.xy,float2(1,1));
   float region=step(dot(q,q),1);
   float lining=region;
   // El borde inferior del óvalo pertenece al reverso: no puede cortar la piel por delante.
   // Sólo la pared superior de tela permanece delante del brazo.
   if(_SleeveTube>.5){
     float along=dot(delta,axis),across=dot(delta,normal);
     float arc=_SleeveRadii.y*sqrt(saturate(1-pow(along/_SleeveRadii.x,2)));
     lining=step(across,arc);
   }
   color.a*=(_SleeveSplit>0)?lining:(1-lining);
 }
 float2 local=saturate((i.uv-_LayerRect.xy)/max(_LayerRect.zw,float2(.001,.001)));
 // Sólo el material de boca activa esta sustitución. Conserva alfa, dientes y contorno.
 // El rosa de la lengua pintada se convierte en cavidad al mostrar la lengua independiente.
 if(_TongueOut>0){
   float3 pigment=color.rgb;
   #ifndef UNITY_COLORSPACE_GAMMA
   pigment=LinearToGammaSpace(pigment);
   #endif
   float pink=smoothstep(.68,.80,pigment.r)*smoothstep(.30,.45,pigment.g)*smoothstep(.27,.40,pigment.b)*smoothstep(.10,.18,pigment.r-pigment.g);
   pink*=1-smoothstep(.76,.83,local.y);
   // Incluye la línea interior que delimitaba la lengua, sin tocar dientes ni borde externo.
   float2 tongueRegion=(local-float2(.5,.36))/float2(.405,.385);
   float insideTongue=(1-smoothstep(.97,1.07,dot(tongueRegion,tongueRegion)))*smoothstep(.08,.13,local.y);
   pink=max(pink,insideTongue);
   float3 cavity=float3(.592157,.239216,.227451);
   #ifndef UNITY_COLORSPACE_GAMMA
   cavity=GammaToLinearSpace(cavity);
   #endif
   color.rgb=lerp(color.rgb,cavity,saturate(_TongueOut)*pink);
 }
 // Sólo bordes internos superpuestos: se conserva el contorno exterior y el borde del puño.
 float2 joinUV=(i.uv-_JoinRect.xy)/max(_JoinRect.zw,float2(.001,.001));
 float down=1-joinUV.y;
 if(abs(_JoinMode)>.5&&abs(_JoinMode)<1.5){
   float inward=(_JoinMode>0)?joinUV.x:1-joinUV.x;
   float boundary=.92-.12*smoothstep(.15,.9,down);
   float window=smoothstep(.12,.26,down)*(1-smoothstep(.76,.9,down));
   color.a*=1-_JoinStrength*window*smoothstep(boundary-.13,boundary+.015,inward);
 }
 if(_JoinMode>1.5&&_JoinMode<2.5)color.a*=lerp(1,smoothstep(.025,.135,down),_JoinStrength);
 // Volumen ilustrado suave por pieza, independiente del color y con alfa intacto.
 if(_HeadShadeStrength>0){
   float4 painted=tex2D(_HeadShadeTex,i.uv);
   float originalLight=dot(color.rgb,float3(.299,.587,.114));
   float paintedLight=dot(painted.rgb,float3(.299,.587,.114));
   // Se transfiere luz y sombra, manteniendo el tono original del pelo y la tinta.
   float lightRatio=clamp(paintedLight/max(originalLight,.025),.52,1.8);
   float3 tintRatio=clamp(painted.rgb/max(color.rgb,float3(.025,.025,.025)),.52,1.8);
   float ink=smoothstep(.025,.07,originalLight);
   color.rgb*=lerp(float3(1,1,1),lerp(float3(lightRatio,lightRatio,lightRatio),tintRatio,.2),_HeadShadeStrength*painted.a*ink);
 }
 // Degradado sólo en pigmento cian: se mantienen negros, tinta, brillos y sombreado ilustrado.
 if(_HairGradient>0){
  float3 pigment=color.rgb;
  #ifndef UNITY_COLORSPACE_GAMMA
  pigment=LinearToGammaSpace(pigment);
  #endif
  float cyan=smoothstep(.10,.22,pigment.b-pigment.r)*smoothstep(.08,.18,pigment.g-pigment.r);
  float y=saturate((i.uv.y-_HairUV.y)/max(.001,_HairUV.w));
  float tips=1-smoothstep(.12,.85,y);
  float3 tint=lerp(float3(.72,.86,1.03),float3(1.10,1.12,1.055),tips);
  pigment=saturate(pigment*lerp(float3(1,1,1),tint,cyan*_HairGradient));
  #ifndef UNITY_COLORSPACE_GAMMA
  pigment=GammaToLinearSpace(pigment);
  #endif
  color.rgb=pigment;
 }
 float edge=pow(abs(local.x-.5)*2,1.6);
 float shade=.62*edge+.24*(1-local.y)+.20*local.x;
 color.rgb*=1-_Volume*shade;
 color.rgb+=_Volume*.07*(1-edge)*local.y*color.rgb;
 return color;}
 ENDCG }
 }
}
