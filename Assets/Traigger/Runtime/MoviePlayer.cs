using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
namespace Traigger {
    // Cine en Traigger: reproduce películas de tu disco o NAS (SMB) con libVLC 4 (MKV, subtítulos, cualquier formato) y
    // las pone de fondo detrás del personaje. Como la cámara del avatar las dibuja, también salen en la cámara «Traigger»
    // de la videollamada. El sonido sale por el altavoz del dispositivo. Plugin: Plugins/Apple/TraiggerMoviePlayer.m.
    public class MoviePlayer : MonoBehaviour {
        public Camera target;
        public bool background=true;
        // Zona de la imagen (0–1) donde se encaja la película; el resto queda libre para Traigger (modo «junto a la película»).
        public Rect area=new Rect(0,0,1,1);
        // Tamaño de la película dentro de su zona (1 = lo más grande que cabe sin deformar).
        public float scale=1;
        // Capa propia para la película y sus subtítulos: así se puede ocultar a Traigger sin ocultar la película.
        public const int Layer=31;
        [Range(0,1)]public float opacity=1;
        [Range(0,100)]public int volume=80;
        public string status {get;private set;}="Sin película";
        public string title {get;private set;}="";
#if (UNITY_STANDALONE_OSX||UNITY_EDITOR_OSX)
        const string Lib="TraiggerMoviePlayer";
        public static bool Supported=>true;
#elif (UNITY_IOS||UNITY_VISIONOS)&&!UNITY_EDITOR
        const string Lib="__Internal";
        public static bool Supported=>true;
#else
        const string Lib="__Internal";
        public static bool Supported=>false;
#endif
        [DllImport(Lib)]static extern int TraiggerMoviePlay(string location);
        [DllImport(Lib)]static extern void TraiggerMoviePause(int paused);
        [DllImport(Lib)]static extern void TraiggerMovieStop();
        [DllImport(Lib)]static extern int TraiggerMovieIsPlaying();
        [DllImport(Lib)]static extern double TraiggerMovieTime();
        [DllImport(Lib)]static extern double TraiggerMovieLength();
        [DllImport(Lib)]static extern void TraiggerMovieSeek(double seconds);
        [DllImport(Lib)]static extern void TraiggerMovieVolume(int percent);
        [DllImport(Lib)]static extern long TraiggerMovieFrameInfo(out int width,out int height);
        [DllImport(Lib)]static extern int TraiggerMovieCopyFrame(byte[] destination,int capacity);
        [DllImport(Lib)]static extern int TraiggerMovieBrowse(string location);
        [DllImport(Lib)]static extern IntPtr TraiggerMovieBrowseResult();
        [DllImport(Lib,EntryPoint="TraiggerMovieFree")]static extern void Free(IntPtr pointer);
        [DllImport(Lib)]static extern IntPtr TraiggerMovieTracks(int type);
        [DllImport(Lib)]static extern void TraiggerMovieSelectTrack(int type,string id);
        [DllImport(Lib)]static extern int TraiggerMovieAddSubtitle(string location);
        [DllImport(Lib)]static extern void TraiggerMovieSendAudio(int enabled);
        // Enviar el sonido de la película al Mac (micrófono «Traigger») en vez de sonar aquí. Si cambia en plena
        // película, se reabre en el mismo punto (VLC necesita un reproductor nuevo para ello).
        bool audioToMac;Item currentItem;
        public bool AudioToMac{get=>audioToMac;set{
            if(value==audioToMac)return;audioToMac=value;TraiggerMovieSendAudio(value?1:0);
            if(playing&&currentItem!=null){double at=time;bool wasPaused=paused;Play(currentItem);SeekTo(at);if(wasPaused)TogglePause();}
        }}

