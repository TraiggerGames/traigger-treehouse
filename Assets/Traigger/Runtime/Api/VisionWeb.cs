using System;
using System.Runtime.InteropServices;
using UnityEngine;

// Navegador web nativo controlado desde Unity (mismo estilo que la API de UnityRealityKitBridge: Unity decide,
// el visor dibuja). En Vision Pro se abre en su propia ventana o, inmersivo, en una pantalla grande con el entorno
// oscurecido; en iPhone/iPad, a pantalla completa sobre la escena. Fuentes nativas: Plugins/Apple/UI/TraiggerWeb.swift.
public static class VisionWeb {
    // Cada página cargada: dirección y título.
    public static event Action<string, string> PageChanged;
    public static string CurrentUrl {get;private set;}="";
    public static string CurrentTitle {get;private set;}="";

    public static void Open(string url)=>VisionNative.Request("web:open:"+url);
    public static void Show()=>VisionNative.Request("web:show");
    public static void Close()=>VisionNative.Request("web:close");
    // Solo Vision Pro: pantalla grande en un espacio inmersivo (el resto de apps se ocultan mientras dura).
    public static bool Immersive {get;private set;}
    public static void SetImmersive(bool on){Immersive=on;VisionNative.Request(on?"web:immersive:on":"web:immersive:off");}
    // iPhone/iPad: la web a pantalla completa con Traigger flotando encima (en vez de al lado).
    public static bool Overlay {get;private set;}
    public static event Action<bool> OverlayChanged;
    public static void SetOverlay(bool on){Overlay=on;OverlayChanged?.Invoke(on);VisionNative.Request(on?"web:overlay:on":"web:overlay:off");}

    // Lo llama el puente nativo al terminar de cargar cada página.
    public static void Report(string url,string title){CurrentUrl=url??"";CurrentTitle=title??"";PageChanged?.Invoke(CurrentUrl,CurrentTitle);}
}

// La película del cine de Traigger fuera de Unity: en Vision Pro, en su propia ventana (que se mueve y escala por
// separado) o en una pantalla de cine inmersiva. Usa los fotogramas de libVLC (VLCKit, LGPL 2.1, enlazado dinámico).
public static class VisionCinema {
    public static float Scale {get;private set;}=1;
    public static bool WindowOpen {get;private set;}
    public static bool Immersive {get;private set;}
    public static void ShowWindow(bool on){WindowOpen=on;VisionNative.Request(on?"cinema:window:on":"cinema:window:off");}
    // 1 = tamaño de partida (ventana de 96 cm o pantalla inmersiva de 3 m de ancho); 0,5–4.
    public static void SetScale(float scale){Scale=Mathf.Clamp(scale,.5f,4);VisionNative.Request("cinema:scale:"+Scale.ToString(System.Globalization.CultureInfo.InvariantCulture));}
    public static void SetImmersive(bool on){Immersive=on;VisionNative.Request(on?"cinema:immersive:on":"cinema:immersive:off");}
}

// Envío de peticiones a la parte nativa de cada plataforma.
static class VisionNative {
#if UNITY_VISIONOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void TraiggerVisionRequest(string request);
    public static void Request(string request)=>TraiggerVisionRequest(request);
    public static void AvatarRect(Rect r){}
#elif UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void TraiggerNativeRequest(string request);
    public static void Request(string request)=>TraiggerNativeRequest(request);
    [DllImport("__Internal")] static extern void TraiggerSetAvatarRect(float x,float y,float width,float height);
    public static void AvatarRect(Rect r)=>TraiggerSetAvatarRect(r.x,r.y,r.width,r.height);
#else
    public static void AvatarRect(Rect r){}
    public static void Request(string request)=>Debug.Log("Traigger (sin interfaz nativa en esta plataforma): "+request);
#endif
}

// Fondos: una imagen detrás del personaje (JPG/PNG; también sale en la cámara «Traigger») y, en Vision Pro, una imagen
// de 360° o HDRI (.hdr, .exr o panorámica equirectangular) que envuelve el espacio inmersivo.
public static class VisionBackground {
    public static string Image {get;private set;}="";
    public static string Sky {get;private set;}="";
    public static event Action<string> ImageChanged;
    public static void SetImage(string path){Image=path??"";ImageChanged?.Invoke(Image);}
    public static void SetSky(string path){Sky=path??"";VisionNative.Request(string.IsNullOrEmpty(Sky)?"sky:off":"sky:"+Sky);}
}

// Vision Pro: Traigger «libre» (sin ventana ni fondo, se coloca en cualquier sitio del espacio) y la ventana para
// compartir por SharePlay/FaceTime, que junta la actividad (cine, HDMI o navegador) con Traigger encima como logo de
// canal («logo») o a su lado como avatar de llamada («side»).
public static class VisionShare {
    public static bool Free {get;private set;}
    public static void SetFree(bool on){Free=on;VisionNative.Request(on?"free:on":"free:off");}
    public static void Open(string source="cinema",string mode="logo")=>VisionNative.Request("share:open:"+source+":"+mode);
    public static void Close()=>VisionNative.Request("share:close");
}

// Escenas y carteles (como OBS): VisionScenes.Apply("Vuelvo enseguida"), ShowCard("brb"), HideCard().
public static class VisionScenes {
    static Traigger.StudioController Studio=>UnityEngine.Object.FindAnyObjectByType<Traigger.StudioController>();
    public static string[] Names()=>Studio?Studio.SceneNames():new string[0];
    public static void Apply(string name){var s=Studio;if(s)s.ApplyScene(name);}
    public static void Save(string name){var s=Studio;if(s)s.SaveScene(name);}
    public static void ShowCard(string kind,string title=null,string subtitle=null,bool coverAvatar=false){
        var s=Studio;if(!s||!s.Cards)return;
        var card=Traigger.StreamCards.Preset(kind);if(title!=null)card.title=title;if(subtitle!=null)card.subtitle=subtitle;card.coverAvatar=coverAvatar;
        s.Cards.Show(card);
    }
    public static void HideCard(){var s=Studio;if(s&&s.Cards)s.Cards.Hide();}
}

// Emisión en directo (Twitch, YouTube, RTMP): VisionStream.Start("rtmp://live.twitch.tv/app", clave).
public static class VisionStream {
    static Traigger.StreamOutput Output=>UnityEngine.Object.FindAnyObjectByType<Traigger.StreamOutput>();
    public static bool Live{get{var o=Output;return o&&o.Active;}}
    public static string Status{get{var o=Output;return o?o.Status:"";}}
    public static void Start(string url,string key,int width=1280,int height=720,int fps=30,float mbps=4.5f){
        var o=Output;if(o)o.StartStream(new Traigger.StreamOutput.Settings{url=url,key=key,width=width,height=height,fps=fps,mbps=mbps});
    }
    public static void Stop(){var o=Output;if(o)o.StopStream();}
    public static void SetVolumes(float mic,float movie,float capture){var o=Output;if(o)o.SetGains(mic,movie,capture);}
}
