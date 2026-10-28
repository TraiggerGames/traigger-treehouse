using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
namespace Traigger {
    // Emisión en directo (Twitch, YouTube o cualquier RTMP): manda la imagen compuesta (Traigger, película, capturadora,
    // carteles…) al emisor nativo (Plugins/Apple/TraiggerStream.swift), que la codifica en H.264 con el sonido en AAC.
    // Igual que la cámara virtual: dibuja la cámara del avatar en una imagen aparte, la lee de la GPU sin bloquear.
    public class StreamOutput : MonoBehaviour {
        public Camera source;
        [Serializable]public class Settings{public string url="",key="";public int width=1280,height=720,fps=30;public float mbps=4.5f;public int audioKbps=160;}
#if UNITY_IOS || UNITY_VISIONOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
        const string Lib="__Internal";
#else
        const string Lib="TraiggerMoviePlayer";
#endif
        [DllImport(Lib)]static extern void TraiggerStreamStart(string json);
        [DllImport(Lib)]static extern void TraiggerStreamStop();
        [DllImport(Lib)]static extern int TraiggerStreamIsLive();
        [DllImport(Lib)]static extern void TraiggerStreamSendVideo(IntPtr pixels,int width,int height);
        [DllImport(Lib)]static extern IntPtr TraiggerStreamStatus();
        [DllImport(Lib)]static extern void TraiggerStreamSetGains(float mic,float movie,float capture);
        [DllImport(Lib)]static extern void TraiggerStreamFree(IntPtr p);
        public static bool Supported=>true;
        public string Status{get{var p=TraiggerStreamStatus();if(p==IntPtr.Zero)return "";var s=Marshal.PtrToStringUTF8(p);TraiggerStreamFree(p);return s;}}
        public bool Live=>TraiggerStreamIsLive()!=0;
#else
        static void TraiggerStreamStart(string json){}static void TraiggerStreamStop(){}static void TraiggerStreamSendVideo(IntPtr p,int w,int h){}static void TraiggerStreamSetGains(float a,float b,float c){}
        public static bool Supported=>false;
        public string Status=>"Sin emisión en directo en esta plataforma";
        public bool Live=>false;
#endif
        public bool Active{get;private set;}
        Settings settings;RenderTexture target;byte[] pixels;int pending;float next;

        public void StartStream(Settings s){
            if(!Supported||s==null||string.IsNullOrEmpty(s.url)||string.IsNullOrEmpty(s.key))return;
            settings=s;Active=true;TraiggerStreamStart(JsonUtility.ToJson(s));
        }
        public void StopStream(){Active=false;TraiggerStreamStop();}
        public void SetGains(float mic,float movie,float capture)=>TraiggerStreamSetGains(mic,movie,capture);

        void LateUpdate(){
            if(!Active||!source||settings==null)return;
            if(Time.unscaledTime<next)return;next=Mathf.Max(next+1f/settings.fps,Time.unscaledTime-.5f/settings.fps);
            // Siempre 16:9 al tamaño de la emisión; el codificador lo recibe tal cual.
            if(!target||target.width!=settings.width||target.height!=settings.height){
                if(target)target.Release();
                target=new RenderTexture(settings.width,settings.height,24,RenderTextureFormat.ARGB32){name="Traigger Stream"};target.Create();
            }
            var previous=source.targetTexture;source.targetTexture=target;source.Render();source.targetTexture=previous;source.ResetAspect();
            if(pending>=2)return;pending++;
            int w=settings.width,h=settings.height;
            AsyncGPUReadback.Request(target,0,TextureFormat.BGRA32,request=>{
                pending--;if(request.hasError||!Active)return;
                var data=request.GetData<byte>();
                if(pixels==null||pixels.Length!=data.Length)pixels=new byte[data.Length];
                data.CopyTo(pixels);
                var handle=GCHandle.Alloc(pixels,GCHandleType.Pinned);
                try{TraiggerStreamSendVideo(handle.AddrOfPinnedObject(),w,h);}finally{handle.Free();}
            });
        }
        void OnDestroy(){if(Active)StopStream();if(target)target.Release();}
    }
}
