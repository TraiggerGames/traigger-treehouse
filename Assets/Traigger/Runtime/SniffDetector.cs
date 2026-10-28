using UnityEngine;
namespace Traigger {
    // Reconoce el gesto de olfatear para que Traigger lo haga como un gato (nariz a tironcitos, hocico arriba, orejas
    // adelante, ojos quietos) en vez de copiar la mueca de «arrugar la nariz», que en una cara humana arrastra los ojos.
    // Dos señales, cualquiera basta:
    //  · Sonido: ráfagas cortas (40–300 ms) de soplido sin tono, 2 o más en 1,5 s, sin voz alrededor. Funciona con el
    //    micrófono de cualquier aparato (también en Vision Pro, que no ve la cara). El clasificador de Apple no tiene
    //    «olfateo», así que se detecta aquí con lo que ya mide la voz: nivel, agudos (cruces por cero) y tono.
    //  · Cara: 2 o más tironcitos rápidos de la nariz en 1,2 s con la boca cerrada (la mueca es lenta y sostenida).
    public class SniffDetector {
        public float amount {get;private set;}   // 0–1: cuánto está olfateando ahora (con entrada y salida suaves)
        public string lastSource="";
        float activeUntil=-10;
        // Sonido
        bool inBurst;float burstStart,lastVoiced=-10,firstBurst=-10;int bursts;
        // Cara
        float baseline,pulseStart=-10,firstPulse=-10;bool rising;int pulses;

        public void FeedAudio(float level,float zcr,float pitch,float t){
            if(pitch>0&&level>.05f)lastVoiced=t;
            bool noisy=level>.08f&&pitch<=0&&zcr>.12f;
            if(noisy&&!inBurst){inBurst=true;burstStart=t;}
            else if(!noisy&&inBurst){
                inBurst=false;
                float length=t-burstStart;
                // Ni dentro ni cerca de la voz: así las «s» al hablar no cuentan.
                bool clean=length>.04f&&length<.3f&&burstStart-lastVoiced>.3f;
                if(clean){
                    if(t-firstBurst>1.5f){firstBurst=burstStart;bursts=0;}
                    if(++bursts>=2)Trigger(t,"sonido");
                }
            }
            if(inBurst&&t-burstStart>.4f)inBurst=false; // soplido largo: suspiro o respiración, no olfateo
        }
        public void FeedFace(float nose,float jaw,float t){
            baseline=Mathf.Lerp(baseline,nose,.02f);
            if(jaw>.12f){rising=false;return;}
            float delta=nose-baseline;
            if(!rising&&delta>.08f){rising=true;pulseStart=t;}
            else if(rising&&delta<.04f){
                rising=false;
                if(t-pulseStart<.35f){
                    if(t-firstPulse>1.2f){firstPulse=pulseStart;pulses=0;}
                    if(++pulses>=2)Trigger(t,"cara");
                }
            }
            if(rising&&t-pulseStart>.5f)rising=false; // arruga sostenida: es una mueca
        }
        void Trigger(float t,string source){activeUntil=t+1.1f;lastSource=source;}
        public void Update(float t,float dt){
            amount=Mathf.MoveTowards(amount,t<activeUntil?1:0,dt*(t<activeUntil?6:2.5f));
        }
    }
}
