using System;
using UnityEngine;
namespace Traigger {
    // Perfil de tu voz: cómo hablas normalmente y, si lo calibras, cómo suenas con cada emoción. Se guarda en el
    // dispositivo y, si está activado, en iCloud para que iPhone, iPad y Vision Pro compartan el mismo.
    [Serializable] public class VoiceProfile {
        public float baseTone,baseSpread=1.5f,baseEnergy=.4f,baseZcr=.05f,baseLow=.3f,learned;
        public float[] centroids=new float[VoiceEmotion.Count*VoiceEmotion.Features];
        public int[] samples=new int[VoiceEmotion.Count];
        public long updated;
    }
    // Emoción por la entonación: compara cómo hablas ahora (últimos ~2 s) con tu forma habitual de hablar.
    // Mide cuatro rasgos: tono, melodía (cuánto sube y baja), energía y aspereza (agudos sibilantes frente a graves).
    //  · Sin calibrar: reglas generales (la alegría canta; el enfado empuja la voz, plana y áspera; la tristeza es
    //    grave, plana y floja).
    //  · Calibrado: compara con tus propias muestras de cada emoción, mucho más preciso para tu voz.
    // Es una pista: las palabras (TraiggerListen) mandan cuando las hay.
    public class VoiceEmotion {
        // Mismo orden que las expresiones de StudioController: Neutro, Contento, Triste, Enfado, Sorpresa, Curioso, Vergüenza.
        public const int Count=7,Features=4;
        public static readonly string[] Names={"Neutro","Contento","Triste","Enfado","Sorpresa","Curioso","Vergüenza"};
        public readonly float[] scores=new float[Count]; // 0..1, suavizados
        public float happy=>scores[1];public float sad=>scores[2];public float angry=>scores[3];
        public string status {get;private set;}="Esperando voz";
        public string debug {get;private set;}="";
        public VoiceProfile profile=new VoiceProfile();
        public bool dirty,saveNow;      // hay cambios del perfil por guardar (saveNow: tras calibrar)
        public int calibrating=-1;public float calibrationLeft;
        float tone,spread=1.5f,energy=.4f,zcr=.05f,low=.3f,talking,silence=99;bool started;
        readonly float[] feature=new float[Features],target=new float[Count];
        static readonly float[] Scale={1,1,1,1};
        static float Ease(float dt,float seconds)=>1-Mathf.Exp(-dt/seconds);
        public bool Calibrated(int i)=>profile.samples[i]>=40;
        public bool UsesProfile{get{if(!Calibrated(0))return false;for(int i=1;i<Count;i++)if(Calibrated(i))return true;return false;}}
        public void Calibrate(int index){calibrating=index;calibrationLeft=5;profile.samples[index]=0;}
        public void ResetProfile(){profile=new VoiceProfile();profile.updated=DateTime.UtcNow.Ticks;started=false;dirty=saveNow=true;}

