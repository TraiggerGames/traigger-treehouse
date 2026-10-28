using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
namespace Traigger {
    // Cámara virtual del Mac: manda al personaje a la cámara «Traigger» (MacCamera), que FaceTime, Zoom, Meet o
    // Discord ven como una webcam. Funciona desde Traigger en el propio Mac o desde el iPhone/iPad, que se lo envía
    // al Mac por la Wi-Fi (Plugins/iOS/TraiggerMacCamera.swift). Dibuja la cámara del avatar en una imagen 1920×1080 aparte (la ventana no cambia),
    // la lee de la GPU sin bloquear y la entrega al plugin 60 veces por segundo.
    public class VirtualCameraOutput : MonoBehaviour {
        public Camera source;
        public bool active;
        public int width=1920,height=1080,fps=60;
        public string status {get;private set;}="Apagada";
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        [DllImport("TraiggerVirtualCam")] static extern int TraiggerVCamStart();
        [DllImport("TraiggerVirtualCam")] static extern void TraiggerVCamStop();
        [DllImport("TraiggerVirtualCam")] static extern int TraiggerVCamSend(IntPtr pixels,int width,int height);
        public static bool Supported=>true;
        static string NativeStatus()=>null;
#elif UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int TraiggerVCamStart();
        [DllImport("__Internal")] static extern void TraiggerVCamStop();
        [DllImport("__Internal")] static extern int TraiggerVCamSend(IntPtr pixels,int width,int height);
        [DllImport("__Internal")] static extern string TraiggerVCamStatus();
        public static bool Supported=>true;
        static string NativeStatus()=>TraiggerVCamStatus();
#else
        static int TraiggerVCamStart()=>0;static void TraiggerVCamStop(){}static int TraiggerVCamSend(IntPtr p,int w,int h)=>0;
        public static bool Supported=>false;
        static string NativeStatus()=>null;
#endif
        RenderTexture target;byte[] pixels;bool connected;int pending;float next,retryAt;int sent;float countFrom;
        void LateUpdate(){
            if(!active||!source){if(connected)Disconnect();return;}
            if(Time.unscaledTime<next)return;next=Mathf.Max(next+1f/fps,Time.unscaledTime-.5f/fps);
            if(!connected){
                if(Time.unscaledTime<retryAt)return;
                int result;try{result=TraiggerVCamStart();}catch(Exception e){status="Falta el plugin: "+e.Message;active=false;return;}
                if(result!=1){status=result==0?"La cámara «Traigger» no está instalada: abre Traigger Camera y pulsa Instalar":"No se pudo abrir la cámara «Traigger»";retryAt=Time.unscaledTime+3;return;}
                connected=true;status="Enviando a la cámara «Traigger»";countFrom=Time.unscaledTime;sent=0;
            }
            if(!target){target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){name="Traigger Virtual Camera"};target.Create();}
            // Se dibuja aparte: la ventana sigue igual y la imagen sale siempre en 16:9.
            var previous=source.targetTexture;source.targetTexture=target;source.Render();source.targetTexture=previous;source.ResetAspect();
            // Hasta dos lecturas en curso: la GPU tarda 1–2 fotogramas en devolver cada una.
            if(pending>=2)return;pending++;
            AsyncGPUReadback.Request(target,0,TextureFormat.BGRA32,Deliver);
        }
        void Deliver(AsyncGPUReadbackRequest request){
            pending--;
            if(request.hasError||!connected)return;
            var data=request.GetData<byte>();
            if(pixels==null||pixels.Length!=data.Length)pixels=new byte[data.Length];
            data.CopyTo(pixels);
            var handle=GCHandle.Alloc(pixels,GCHandleType.Pinned);
            try{sent+=TraiggerVCamSend(handle.AddrOfPinnedObject(),width,height);}finally{handle.Free();}
            if(Time.unscaledTime-countFrom>2){
                // En iPhone/iPad el plugin dice si ha encontrado el Mac; en el Mac se cuenta lo enviado.
                string native=NativeStatus();
                status=native!=null&&sent==0?native:(native??"Enviando a la cámara «Traigger»")+$" · {sent/(Time.unscaledTime-countFrom):0} fps";
                countFrom=Time.unscaledTime;sent=0;
            }
        }
        void Disconnect(){try{TraiggerVCamStop();}catch(Exception){}connected=false;status="Apagada";}
        void OnDisable(){if(connected)Disconnect();}
        void OnDestroy(){if(target){target.Release();Destroy(target);}}
    }
}
