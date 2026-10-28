Shader "Traigger/LayeredEye" {
 Properties { _MainTex("Original eye atlas",2D)="white"{} _Color("Tint",Color)=(1,1,1,1) }
 SubShader {
  Tags {"Queue"="Transparent" "RenderType"="Transparent"} Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
  CGINCLUDE
   #include "UnityCG.cginc"
   sampler2D _MainTex;float4 _Color,_EyeUV,_Iris,_Pupil,_Gaze;float _PupilScale,_Blink;
   
   struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
   struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;};
   v2f vert(appdata v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
   float2 atlas(float2 local){return _EyeUV.xy+local*_EyeUV.zw;}
   float ellipse(float2 samplePosition,float4 region){float q=length((samplePosition-region.xy)/region.zw);return 1-smoothstep(.985,1.02,q);}
   fixed4 eyeFrag(v2f i,float layer) {
    float4 original=tex2D(_MainTex,i.uv);float3 pigment=original.rgb;
    #ifndef UNITY_COLORSPACE_GAMMA
    pigment=LinearToGammaSpace(pigment);
    #endif
    // La tinta marrón pertenece al párpado, no al iris ni a la pupila.
    float ink=smoothstep(.018,.055,pigment.r-pigment.g)*(1-smoothstep(.38,.52,pigment.r));
    float2 local=(i.uv-_EyeUV.xy)/_EyeUV.zw;
    if(layer<.5){
     // Fondo opaco bajo el iris: reconstruye el blanco que no existe detrás del ojo dibujado.
     float shade=smoothstep(.42,.84,local.y);
     float3 white=lerp(float3(.98,.975,.945),float3(.78,.77,.745),shade*.85);
     #ifndef UNITY_COLORSPACE_GAMMA
     white=GammaToLinearSpace(white);
     #endif
     return float4(lerp(white,original.rgb,ink),original.a)*_Color;
    }
    // Las UV conservan el dibujo al girar o trasladar el rig. El cierre recorta el iris.
    float2 paint=local;
    paint.y=(paint.y-.5)*(1-.94*_Blink)+.5;
    paint-=_Gaze.xy;
    float aperture=original.a*(1-ink)*(1-smoothstep(.75,.98,_Blink));
    float irisMask=ellipse(paint,_Iris);
    if(layer<1.5){
     float4 color=tex2D(_MainTex,atlas(paint));
     float pupil=ellipse(paint,_Pupil);
     float2 side=float2(_Pupil.z*2.7,0);
     float3 under=(tex2D(_MainTex,atlas(paint-side)).rgb+tex2D(_MainTex,atlas(paint+side)).rgb)*.5;
     color.rgb=lerp(color.rgb,under,pupil);
     return float4(color.rgb,aperture*irisMask)*_Color;
    }
    float2 pupilPaint=_Pupil.xy+(paint-_Pupil.xy)/max(.5,_PupilScale);
    float4 pupilColor=tex2D(_MainTex,atlas(pupilPaint));
    float3 rgb=pupilColor.rgb;
    #ifndef UNITY_COLORSPACE_GAMMA
    rgb=LinearToGammaSpace(rgb);
    #endif
    float dark=1-smoothstep(.13,.25,max(rgb.r,max(rgb.g,rgb.b)));
    return float4(pupilColor.rgb,aperture*irisMask*ellipse(pupilPaint,_Pupil)*dark)*_Color;
   }
   fixed4 fragSclera(v2f i):SV_Target{return eyeFrag(i,0);}
   fixed4 fragIris(v2f i):SV_Target{return eyeFrag(i,1);}
   fixed4 fragPupil(v2f i):SV_Target{return eyeFrag(i,2);}
   ENDCG
  // Tres pasadas en el mismo renderer: conserva el orden original respecto a pelo y cara.
  Pass { CGPROGRAM
   #pragma vertex vert
   #pragma fragment fragSclera
   ENDCG }
  Pass { CGPROGRAM
   #pragma vertex vert
   #pragma fragment fragIris
   ENDCG }
  Pass { CGPROGRAM
   #pragma vertex vert
   #pragma fragment fragPupil
   ENDCG }
 }
}