        public void Step(float open,float pitchHz,float zcrNow,float lowNow,float dt){
            var p=profile;
            bool voiced=open>.12f&&pitchHz>60&&pitchHz<500;
            talking=Mathf.Lerp(talking,voiced?1:0,Ease(dt,1.5f));
            silence=voiced?0:silence+dt;
            if(voiced){
                float st=12*Mathf.Log(pitchHz/100f,2); // semitonos
                if(!started){started=true;tone=p.learned>0?p.baseTone:st;zcr=zcrNow;low=lowNow;if(p.learned<=0){p.baseTone=st;p.baseZcr=zcrNow;p.baseLow=lowNow;}}
                tone=Mathf.Lerp(tone,st,Ease(dt,1.2f));
                spread=Mathf.Lerp(spread,Mathf.Abs(st-tone),Ease(dt,1.5f));
                energy=Mathf.Lerp(energy,open,Ease(dt,1.2f));
                zcr=Mathf.Lerp(zcr,zcrNow,Ease(dt,1.2f));low=Mathf.Lerp(low,lowNow,Ease(dt,1.2f));
                // La referencia aprende rápido al principio y luego despacio; durante la calibración no se mueve.
                if(calibrating<0){
                    float learnTime=p.learned<8?2:40;p.learned+=dt;
                    p.baseTone=Mathf.Lerp(p.baseTone,st,Ease(dt,learnTime));
                    p.baseSpread=Mathf.Lerp(p.baseSpread,Mathf.Abs(st-tone),Ease(dt,learnTime));
                    p.baseEnergy=Mathf.Lerp(p.baseEnergy,open,Ease(dt,learnTime));
                    p.baseZcr=Mathf.Lerp(p.baseZcr,zcrNow,Ease(dt,learnTime));p.baseLow=Mathf.Lerp(p.baseLow,lowNow,Ease(dt,learnTime));
                }
            }
            feature[0]=(tone-p.baseTone)/2.5f;
            feature[1]=(spread-p.baseSpread)/Mathf.Max(.6f,p.baseSpread);
            feature[2]=(energy-p.baseEnergy)/.2f;
            feature[3]=(zcr-p.baseZcr)/Mathf.Max(.01f,p.baseZcr)-(low-p.baseLow)/Mathf.Max(.05f,p.baseLow);
            float arousal=feature[0]*.4f+feature[2]*.6f;
            debug=$"tono {feature[0]:+0.0;-0.0} · melodía {feature[1]:+0.0;-0.0} · energía {feature[2]:+0.0;-0.0} · aspereza {feature[3]:+0.0;-0.0}";
            Array.Clear(target,0,Count);
            if(calibrating>=0){
                // Calibración: media de los rasgos mientras hablas con esa emoción.
                calibrationLeft-=dt;
                if(voiced){int n=++p.samples[calibrating];for(int f=0;f<Features;f++){ref float c=ref p.centroids[calibrating*Features+f];c+=(feature[f]-c)/Mathf.Min(n,200);}}
                status=$"Calibrando «{Names[calibrating]}»: habla así {Mathf.CeilToInt(Mathf.Max(0,calibrationLeft))} s…";
                if(calibrationLeft<=0){status=Calibrated(calibrating)?$"«{Names[calibrating]}» guardado":$"«{Names[calibrating]}»: no te he oído bien, repite";calibrating=-1;p.updated=DateTime.UtcNow.Ticks;dirty=saveNow=true;}
            }
            else if(p.learned<6){status=started?"Aprendiendo tu voz…":"Esperando voz";}
            else if(silence<2.5f&&talking>.25f){
                if(UsesProfile){
                    // Tu voz calibrada: parecido a cada muestra (gaussiana) y reparto entre las calibradas.
                    float total=0;
                    for(int i=0;i<Count;i++){
                        if(!Calibrated(i))continue;float d=0;
                        for(int f=0;f<Features;f++){float x=(feature[f]-p.centroids[i*Features+f])/Scale[f];d+=x*x;}
                        target[i]=Mathf.Exp(-d*.8f);total+=target[i];
                    }
                    for(int i=0;i<Count;i++)target[i]=total>1e-4f?Mathf.Clamp01((target[i]/total-.35f)/.5f):0;
                    target[0]=0;
                } else {
                    target[1]=Mathf.Clamp01((arousal+feature[1]*.6f-feature[3]*.4f-.35f)/.7f);
                    target[3]=Mathf.Clamp01((arousal+feature[3]*.6f-feature[1]*.5f-.45f)/.7f);
                    target[2]=Mathf.Clamp01((-arousal*.9f-feature[1]*.3f-.15f)/.6f);
                }
                int best=0;for(int i=1;i<Count;i++)if(target[i]>target[best])best=i;
                status=target[best]>.3f?Names[best]+(UsesProfile?" (tu voz)":" (entonación)"):"Neutra";
            } else if(silence>=2.5f)status="En silencio";
            // Entra en ~1 s y se va despacio (~3 s), como una emoción real.
            for(int i=0;i<Count;i++)scores[i]=Mathf.Lerp(scores[i],target[i],Ease(dt,target[i]>scores[i]?.8f:3));
            if(p.learned>0&&Mathf.FloorToInt(p.learned)%15==0&&voiced){p.updated=DateTime.UtcNow.Ticks;dirty=true;}
        }
    }
}
