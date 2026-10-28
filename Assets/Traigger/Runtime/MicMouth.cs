using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
namespace Traigger {
    // Boca con la voz: lee el micrófono y convierte el volumen en apertura de la boca. Sirve donde no hay seguimiento
    // facial (Vision Pro, Mac sin iPhone) y se suma a la cámara cuando la hay. Se calibra solo con el ruido de fondo.
    public class MicMouth : MonoBehaviour {
        public bool active=true;
        [Range(.5f,4)]public float sensitivity=1.5f;
        public float open {get;private set;}   // 0..1
        public float zcr {get;private set;}    // cruces por cero (agudo / sibilante)
        public float lowRatio {get;private set;} // proporción de graves (o, u)
        public float pitch {get;private set;}    // tono en Hz (0 = sin tono), para la emoción de la voz
        public string status {get;private set;}="Micrófono apagado";
        // Frases entendidas y su emoción (iPhone/iPad, plugin TraiggerVoiceIOS); mismos campos que en Vision Pro.
        public string speechText="",speechStatus="Sin reconocimiento de voz",wordEmotion,emotionCue;public float wordEmotionLevel,wordEmotionTime=-100;
#if UNITY_IOS&&!UNITY_EDITOR
        // En iPhone/iPad el audio lo captura el plugin nativo: voz aislada y el mismo audio para entender frases.
        [DllImport("__Internal")]static extern void TraiggerVoiceStart();
        [DllImport("__Internal")]static extern void TraiggerVoiceStop();
        [DllImport("__Internal")]static extern void TraiggerVoiceLanguage(string code);
        [DllImport("__Internal")]static extern void TraiggerVoiceRead(float[] values);
        [DllImport("__Internal")]static extern System.IntPtr TraiggerVoicePoll();
        const bool Native=true;
#else
        static void TraiggerVoiceStart(){}static void TraiggerVoiceStop(){}static void TraiggerVoiceLanguage(string code){}static void TraiggerVoiceRead(float[] v){}static System.IntPtr TraiggerVoicePoll()=>System.IntPtr.Zero;
        const bool Native=false;
#endif
        readonly float[] nativeValues=new float[4];bool nativeRunning;
        public void SetSpeechLanguage(string code){TraiggerVoiceLanguage(code);}
        [System.Serializable]class NativeMessage{public string voiceStatus,speechStatus,speechText,emotion,emotionCue;public float emotionLevel;}
        void ReadNativeMessages(){
            for(int i=0;i<8;i++){
                var pointer=TraiggerVoicePoll();if(pointer==System.IntPtr.Zero)return;
                string json=Marshal.PtrToStringUTF8(pointer);Marshal.FreeHGlobal(pointer);
                NativeMessage m;try{m=JsonUtility.FromJson<NativeMessage>(json);}catch{continue;}
                if(!string.IsNullOrEmpty(m.voiceStatus))status=m.voiceStatus;
                if(!string.IsNullOrEmpty(m.speechStatus))speechStatus=m.speechStatus;
                if(!string.IsNullOrEmpty(m.speechText))speechText=m.speechText;
                if(!string.IsNullOrEmpty(m.emotion)){emotionCue=m.emotionCue;wordEmotion=m.emotion;wordEmotionLevel=Mathf.Clamp01(m.emotionLevel);wordEmotionTime=Time.unscaledTime;}
            }
        }
        AudioClip clip;string device;readonly float[] buffer=new float[512],pitchBuffer=new float[2048];float noise=.002f,peak=.05f;
        IEnumerator Start(){
#pragma warning disable CS0162
            if(Native){status="Pidiendo micrófono…";yield break;}
            // En Vision Pro el micrófono lo lleva el host nativo (voz aislada): abrirlo también aquí le quitaría el audio.
            if(Application.platform==RuntimePlatform.VisionOS){active=false;status="Micrófono en el host de Vision Pro";yield break;}
#pragma warning restore CS0162
            if(!Application.HasUserAuthorization(UserAuthorization.Microphone))yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
            if(!Application.HasUserAuthorization(UserAuthorization.Microphone)){status="Permiso de micrófono denegado";yield break;}
            if(Microphone.devices.Length==0){status="No hay micrófono";yield break;}
            device=Microphone.devices[0];clip=Microphone.Start(device,true,1,48000);status="Escuchando: "+device;
        }
        void Update(){
            float rms;
#pragma warning disable CS0162
            if(Native){
                if(active!=nativeRunning){nativeRunning=active;if(active)TraiggerVoiceStart();else TraiggerVoiceStop();}
                ReadNativeMessages();
                if(!active){open=Mathf.MoveTowards(open,0,Time.deltaTime*6);return;}
                TraiggerVoiceRead(nativeValues);rms=nativeValues[0];zcr=nativeValues[1];lowRatio=nativeValues[2];pitch=nativeValues[3];
            }else{
#pragma warning restore CS0162
            if(!active||!clip||!Microphone.IsRecording(device)){open=Mathf.MoveTowards(open,0,Time.deltaTime*6);return;}
            int pos=Microphone.GetPosition(device)-buffer.Length;if(pos<0)return;
            clip.GetData(buffer,pos);float sum=0,low=0,state=0;int crossings=0;
            for(int i=0;i<buffer.Length;i++){float v=buffer[i];sum+=v*v;state+=.08f*(v-state);low+=state*state;if(i>0&&(buffer[i-1]<0)!=(v<0))crossings++;}
            rms=Mathf.Sqrt(sum/buffer.Length);zcr=crossings/(float)buffer.Length;lowRatio=sum>0?low/sum:0;
            int pitchPos=pos-pitchBuffer.Length+buffer.Length;
            if(rms>noise*2&&pitchPos>=0){clip.GetData(pitchBuffer,pitchPos);pitch=PitchTracker.Estimate(pitchBuffer,pitchBuffer.Length,clip.frequency);}else pitch=0;
            }
            // Ruido de fondo: sube despacio y baja rápido. Pico: se adapta a tu volumen de voz.
            noise=rms<noise?Mathf.Lerp(noise,rms,.2f):Mathf.Lerp(noise,rms,.002f);
            peak=Mathf.Max(Mathf.Lerp(peak,noise*4,.001f),rms);
            float level=Mathf.Clamp01((rms-noise*1.8f)/Mathf.Max(.005f,(peak-noise*1.8f)/sensitivity));
            if(VisionCapture.MutesVoice)level=0;
            open=level>open?level:Mathf.MoveTowards(open,level,Time.deltaTime*7);
        }
        void OnDisable(){if(nativeRunning){nativeRunning=false;TraiggerVoiceStop();}if(device!=null&&Microphone.IsRecording(device))Microphone.End(device);}
    }
}
