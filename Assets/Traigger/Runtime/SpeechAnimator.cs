using UnityEngine;
namespace Traigger {
    // Animación procedural a partir de la voz, para cuando no hay cámara de cara (Vision Pro, Mac sin iPhone):
    // · forma de la boca por vocal (ancha i/e, redonda o/u) a partir de agudos y graves;
    // · cabeza que acompaña al habla: asiente en las sílabas fuertes e inclina al final de las frases;
    // · escucha activa cuando hablan los demás (tú en silencio): pequeños asentimientos y orejas atentas;
    // · mirada viva: sacadas cortas con pausas, casi siempre hacia el centro (donde están las Personas).
    // Sólo produce valores; StudioController los suma a sus controles.
    public class SpeechAnimator {
        public float roll,yaw,pitch,mouthShape,gazeX,gazeY,earAttention,armSway;
        public bool speaking {get;private set;}
        public float intensity=1;
        float previousOpen,nodVelocity,nod,tiltTarget,tilt,silence,speechTime,nextSaccade,listenTimer;
        Vector2 gazeTarget;
        public void Step(float open,float zcr,float low,float dt,float t){
            // Hablando: con histéresis para no parpadear entre estados.
            if(open>.18f){speaking=true;silence=0;speechTime+=dt;}
            else{silence+=dt;if(silence>.35f){if(speaking)OnPhraseEnd();speaking=false;speechTime=0;}}
            // Sílaba fuerte: subida rápida de la apertura → impulso de asentimiento (muelle amortiguado).
            float rise=(open-previousOpen)/Mathf.Max(dt,.001f);previousOpen=open;
            if(speaking&&rise>4f&&open>.35f)nodVelocity-=Mathf.Min(rise,12)*.9f;
            nodVelocity+=(-60*nod-9*nodVelocity)*dt;nod+=nodVelocity*dt;nod=Mathf.Clamp(nod,-6,4);
            // Inclinación suave que cambia de lado cada frase y un ligero giro mientras se habla.
            tilt=Mathf.Lerp(tilt,tiltTarget,1-Mathf.Exp(-2.5f*dt));
            float talkYaw=speaking?Mathf.Sin(t*1.3f)*2.5f+(Mathf.PerlinNoise(t*.7f,3)-.5f)*4:0;
            // Escucha activa: sin hablar, asentimientos lentos y orejas un poco más atentas.
            float listen=0;
            if(!speaking&&silence>1.2f){listenTimer-=dt;if(listenTimer<=0){nodVelocity-=Random.Range(4f,8f);listenTimer=Random.Range(2.2f,4.5f);}listen=1;}
            earAttention=Mathf.Lerp(earAttention,speaking?.25f:listen*.45f,1-Mathf.Exp(-2*dt));
            // Vocal: agudos (zcr alto, pocos graves) → ancha; graves dominantes → redonda.
            float bright=Mathf.InverseLerp(.05f,.18f,zcr)-Mathf.InverseLerp(.35f,.75f,low);
            float target=speaking?Mathf.Clamp(bright*.9f,-1,1)*Mathf.Clamp01(open*1.5f):0;
            mouthShape=Mathf.Lerp(mouthShape,target,1-Mathf.Exp(-14*dt));
            // Mirada: sacadas cortas, sobre todo al centro; al hablar mira algo más a los lados.
            if(t>=nextSaccade){
                float spread=speaking?.35f:.2f;
                gazeTarget=Random.value<.65f?new Vector2(Random.Range(-.08f,.08f),Random.Range(-.05f,.05f)):new Vector2(Random.Range(-spread,spread),Random.Range(-.15f,.12f));
                nextSaccade=t+Random.Range(.7f,2.8f);
            }
            gazeX=Mathf.Lerp(gazeX,gazeTarget.x,1-Mathf.Exp(-30*dt));gazeY=Mathf.Lerp(gazeY,gazeTarget.y,1-Mathf.Exp(-30*dt));
            armSway=speaking?Mathf.Sin(speechTime*2.1f)*open*3:0;
            pitch=(nod+(speaking?open*1.5f:0))*intensity;roll=tilt*intensity;yaw=talkYaw*intensity;
        }
        void OnPhraseEnd(){tiltTarget=Random.Range(3f,6f)*(Random.value<.5f?-1:1);nodVelocity-=3;}
    }
}
