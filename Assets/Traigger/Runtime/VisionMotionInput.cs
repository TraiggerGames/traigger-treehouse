using System;
using System.Runtime.InteropServices;
using UnityEngine;
namespace Traigger {
    // Recibe del host de Vision Pro la pose de la cabeza y de las manos (ARKit) y la convierte en controles del
    // personaje: inclinar, girar y asentir con tu cabeza; levantar los brazos con tus manos. Los ojos y la cara no
    // llegan: visionOS no da a las apps el seguimiento ocular ni las cámaras internas.
    public class VisionMotionInput : MonoBehaviour {
        [Serializable] class V3 {public float x,y,z;public Vector3 v=>new Vector3(x,y,-z);}
        [Serializable] class Q4 {public float x,y,z,w;}
        [Serializable] class Head {public V3 position;public Q4 rotation;}
        [Serializable] class Hand {public bool tracked;public V3 wrist;public float pinch;public float sampleAge;public float[] curls;}
        [Serializable] class BodyArm {public float raise=-1,bend;public bool up;}
        [Serializable] class BodyLeg {public float spread=999,bend;}
        [Serializable] class Frame {public bool body,body3D;public float roll=999,yaw=999,pitch=999,lean=999;public BodyArm armLeft,armRight;public BodyLeg legLeft,legRight;public string bodyStatus;public Head head;public Hand left,right;public int headSamples=-1;public float voice=-1,zcr=-1,low=-1,f0=-1;public string voiceStatus,sensors,speechText,speechStatus,emotion,emotionCue;public float emotionLevel=-1;public bool immersive;}
        public bool hasHead {get;private set;}
        public float yaw,pitch,roll;              // grados respecto a la postura de calibración
        public float leftArm,rightArm;            // 0 = mano baja, 1 = mano a la altura de la cabeza
        public bool leftTracked,rightTracked,leftPinch,rightPinch;
        public float[] leftCurls=new float[5],rightCurls=new float[5];public float lastLeftHand=-100,lastRightHand=-100;
        static bool ReadCurls(Hand h,float[] target){if(h==null||!h.tracked||float.IsNaN(h.sampleAge)||float.IsInfinity(h.sampleAge)||h.sampleAge<0||h.sampleAge>.3f||h.curls==null||h.curls.Length!=5)return false;for(int i=0;i<5;i++)if(float.IsNaN(h.curls[i])||float.IsInfinity(h.curls[i])||h.curls[i]<0||h.curls[i]>1)return false;Array.Copy(h.curls,target,5);return true;}
        public float lastArrival=-100;
        // Diagnóstico y estado del host (se muestran en el panel).
        public int messages,headSamplesReceived;public string sensorStatus="Sensores apagados (modo compartido)",voiceStatus="Esperando micrófono";public bool immersive;
        // Boca con la voz aislada por Apple: puerta de ruido que sólo abre con voz cercana y clara.
        public float lastBody=-100,bodyRoll,bodyYaw,bodyPitch,bodyArmLeft,bodyArmRight,bodyLean,bodyLegSpreadLeft,bodyLegSpreadRight,bodyKneeLeft,bodyKneeRight,lastLegs=-100;public bool bodyLeftTracked,bodyRightTracked,body3D;
        public string bodyStatus="Sin sensor de cuerpo";float pitchRest;bool pitchCalibrated;
        public bool bodyLive=>Time.unscaledTime-lastBody<.5f;
        public bool legsLive=>Time.unscaledTime-lastLegs<.5f;
        public float voiceOpen {get;private set;}public float voiceZcr,voiceLow,voicePitch;
        // Frases entendidas en el casco y la emoción que el modelo local les asigna (ver TraiggerSpeech.swift).
        public string speechText="",speechStatus="Sin reconocimiento de voz",wordEmotion,emotionCue;public float wordEmotionLevel,wordEmotionTime=-100;public float lastVoice=-100;
        [Range(.5f,4)]public float voiceSensitivity=1.5f;float floor=.002f,peak=.03f;
#if UNITY_VISIONOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void TraiggerVisionRequest(string request);
#else
        static void TraiggerVisionRequest(string request){}
#endif
        public void SetSpeechLanguage(string code){TraiggerVisionRequest("speech:lang:"+code);}
        public void OpenRelaySettings(){TraiggerVisionRequest("relay:settings");}
        public void SetSensors(bool on){sensorStatus=on?"Abriendo espacio de sensores…":"Cerrando sensores…";TraiggerVisionRequest(on?"sensors:on":"sensors:off");}
        Quaternion reference=Quaternion.identity;bool calibrated;
        public void Recenter(){calibrated=false;pitchCalibrated=false;}
        // ARKit usa mano derecha (z hacia el usuario); se convierte a la convención de Unity.
        public void OnNative(string json){
            Frame f;try{f=JsonUtility.FromJson<Frame>(json);}catch{return;}
            if(f==null)return;messages++;
            if(ReadCurls(f.left,leftCurls))lastLeftHand=Time.unscaledTime;else if(f.left!=null)lastLeftHand=-100;
            if(ReadCurls(f.right,rightCurls))lastRightHand=Time.unscaledTime;else if(f.right!=null)lastRightHand=-100;
            if(!string.IsNullOrEmpty(f.voiceStatus))voiceStatus=f.voiceStatus;
            if(!string.IsNullOrEmpty(f.speechStatus))speechStatus=f.speechStatus;
            if(!string.IsNullOrEmpty(f.speechText))speechText=f.speechText;
            if(!string.IsNullOrEmpty(f.emotion)){emotionCue=f.emotionCue;wordEmotion=f.emotion;wordEmotionLevel=Mathf.Clamp01(f.emotionLevel);wordEmotionTime=Time.unscaledTime;return;}
            if(!string.IsNullOrEmpty(f.speechText)||!string.IsNullOrEmpty(f.speechStatus))return;
            if(!string.IsNullOrEmpty(f.bodyStatus))bodyStatus=f.bodyStatus;
            // Cuerpo desde un iPhone o iPad en modo sensor (no hace falta el espacio inmersivo).
            if(f.body){
                lastBody=Time.unscaledTime;bodyStatus="Sensor de cuerpo conectado";body3D=f.body3D;
                // Inclinación del torso: solo el modo 3D (ARKit) la mide.
                bodyLean=f.lean<900?Mathf.Lerp(bodyLean,f.lean,.4f):Mathf.MoveTowards(bodyLean,0,Time.unscaledDeltaTime*20);
                if(f.roll<900){bodyRoll=Mathf.Lerp(bodyRoll,f.roll,.5f);bodyYaw=Mathf.Lerp(bodyYaw,f.yaw*35,.5f);bodyPitch=Mathf.Lerp(bodyPitch,(f.pitch-pitchRest)*-40,.5f);if(!pitchCalibrated){pitchRest=f.pitch;pitchCalibrated=true;}}
                bodyLeftTracked=f.armLeft!=null&&f.armLeft.raise>=0;bodyRightTracked=f.armRight!=null&&f.armRight.raise>=0;
                if(bodyLeftTracked)bodyArmLeft=Mathf.Lerp(bodyArmLeft,f.armLeft.raise,.5f);
                if(bodyRightTracked)bodyArmRight=Mathf.Lerp(bodyArmRight,f.armRight.raise,.5f);
                // Piernas (solo cuerpo 3D): apertura del muslo y flexión de la rodilla, en grados.
                if(f.legLeft!=null&&f.legLeft.spread<900&&f.legRight!=null&&f.legRight.spread<900){
                    lastLegs=Time.unscaledTime;
                    bodyLegSpreadLeft=Mathf.Lerp(bodyLegSpreadLeft,f.legLeft.spread,.4f);bodyLegSpreadRight=Mathf.Lerp(bodyLegSpreadRight,f.legRight.spread,.4f);
                    bodyKneeLeft=Mathf.Lerp(bodyKneeLeft,f.legLeft.bend,.4f);bodyKneeRight=Mathf.Lerp(bodyKneeRight,f.legRight.bend,.4f);
                }
                return;
            }
            if(!string.IsNullOrEmpty(f.sensors)){sensorStatus=f.sensors;immersive=f.immersive;if(!immersive){hasHead=false;leftTracked=rightTracked=false;}}
            if(f.voice>=0){Voice(f.voice);if(f.zcr>=0){voiceZcr=f.zcr;voiceLow=f.low;}if(f.f0>=0)voicePitch=f.f0;return;}
            if(f.headSamples>=0)headSamplesReceived=f.headSamples;
            lastArrival=Time.unscaledTime;
            if(f.head!=null&&f.head.position!=null&&f.head.rotation!=null&&(f.head.rotation.w!=0||f.head.rotation.x!=0)){
                var q=new Quaternion(-f.head.rotation.x,-f.head.rotation.y,f.head.rotation.z,f.head.rotation.w);
                if(!calibrated){reference=Quaternion.Euler(0,q.eulerAngles.y,0);calibrated=true;}
                var e=(Quaternion.Inverse(reference)*q).eulerAngles;
                yaw=Mathf.DeltaAngle(0,e.y);pitch=-Mathf.DeltaAngle(0,e.x);roll=-Mathf.DeltaAngle(0,e.z);hasHead=true;
                Vector3 head=f.head.position.v;
                Arm(f.left,head,ref leftArm,ref leftTracked,ref leftPinch);Arm(f.right,head,ref rightArm,ref rightTracked,ref rightPinch);
            }
        }
        void Voice(float rms){
            float dt=Mathf.Clamp(Time.unscaledTime-lastVoice,.001f,.1f);lastVoice=Time.unscaledTime;
            // Fondo: baja enseguida al silencio y sube muy despacio (no se "come" una frase larga). Basado en tiempo,
            // no en número de mensajes. Pico: tu volumen de voz, que decae despacio.
            floor=rms<floor?Mathf.Lerp(floor,rms,1-Mathf.Exp(-8*dt)):Mathf.Lerp(floor,rms,1-Mathf.Exp(-.05f*dt));
            peak=Mathf.Max(Mathf.Lerp(peak,Mathf.Max(floor*5,.01f),1-Mathf.Exp(-.1f*dt)),rms);
            // La voz del usuario ya llega aislada; además se exige estar claramente por encima del fondo.
            float gate=floor*2.5f+.002f;
            float level=Mathf.Clamp01((rms-gate)/Mathf.Max(.004f,(peak-gate)/voiceSensitivity));
            voiceOpen=level>voiceOpen?level:Mathf.MoveTowards(voiceOpen,level,dt*7);
        }
        void Update(){if(Time.unscaledTime-lastVoice>.3f)voiceOpen=Mathf.MoveTowards(voiceOpen,0,Time.unscaledDeltaTime*6);}
        static void Arm(Hand h,Vector3 head,ref float arm,ref bool tracked,ref bool pinch){
            tracked=h!=null&&h.tracked&&h.wrist!=null;if(!tracked){return;}
            // Altura de la muñeca: 0,6 m por debajo de la cabeza = brazo bajado; a la altura de la cabeza = levantado.
            arm=Mathf.Clamp01(Mathf.InverseLerp(-.6f,0,h.wrist.v.y-head.y));pinch=h.pinch<.02f;
        }
    }
}
