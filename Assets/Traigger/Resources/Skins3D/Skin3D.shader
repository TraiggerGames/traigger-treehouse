// Skins 3D: color + mapa de normales con una luz suave desde delante, sombra en dos tonos (estilo anime) y un
// borde de luz. No depende de las luces de la escena (el estudio es 2D y no tiene).
Shader "Traigger/Skin3D" {
    Properties {
        _MainTex ("Color", 2D) = "white" {}
        _BumpMap ("Normales", 2D) = "bump" {}
        _ClosedTex ("Ojos cerrados", 2D) = "white" {}
        _BlinkMask ("Máscara (R ojo derecho, G ojo izquierdo, B boca)", 2D) = "black" {}
        _BlinkR ("Parpadeo derecho", Range(0,1)) = 0
        _BlinkL ("Parpadeo izquierdo", Range(0,1)) = 0
        _MouthClosed ("Boca cerrada", Range(0,1)) = 0
        _Shade ("Sombra", Range(0,1)) = 0.72
        _Rim ("Borde de luz", Range(0,1)) = 0.25
    }
    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass {
            Cull Back ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _BumpMap, _ClosedTex, _BlinkMask; float4 _MainTex_ST; float _Shade, _Rim, _BlinkR, _BlinkL, _MouthClosed;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float4 tangent:TANGENT; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 n:TEXCOORD1; float3 t:TEXCOORD2; float3 b:TEXCOORD3; float3 view:TEXCOORD4; };
            v2f vert(appdata v) {
                v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=TRANSFORM_TEX(v.uv,_MainTex);
                o.n=UnityObjectToWorldNormal(v.normal); o.t=UnityObjectToWorldDir(v.tangent.xyz); o.b=cross(o.n,o.t)*v.tangent.w;
                o.view=normalize(_WorldSpaceCameraPos-mul(unity_ObjectToWorld,v.vertex).xyz); return o;
            }
            fixed4 frag(v2f i):SV_Target {
                fixed4 color=tex2D(_MainTex,i.uv);
                // Parpadeo: la textura de ojos cerrados se mezcla solo dentro de la máscara de cada ojo.
                // Lo mismo para la boca cerrada (canal B).
                float3 zones=tex2D(_BlinkMask,i.uv).rgb;
                float closed=saturate(zones.r*_BlinkR+zones.g*_BlinkL+zones.b*_MouthClosed);
                if(closed>0.001) color.rgb=lerp(color.rgb,tex2D(_ClosedTex,i.uv).rgb,closed);
                float3 tn=UnpackNormal(tex2D(_BumpMap,i.uv));
                float3 n=normalize(tn.x*normalize(i.t)+tn.y*normalize(i.b)+tn.z*normalize(i.n));
                float3 light=normalize(float3(-0.35,0.55,-0.75));          // desde delante, arriba a la izquierda
                float lambert=dot(n,light)*0.5+0.5;
                float tone=lerp(_Shade,1,smoothstep(0.42,0.52,lambert));   // dos tonos con transición suave
                float rim=pow(1-saturate(dot(n,normalize(i.view))),3)*_Rim;
                return fixed4(color.rgb*tone+rim,1);
            }
            ENDCG
        }
    }
}