        // Pistas de audio y de subtítulos de la película (idioma y nombre), para elegirlas.
        public const int AudioTracks=0,SubtitleTracks=2;
        [Serializable]public class Track{public string id,name,language;public bool selected;}
        [Serializable]class TrackList{public Track[] items;}
        public Track[] Tracks(int type){
            if(!playing)return new Track[0];
            var pointer=TraiggerMovieTracks(type);if(pointer==IntPtr.Zero)return new Track[0];
            string json=Marshal.PtrToStringUTF8(pointer);Free(pointer);
            return JsonUtility.FromJson<TrackList>("{\"items\":"+json+"}")?.items??new Track[0];
        }
        public void SelectTrack(int type,string id){if(playing)TraiggerMovieSelectTrack(type,id??"");}
        public bool AddSubtitle(string location)=>playing&&TraiggerMovieAddSubtitle(location)!=0;

        // Subtítulos generados con Apple (TraiggerSubtitles.swift): la escucha va 12 s por delante de la imagen para que
        // cada frase esté transcrita (y traducida) antes de decirse. Al activarlos o al saltar tardan esos 12 s.
        [DllImport(Lib)]static extern int TraiggerListenStart(double from);
        [DllImport(Lib)]static extern void TraiggerListenStop();
        [DllImport(Lib)]static extern double TraiggerListenTime();
        [DllImport(Lib)]static extern void TraiggerListenPause(int paused);
        [DllImport(Lib)]static extern void TraiggerSubtitlesConfigure(string language,string translate);
        [DllImport(Lib)]static extern void TraiggerSubtitlesClear();
        [DllImport(Lib)]static extern IntPtr TraiggerSubtitlesText(double time);
        [DllImport(Lib)]static extern IntPtr TraiggerSubtitlesStatus();
        public const float ListenLead=12;
        public bool generatedSubtitles;
        public string subtitleLanguage="",translateTo="";   // "" = idioma de la pista de audio / sin traducir
        public string generatedStatus {get;private set;}="";
        bool listening,needsRestart;string appliedConfig,languageCache="";float nextConfig;string caption="";TextMesh captionText,captionShadow;
        static readonly System.Collections.Generic.Dictionary<string,string> Iso3=new System.Collections.Generic.Dictionary<string,string>{{"spa","es-ES"},{"eng","en-US"},{"fra","fr-FR"},{"fre","fr-FR"},
            {"deu","de-DE"},{"ger","de-DE"},{"ita","it-IT"},{"por","pt-PT"},{"jpn","ja-JP"},{"kor","ko-KR"},{"zho","zh-CN"},{"chi","zh-CN"},{"cat","ca-ES"},{"rus","ru-RU"}};
        string MovieLanguage(){
            if(!string.IsNullOrEmpty(subtitleLanguage))return subtitleLanguage;
            foreach(var track in Tracks(AudioTracks))if(track.selected&&!string.IsNullOrEmpty(track.language))return Iso3.TryGetValue(track.language,out var code)?code:track.language;
            return "";
        }
        void UpdateGenerated(){
            if(!generatedSubtitles||!playing){
                if(listening){TraiggerListenStop();listening=false;}
                caption="";generatedStatus=generatedSubtitles?"Pon una película":"";return;
            }
            // El idioma de la pista se consulta una vez por segundo (no en cada fotograma).
            if(Time.unscaledTime>=nextConfig){nextConfig=Time.unscaledTime+1;languageCache=MovieLanguage();}
            string config=languageCache+"|"+translateTo;
            if(config!=appliedConfig){bool onlyTranslation=appliedConfig!=null&&appliedConfig.Split('|')[0]==languageCache;appliedConfig=config;TraiggerSubtitlesConfigure(languageCache,translateTo);if(!onlyTranslation)needsRestart=true;}
            if(!listening||needsRestart){needsRestart=false;listening=TraiggerListenStart(time+ListenLead)!=0;}
            double lead=TraiggerListenTime()-time;
            // Con la película en pausa, la escucha se para al llegar a 30 s de adelanto; sigue cuando baja de 20 s.
            if(paused||lead>30)TraiggerListenPause(1);else if(lead<20)TraiggerListenPause(0);
            var pointer=TraiggerSubtitlesText(time);
            caption=pointer==IntPtr.Zero?"":Marshal.PtrToStringUTF8(pointer);if(pointer!=IntPtr.Zero)Free(pointer);
            var status=TraiggerSubtitlesStatus();generatedStatus=Marshal.PtrToStringUTF8(status);Free(status);
            if(lead<ListenLead-2&&string.IsNullOrEmpty(caption))generatedStatus=$"Preparando subtítulos… empiezan en unos {Mathf.CeilToInt((float)(ListenLead-Math.Max(0,lead)))} s";
        }
        // Dos líneas como mucho, centradas en la parte baja de la película.
        void DrawCaption(float width,float height){
            if(!screen)return;
            if(!captionText){
                var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                captionShadow=MakeCaption(font,Color.black,-31001);captionText=MakeCaption(font,Color.white,-31000);
            }
            string text=Wrap(caption,44);
            bool visible=background&&screen.gameObject.activeSelf&&text.Length>0;
            captionText.gameObject.SetActive(visible);captionShadow.gameObject.SetActive(visible);
            if(!visible)return;
            float lineHeight=height*.055f,size=lineHeight/(captionText.fontSize*.1f);
            var position=screen.localPosition+new Vector3(0,-height*.5f+height*.09f,-.01f);
            captionText.text=captionShadow.text=text;captionText.characterSize=captionShadow.characterSize=size;
            captionText.transform.localPosition=position;captionShadow.transform.localPosition=position+new Vector3(lineHeight*.05f,-lineHeight*.05f,.001f);
        }
        TextMesh MakeCaption(Font font,Color color,int order){
            var go=new GameObject("Traigger Movie Caption");go.transform.SetParent(target.transform,false);go.layer=Layer;
            var mesh=go.AddComponent<TextMesh>();mesh.font=font;mesh.fontSize=64;mesh.anchor=TextAnchor.LowerCenter;mesh.alignment=TextAlignment.Center;mesh.color=color;
            var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=font.material;renderer.sortingOrder=order;
            return mesh;
        }
        static string Wrap(string text,int width){
            if(string.IsNullOrEmpty(text)||text.Length<=width)return text??"";
            int cut=text.LastIndexOf(' ',Math.Min(text.Length-1,text.Length/2+8));
            return cut>0?text.Substring(0,cut)+"\n"+text.Substring(cut+1):text;
        }

