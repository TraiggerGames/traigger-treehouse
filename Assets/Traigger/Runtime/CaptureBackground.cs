using System;
using System.Runtime.InteropServices;
using UnityEngine;
namespace Traigger {
    // Entrada HDMI (capturadora USB) de fondo detrás de Traigger, como la película: va en la capa de la película, así
    // sale también en la cámara «Traigger». La captura es nativa (Plugins/Apple/TraiggerCapture.swift, iPad/iPhone y Mac);
    // la controla VisionCapture.
    public class CaptureBackground : MonoBehaviour {
        public Camera target;
        // La imagen de fondo se sube a la GPU como mucho 30 veces por segundo: copiar 1080p a 60 fps en el hilo
        // principal hacía que todo (también Traigger) fuera a tirones.
        float nextUpload,costFrom;double costMs;int costCount;
        Transform screen;Material material;MeshRenderer screenRenderer;Texture2D texture;byte[] pixels;GCHandle handle;long shown=-1;
        void OnDisable(){if(screen)screen.gameObject.SetActive(false);}
        void OnDestroy(){if(handle.IsAllocated)handle.Free();VisionCapture.Stop();}
        void LateUpdate(){
            if(!target)return;
            if(!VisionCapture.Running||!VisionCapture.Background){if(screen)screen.gameObject.SetActive(false);shown=-1;return;}
            long serial=VisionCapture.FrameInfo(out int w,out int h);
            if(serial<0||w<=0||h<=0){return;}
            if(serial!=shown&&Time.unscaledTime>=nextUpload){
                nextUpload=Time.unscaledTime+1f/30;
                var watch=System.Diagnostics.Stopwatch.StartNew();
                if(!texture||texture.width!=w||texture.height!=h){
                    if(texture)Destroy(texture);
                    texture=new Texture2D(w,h,TextureFormat.BGRA32,false){name="Traigger HDMI",wrapMode=TextureWrapMode.Clamp};
                    if(handle.IsAllocated)handle.Free();pixels=new byte[w*h*4];handle=GCHandle.Alloc(pixels,GCHandleType.Pinned);
                }
                if(VisionCapture.CopyFrame(handle.AddrOfPinnedObject(),pixels.Length)){texture.LoadRawTextureData(pixels);texture.Apply(false);}
                shown=serial;
                costMs+=watch.Elapsed.TotalMilliseconds;costCount++;
                if(Time.unscaledTime-costFrom>5){Debug.Log($"Traigger: fondo HDMI {w}x{h}, {costCount/(Time.unscaledTime-costFrom):0} subidas/s, {costMs/Mathf.Max(1,costCount):0.0} ms cada una");costFrom=Time.unscaledTime;costMs=0;costCount=0;}
            }
            if(!screen)Build();
            material.mainTexture=texture;material.color=new Color(1,1,1,VisionCapture.Opacity);screen.gameObject.SetActive(true);
            screenRenderer.sortingOrder=VisionCapture.Front?-31900:-32400;
            // Llena el encuadre sin deformar (recortando lo que sobre), detrás de Traigger y delante de la imagen de fondo.
            float viewHeight=target.orthographicSize*2,viewWidth=viewHeight*target.aspect,aspect=(float)w/h;
            var area=VisionCapture.Area;float sw,sh;Vector2 center=Vector2.zero;
            if(area==new Rect(0,0,1,1))sw=(viewWidth/viewHeight>aspect?viewWidth:viewHeight*aspect)*VisionCapture.Scale;
            else{
                // Con un rectángulo del modo layout: cabe entero dentro de él (sin recortar).
                float aw=viewWidth*area.width,ah=viewHeight*area.height;
                sw=(aw/ah>aspect?ah*aspect:aw)*VisionCapture.Scale;
                center=new Vector2((area.center.x-.5f)*viewWidth,(area.center.y-.5f)*viewHeight);
            }
            sh=sw/aspect;
            screen.localPosition=new Vector3(center.x,center.y,target.farClipPlane*.94f);screen.localRotation=Quaternion.identity;screen.localScale=new Vector3(sw,sh,1);
        }
        void Build(){
            var go=new GameObject("Traigger HDMI");go.layer=MoviePlayer.Layer;
            // Las filas llegan de arriba abajo: se invierte la V.
            var mesh=new Mesh{vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(.5f,-.5f),new Vector3(-.5f,.5f),new Vector3(.5f,.5f)},
                uv=new[]{new Vector2(0,1),new Vector2(1,1),new Vector2(0,0),new Vector2(1,0)},triangles=new[]{0,2,1,2,3,1}};
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            material=new Material(Shader.Find("Traigger/UnlitAlpha"));
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.sortingOrder=-32400;screenRenderer=renderer;
            screen=go.transform;screen.SetParent(target.transform,false);
        }
    }

    // API de la entrada HDMI: capturadoras USB (UVC) en iPad/iPhone (iPadOS 17+) y Mac. En Vision Pro no hay USB: allí
    // se recibe lo que envía el iPad o el Mac (SendToVision) en la ventana «Entrada HDMI» (ShowWindow).
    public static class VisionCapture {
        [Serializable]public class Device{public string id,name;}
        [Serializable]class DeviceList{public Device[] items;}
        public static bool Running {get;private set;}
        public static bool Audio {get;private set;}
        public static bool SendingToVision {get;private set;}
        // De fondo detrás de Traigger (en Unity) y/o en su propia ventana flotante (nativa, se escala con los dedos).
        public static bool Background {get;set;}=true;
        public static bool WindowOpen {get;private set;}
        // Tamaño de la imagen de fondo (1 = llena el encuadre; menos, más pequeña y centrada).
        public static float Scale=1;
        // Orden respecto a la película (delante o detrás) y opacidad, desde el editor de escena.
        public static bool Front;public static float Opacity=1;
        // Zona de la capturadora (0–1, origen abajo a la izquierda); toda la pantalla = llenar recortando.
        public static Rect Area=new Rect(0,0,1,1);
        public static string DeviceId {get;private set;}="";
        // En iPad la entrada de sonido es única: con el sonido de la capturadora, la boca no sigue a la voz.
        public static bool MutesVoice=>Running&&Audio&&Application.platform==RuntimePlatform.IPhonePlayer;
#if (UNITY_IOS||UNITY_STANDALONE_OSX||UNITY_EDITOR_OSX)&&!UNITY_VISIONOS
#if UNITY_IOS&&!UNITY_EDITOR
        const string Lib="__Internal";
#else
        const string Lib="TraiggerMoviePlayer";
#endif
        [DllImport(Lib)]static extern IntPtr TraiggerCaptureList();
        [DllImport(Lib)]static extern void TraiggerCaptureStart(string id,int audio);
        [DllImport(Lib)]static extern void TraiggerCaptureStop();
        [DllImport(Lib)]static extern IntPtr TraiggerCaptureStatus();
        [DllImport(Lib)]static extern long TraiggerCaptureFrameInfo(out int w,out int h);
        [DllImport(Lib)]static extern int TraiggerCaptureCopyFrame(IntPtr destination,int capacity);
        [DllImport(Lib)]static extern void TraiggerCaptureSendToVision(int on);
        [DllImport(Lib)]static extern void TraiggerCaptureFree(IntPtr p);
        [DllImport(Lib)]static extern void TraiggerCaptureSetQuality(string json);
        // Calidad del envío a Vision Pro: códec «h264» (menos latencia) o «hevc» (más calidad), alto (720, 1080 o 0 =
        // el de la fuente), fps, megabits por segundo y camino: «router» (por defecto), «direct» (AWDL) o «auto» (los dos).
        public static void SetQuality(string codec="h264",int height=1080,int fps=60,float mbps=15,string path="router")=>
            SetQualityJson("{\"codec\":\""+codec+"\",\"height\":"+height+",\"fps\":"+fps+",\"mbps\":"+mbps.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"keySeconds\":2,\"path\":\""+path+"\"}");
        public static void SetQualityJson(string json){if(!string.IsNullOrEmpty(json))TraiggerCaptureSetQuality(json);}
        public static bool Available=>true;
        static string Take(IntPtr p){if(p==IntPtr.Zero)return "";var s=Marshal.PtrToStringUTF8(p);TraiggerCaptureFree(p);return s;}
        public static Device[] Devices(){try{return JsonUtility.FromJson<DeviceList>("{\"items\":"+Take(TraiggerCaptureList())+"}").items??new Device[0];}catch(Exception){return new Device[0];}}
        public static string Status=>Running||SendingToVision?Take(TraiggerCaptureStatus()):"Sin capturadora";
        public static void Start(string deviceId="",bool audio=true){DeviceId=deviceId??"";Audio=audio;Running=true;TraiggerCaptureStart(DeviceId,audio?1:0);}
        public static void Stop(){if(!Running)return;Running=false;TraiggerCaptureStop();}
        public static void SendToVision(bool on){SendingToVision=on;TraiggerCaptureSendToVision(on?1:0);}
        internal static long FrameInfo(out int w,out int h)=>TraiggerCaptureFrameInfo(out w,out h);
        internal static bool CopyFrame(IntPtr destination,int capacity)=>TraiggerCaptureCopyFrame(destination,capacity)!=0;
#else
        public static bool Available=>false;
        public static Device[] Devices()=>new Device[0];
        public static string Status=>Application.platform==RuntimePlatform.VisionOS?"Las Vision Pro reciben la entrada HDMI del iPad o del Mac":"Sin entrada HDMI en esta plataforma";
        public static void Start(string deviceId="",bool audio=true){}
        public static void Stop(){}
        public static void SendToVision(bool on){}
        public static void SetQuality(string codec="h264",int height=1080,int fps=60,float mbps=15,string path="router"){}
        public static void SetQualityJson(string json){}
        internal static long FrameInfo(out int w,out int h){w=h=0;return -1;}
        internal static bool CopyFrame(IntPtr destination,int capacity)=>false;
#endif
        // Ventana «Entrada HDMI» de las Vision Pro (se abre sola al recibir; esto la abre o cierra a mano).
        public static void ShowWindow(bool on){
            WindowOpen=on;
            if(on&&!Running&&Available)Start(DeviceId,true);
            VisionNative.Request(on?"hdmi:window:on":"hdmi:window:off");
        }
    }
}