        [Serializable]public class Item{public string name,url;public bool folder;}
        [Serializable]class Listing{public string status;public Item[] items;}
        public readonly List<Item> items=new List<Item>();
        public string folder {get;private set;}="";
        public bool browsing {get;private set;}
        public bool playing {get;private set;}public bool paused {get;private set;}
        public double time {get;private set;}public double length {get;private set;}
        Texture2D texture;byte[] frame;long lastFrame=-1;Transform screen;Material material;int appliedVolume=-1;float statusTime;

        void Start(){
            // Se recuerdan la última carpeta y el volumen.
            folder=PlayerPrefs.GetString("traigger.movieFolder","");volume=PlayerPrefs.GetInt("traigger.movieVolume",80);
            generatedSubtitles=PlayerPrefs.GetInt("traigger.generatedSubtitles",0)==1;
            subtitleLanguage=PlayerPrefs.GetString("traigger.subtitleLanguage","");translateTo=PlayerPrefs.GetString("traigger.translateTo","");
        }

        int browseRetries;
        public void Browse(string location){browseRetries=0;BrowseNow(location);}
        void BrowseNow(string location){
            if(string.IsNullOrEmpty(location))return;
            if(!location.EndsWith("/"))location+="/";
            folder=location;PlayerPrefs.SetString("traigger.movieFolder",location);
            items.Clear();browsing=TraiggerMovieBrowse(location)!=0;status=browsing?"Leyendo carpeta…":"No se pudo abrir esa dirección";
        }
        public void Up(){
            if(string.IsNullOrEmpty(folder))return;
            string trimmed=folder.TrimEnd('/');int slash=trimmed.LastIndexOf('/');
            if(slash>trimmed.IndexOf("://")+2)Browse(trimmed.Substring(0,slash+1));
        }
        public void Play(Item item){
            if(item.folder){Browse(item.url);return;}
            title=Uri.UnescapeDataString(item.name??"");currentItem=item;
            if(listening){TraiggerListenStop();listening=false;}TraiggerSubtitlesClear();appliedConfig=null;
            playing=TraiggerMoviePlay(item.url)!=0;paused=false;lastFrame=-1;appliedVolume=-1;
            status=playing?"Reproduciendo «"+title+"»":"No se pudo abrir «"+title+"»";
        }
        public void TogglePause(){if(!playing)return;paused=!paused;TraiggerMoviePause(paused?1:0);}
        public void Skip(double seconds){if(playing){TraiggerMovieSeek(Math.Max(0,Math.Min(length-1,time+seconds)));needsRestart=true;}}
        public void SeekTo(double seconds){if(playing){TraiggerMovieSeek(seconds);time=seconds;needsRestart=true;}}
        public void Stop(){if(listening){TraiggerListenStop();listening=false;}TraiggerSubtitlesClear();appliedConfig=null;TraiggerMovieStop();playing=false;paused=false;title="";status="Sin película";if(screen)screen.gameObject.SetActive(false);}

        void Update(){
            if(browsing){
                var pointer=TraiggerMovieBrowseResult();
                if(pointer!=IntPtr.Zero){
                    string json=Marshal.PtrToStringUTF8(pointer);Free(pointer);browsing=false;
                    var listing=JsonUtility.FromJson<Listing>(json);items.Clear();
                    // SMB a veces falla al primer intento (servidor dormido, disco arrancando, Wi‑Fi despertando): reintentar solo.
                    if((listing==null||listing.status!="ok")&&browseRetries<2){browseRetries++;BrowseNow(folder);status="Reintentando ("+browseRetries+"/2)…";return;}
                    if(listing!=null&&listing.items!=null)foreach(var item in listing.items){item.name=Uri.UnescapeDataString(item.name??"");items.Add(item);}
                    items.Sort((a,b)=>a.folder!=b.folder?(a.folder?-1:1):string.Compare(a.name,b.name,StringComparison.OrdinalIgnoreCase));
                    status=listing!=null&&listing.status=="ok"?(items.Count==0?"Carpeta vacía":items.Count+" elementos"):(listing!=null&&listing.status=="timeout"?"El servidor no respondió a tiempo (¿dormido o fuera de la red?)":"El servidor rechazó la conexión o la carpeta no existe (¿usuario, contraseña o ruta?)");
                }
            }
            UpdateGenerated();
            if(!playing)return;
            if(volume!=appliedVolume){TraiggerMovieVolume(volume);appliedVolume=volume;PlayerPrefs.SetInt("traigger.movieVolume",volume);}
            if(Time.unscaledTime-statusTime>.25f){statusTime=Time.unscaledTime;time=TraiggerMovieTime();length=TraiggerMovieLength();}
            long serial=TraiggerMovieFrameInfo(out int width,out int height);
            if(serial<0||serial==lastFrame||width<=0||height<=0)return;
            lastFrame=serial;
            if(!texture||texture.width!=width||texture.height!=height){
                if(texture)Destroy(texture);
                texture=new Texture2D(width,height,TextureFormat.BGRA32,false){name="Traigger Movie",wrapMode=TextureWrapMode.Clamp};
                frame=new byte[width*height*4];
            }
            if(TraiggerMovieCopyFrame(frame,frame.Length)==0)return;
            texture.LoadRawTextureData(frame);texture.Apply(false);
            UpdateScreen(width,height);
        }

        // Una pantalla detrás del personaje que ocupa el encuadre entero sin deformar la película.
        void UpdateScreen(int width,int height){
            if(!target)return;
            if(!screen){
                // Malla de un cuadrado hecha a mano (el proyecto no incluye el módulo de física que usa CreatePrimitive).
                var quad=new GameObject("Traigger Movie Screen");
                var mesh=new Mesh{name="Traigger Movie Quad",vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(.5f,-.5f),new Vector3(-.5f,.5f),new Vector3(.5f,.5f)},
                    uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)},triangles=new[]{0,2,1,2,3,1}};
                quad.AddComponent<MeshFilter>().sharedMesh=mesh;
                material=new Material(Shader.Find("Traigger/UnlitAlpha"));
                var renderer=quad.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.sortingOrder=-32000;quad.layer=Layer;
                screen=quad.transform;screen.SetParent(target.transform,false);
            }
            screen.gameObject.SetActive(background);
            material.mainTexture=texture;material.color=new Color(1,1,1,opacity);
            float viewHeight=target.orthographic?target.orthographicSize*2:2*Mathf.Tan(target.fieldOfView*.5f*Mathf.Deg2Rad)*(target.farClipPlane*.9f);
            float fullWidth=viewHeight*target.aspect,fullHeight=viewHeight;
            float viewWidth=fullWidth*area.width,aspect=(float)width/height;viewHeight=fullHeight*area.height;
            float w=(viewWidth/viewHeight>aspect?viewHeight*aspect:viewWidth)*scale,h=w/aspect;
            var center=new Vector2((area.center.x-.5f)*fullWidth,(area.center.y-.5f)*fullHeight);
            // libVLC entrega las filas de arriba abajo: se voltea la pantalla.
            screen.localPosition=new Vector3(center.x,center.y,target.farClipPlane*.9f);screen.localRotation=Quaternion.identity;screen.localScale=new Vector3(w,-h,1);
            DrawCaption(w,h);
        }

        static string Clock(double seconds){var t=TimeSpan.FromSeconds(Math.Max(0,seconds));return t.TotalHours>=1?t.ToString(@"h\:mm\:ss"):t.ToString(@"m\:ss");}

        // Pestaña «Cine» del panel.
        string address;
        public void DrawPanel(bool touch){
            address??=string.IsNullOrEmpty(folder)?"smb://usuario:contraseña@servidor/carpeta/":folder;
            GUILayout.Label("Películas de tu disco o NAS (SMB). Escribe la dirección y pulsa Abrir:");
            address=GUILayout.TextField(address,GUILayout.Height(touch?44:22));
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Abrir"))Browse(address);
            if(GUILayout.Button("Subir"))Up();
            GUILayout.EndHorizontal();
            GUILayout.Label("· "+status);
            foreach(var item in items.ToArray())if(GUILayout.Button((item.folder?"📁 ":"🎬 ")+item.name)){if(item.folder)address=item.url;Play(item);}
            if(!playing)return;
            GUILayout.Space(8);
            GUILayout.Label("«"+title+"»  "+Clock(time)+" / "+Clock(length));
            if(length>1){float position=GUILayout.HorizontalSlider((float)time,0,(float)length,GUILayout.Height(touch?36:14));if(Mathf.Abs(position-(float)time)>2)SeekTo(position);}
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("−30 s"))Skip(-30);
            if(GUILayout.Button(paused?"▶︎ Seguir":"⏸ Pausa"))TogglePause();
            if(GUILayout.Button("+30 s"))Skip(30);
            if(GUILayout.Button("■ Parar"))Stop();
            GUILayout.EndHorizontal();
            GUILayout.Label("Volumen");volume=Mathf.RoundToInt(GUILayout.HorizontalSlider(volume,0,100,GUILayout.Height(touch?36:14)));
            background=GUILayout.Toggle(background,"Película de fondo detrás de Traigger (también en la cámara «Traigger»)");
            GUILayout.Label("Opacidad");opacity=GUILayout.HorizontalSlider(opacity,.2f,1,GUILayout.Height(touch?36:14));
        }
        void OnDestroy(){if(playing)TraiggerMovieStop();if(texture)Destroy(texture);if(material)Destroy(material);}
    }
}
