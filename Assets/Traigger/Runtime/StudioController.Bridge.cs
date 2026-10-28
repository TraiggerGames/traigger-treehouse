using System;
using UnityEngine;
namespace Traigger {
    // Lo que la interfaz nativa (SwiftUI) ve y puede hacer: un estado en JSON y órdenes sencillas. Toda la lógica sigue
    // aquí, en C#; la interfaz solo dibuja. El transporte (dentro de la app o por red con el editor) está en TraiggerBridge.
    public partial class StudioController {
        [Serializable]public class BridgeCommand{public string cmd,s;public int i;public float f;public bool b;}
        [Serializable]public class CameraOption{public int id;public string name;}
        [Serializable]public class BridgeState{
            public string platform;
            public string[] expressions;public int expression=-1;
            public bool demo,tracking,naturalSpeech,voiceEmotion;public float voiceEmotionStrength;public string emotion,intonation;
            public int speechLanguage;public string[] speechLanguages;
            public string mouth,phrases,heard,face,body,visionSensors;public bool visionAvailable,visionImmersive;
            public CameraOption[] cameras;public int cameraMode;public bool saveBattery,invertLegs;
            public bool virtualCameraAvailable,virtualCamera;public string virtualCameraStatus;
            public string[] voiceProfile;public bool[] voiceCalibrated;public string voiceStatus,voiceSync;public bool voiceUsesProfile;
            public string layout;public bool cinemaLayout,avatarHidden;
            public bool movieAvailable,moviePlaying,moviePaused,movieBrowsing,movieBackground;public string movieStatus,movieTitle,movieFolder;
            public double movieTime,movieLength;public int movieVolume;public float movieOpacity;
            public MoviePlayer.Item[] movieItems;public MoviePlayer.Track[] audioTracks,subtitleTracks;
            public bool generatedSubtitles;public string generatedStatus,subtitleLanguage,translateTo;
            public BridgeControl[] controls;public string backgroundImage,backgroundSky;
            public string webUrl,webTitle;public bool webImmersive,cinemaWindow,cinemaImmersive;public float cinemaScale;
            public CharacterOption[] characters;public string character,characterMessage;
            public OutfitOption[] outfits;public string outfit;public OutfitSwitch[] outfitOptions;
            public bool streamSupported,streamLive;public string streamStatus;
            public float screenAspect;public float[] layerAvatar,layerMovie,layerCapture;
            public string[] scenes;public string scene,cardKind,cardTitle,cardSubtitle,cardImage,cardTitleMedia;public bool cardCover;
            public float captureScale,movieScale,captureOpacity=1;public bool captureFront;public bool captureAvailable,captureOn,captureAudio,captureToVision,captureBackground,captureWindow;public string captureStatus,captureDevice;public VisionCapture.Device[] captureDevices;
        }
        // Posiciones rápidas del personaje: centro o a un lado y en pequeño, para no tapar la película.
        static readonly string[] Layouts={"center","left","right","corner","side"};
        public string layout="center";public bool cinemaLayout=true,avatarHidden;bool movieWasPlaying;int visibleMask=~0;
        // Ocultar a Traigger: la cámara deja de dibujar todo salvo la capa de la película (sin tocar las piezas del rig).
        public void SetAvatarHidden(bool hidden){
            if(!avatarCamera)return;
            if(!avatarHidden)visibleMask=avatarCamera.cullingMask|1<<MoviePlayer.Layer; // se respeta la máscara que tuviera
            avatarHidden=hidden;
            avatarCamera.cullingMask=hidden?(1<<MoviePlayer.Layer)|(1<<StreamCards.Layer):visibleMask;
        }
        static float P(string v)=>float.TryParse(v,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var f)?f:1;
        [Serializable]public class CharacterOption{public string name,path,author,version,license,preview;public bool builtIn;}
        [Serializable]public class OutfitOption{public string id,name,preview;}
        [Serializable]public class OutfitSwitch{public string id,label;public bool on;}
        float charactersRefresh,outfitsRefresh;System.Collections.Generic.List<Outfits.Entry> outfitsCache;CharacterOption[] charactersCache=new CharacterOption[0];
        StreamOutput stream;float streamRefresh;string streamStatusCache="";
        float captureRefresh;VisionCapture.Device[] captureDevicesCache=new VisionCapture.Device[0];string captureStatusCache="";
        float trackRefresh;MoviePlayer.Track[] audioTracksCache=new MoviePlayer.Track[0],subtitleTracksCache=new MoviePlayer.Track[0];

        public BridgeState GetBridgeState(){
            var st=new BridgeState{platform=Application.platform.ToString()};
            st.expressions=Array.ConvertAll(expressions,x=>x.name);st.expression=expressionIndex;
            st.demo=demo;st.tracking=tracking;st.naturalSpeech=naturalSpeech;st.voiceEmotion=voiceEmotion;st.voiceEmotionStrength=voiceEmotionStrength;
            st.emotion=EmotionLabel();st.intonation=emotion.status;
            st.speechLanguage=Mathf.Max(0,speechLanguage);st.speechLanguages=SpeechLanguageNames;
            bool visionOS=vision&&Application.platform==RuntimePlatform.VisionOS;
            st.mouth=visionOS?vision.voiceStatus:mic?mic.status:"";
            st.phrases=visionOS?vision.speechStatus:mic?mic.speechStatus:"";
            st.heard=visionOS?vision.speechText:mic?mic.speechText:"";
            st.face=deviceFace?deviceFace.status:"";st.body=BodySensorStatus();
            st.visionAvailable=visionOS;st.visionImmersive=visionOS&&vision.immersive;st.visionSensors=visionOS?vision.sensorStatus:"";
            var cameras=new System.Collections.Generic.List<CameraOption>();
            if(Application.platform==RuntimePlatform.IPhonePlayer)for(int m=0;m<CameraModeNames.Length;m++)if(CameraModeAvailable(m))cameras.Add(new CameraOption{id=m,name=CameraModeNames[m]+(m==4&&HasLiDAR?" + LiDAR":"")});
            st.cameras=cameras.ToArray();st.cameraMode=cameraMode;st.saveBattery=saveBattery;st.invertLegs=invertLegs;
            st.virtualCameraAvailable=virtualCamera;st.virtualCamera=virtualCamera&&virtualCamera.active;st.virtualCameraStatus=virtualCamera?virtualCamera.status:"";
            st.voiceProfile=VoiceEmotion.Names;st.voiceCalibrated=new bool[VoiceEmotion.Count];for(int i=0;i<VoiceEmotion.Count;i++)st.voiceCalibrated[i]=emotion.Calibrated(i);
            st.voiceStatus=emotion.calibrating>=0?emotion.status:"";st.voiceSync=VoiceProfileStore.status;st.voiceUsesProfile=emotion.UsesProfile;
            st.layout=layout;st.cinemaLayout=cinemaLayout;st.avatarHidden=avatarHidden;
            st.controls=GetControls();st.backgroundImage=VisionBackground.Image;st.backgroundSky=VisionBackground.Sky;
            st.webUrl=VisionWeb.CurrentUrl;st.webTitle=VisionWeb.CurrentTitle;st.webImmersive=VisionWeb.Immersive;st.cinemaWindow=VisionCinema.WindowOpen;st.cinemaImmersive=VisionCinema.Immersive;st.cinemaScale=VisionCinema.Scale;
            if(Time.unscaledTime>=charactersRefresh){charactersRefresh=Time.unscaledTime+3;charactersCache=CharacterPack.List().ConvertAll(x=>new CharacterOption{name=x.name,path=x.path,author=x.author,version=x.version,license=x.license,preview=x.preview,builtIn=x.builtIn}).ToArray();}
            st.characters=charactersCache;st.character=PlayerPrefs.GetString("traigger.character","");
            // Outfits del personaje en uso (por ahora, los de Traigger 2D).
            if(st.character.Length==0){
                if(outfitsCache==null||Time.unscaledTime>=outfitsRefresh){outfitsRefresh=Time.unscaledTime+5;outfitsCache=Outfits.For("Traigger");}
                st.outfits=outfitsCache.ConvertAll(o=>new OutfitOption{id=o.id,name=o.name,preview=o.preview}).ToArray();st.outfit=Outfits.Current;
                var chosen=outfitsCache.Find(o=>o.id==st.outfit);
                st.outfitOptions=chosen?.definition?.options==null?new OutfitSwitch[0]:Array.ConvertAll(chosen.definition.options,o=>new OutfitSwitch{id=o.id,label=o.label,on=Outfits.OptionOn(chosen.id,o)});
            }
            st.characterMessage=characterMessage.Length>0?characterMessage:StudioBootstrap.CharacterStatus;
            st.streamSupported=StreamOutput.Supported;st.streamLive=stream&&stream.Active;
            if(Time.unscaledTime>=streamRefresh){streamRefresh=Time.unscaledTime+1;streamStatusCache=stream?stream.Status:"";}
            st.streamStatus=streamStatusCache;
            st.scenes=SceneNames();st.scene=currentScene;
            st.screenAspect=avatarCamera?avatarCamera.aspect:16f/9;
            float[] F(Rect r)=>new[]{r.x,r.y,r.width,r.height};
            st.layerAvatar=F(AvatarRect);st.layerMovie=F(MovieRect);st.layerCapture=F(CaptureRect);
            var card=cards?cards.current:null;st.cardKind=card!=null?card.kind:"";st.cardTitle=card?.title;st.cardSubtitle=card?.subtitle;
            st.cardImage=PlayerPrefs.GetString("traigger.cardImage","");st.cardTitleMedia=card?.titleMedia;st.cardCover=PlayerPrefs.GetInt("traigger.cardCover",0)==1;
            st.captureAvailable=VisionCapture.Available;st.captureOn=VisionCapture.Running;st.captureAudio=VisionCapture.Audio;st.captureToVision=VisionCapture.SendingToVision;st.captureDevice=VisionCapture.DeviceId;st.captureBackground=VisionCapture.Background;st.captureWindow=VisionCapture.WindowOpen;st.captureScale=VisionCapture.Scale;st.captureOpacity=VisionCapture.Opacity;st.captureFront=VisionCapture.Front;
            if(Time.unscaledTime>=captureRefresh){captureRefresh=Time.unscaledTime+2;captureDevicesCache=VisionCapture.Devices();captureStatusCache=VisionCapture.Status;}
            st.captureDevices=captureDevicesCache;st.captureStatus=captureStatusCache;
            st.movieAvailable=movie;
            if(movie){
                st.moviePlaying=movie.playing;st.moviePaused=movie.paused;st.movieBrowsing=movie.browsing;st.movieBackground=movie.background;
                st.movieStatus=movie.status;st.movieTitle=movie.title;st.movieFolder=movie.folder;st.movieTime=movie.time;st.movieLength=movie.length;
                st.movieVolume=movie.volume;st.movieOpacity=movie.opacity;st.movieScale=movie.scale;st.movieItems=movie.items.ToArray();
                // Las pistas cambian poco: se consultan cada segundo.
                if(Time.unscaledTime-trackRefresh>1){trackRefresh=Time.unscaledTime;audioTracksCache=movie.Tracks(MoviePlayer.AudioTracks);subtitleTracksCache=movie.Tracks(MoviePlayer.SubtitleTracks);}
                st.audioTracks=audioTracksCache;st.subtitleTracks=subtitleTracksCache;
                st.generatedSubtitles=movie.generatedSubtitles;st.generatedStatus=movie.generatedStatus;st.subtitleLanguage=movie.subtitleLanguage;st.translateTo=movie.translateTo;
            }
            return st;
        }

        // Una sola fuente a la vez: al empezar el vídeo, la capturadora o el navegador se paran las otras dos.
        void OnlySource(string keep){
            if(keep!="video"&&movie&&movie.playing)movie.Stop();
            if(keep!="hdmi"&&VisionCapture.Running)VisionCapture.Stop();
            if(keep!="web"&&VisionWeb.CurrentUrl.Length>0){VisionWeb.Close();VisionWeb.Report("","");}
        }
        public void ApplyBridgeCommand(BridgeCommand c){
            if(ApplySceneCommand(c)||ApplyLayoutCommand(c))return;
            switch(c.cmd){
            case "expression":if(c.i>=0&&c.i<expressions.Length)SetExpression(c.i);break;
            case "neutralPose":head=turn=nod=lateral=mouth=smile=blinkLeft=blinkRight=armLeft=armRight=legLeft=legRight=torso=hips=neck=tail=earLeft=earRight=earMood=0;ResetBodyPose();demo=false;expressionIndex=-1;break;
            case "demo":demo=c.b;break;
            case "tracking":tracking=c.b;break;
            case "naturalSpeech":naturalSpeech=c.b;break;
            case "voiceEmotion":voiceEmotion=c.b;break;
            case "voiceEmotionStrength":voiceEmotionStrength=Mathf.Clamp(c.f,0,1.5f);break;
            case "speechLanguage":if(c.i>=0&&c.i<SpeechLanguageCodes.Length){speechLanguage=c.i;ApplySpeechLanguage();}break;
            case "cameraMode":if(CameraModeAvailable(c.i))SetCameraMode(c.i);break;
            case "saveBattery":saveBattery=c.b;if(avatarCamera)avatarCamera.enabled=!(cameraMode>0&&saveBattery);break;
            case "invertLegs":invertLegs=c.b;break;
            case "sensors":if(vision)vision.SetSensors(c.b);break;
            case "recenter":if(vision)vision.Recenter();break;
            case "virtualCamera":if(virtualCamera){virtualCamera.active=c.b;PlayerPrefs.SetInt("traigger.virtualCamera",c.b?1:0);}break;
            case "calibrate":if(c.i>=0&&c.i<VoiceEmotion.Count&&emotion.calibrating<0)emotion.Calibrate(c.i);break;
            case "resetVoiceProfile":emotion.ResetProfile();break;
            case "layout":SetLayout(c.s);break;
            case "avatarHidden":SetAvatarHidden(c.b);break;
            case "cinemaLayout":cinemaLayout=c.b;PlayerPrefs.SetInt("traigger.cinemaLayout",c.b?1:0);break;
            case "resetView":if(manipulator)manipulator.ResetView();layout="center";break;
            case "debugPanel":showPanel=c.b&&Application.isEditor;break;
            case "movie.browse":if(movie)movie.Browse(c.s);break;
            case "movie.up":if(movie)movie.Up();break;
            case "movie.open":OnlySource("video");if(movie&&movie.items.Exists(x=>x.url==c.s))movie.Play(movie.items.Find(x=>x.url==c.s));else if(movie)movie.Play(new MoviePlayer.Item{url=c.s,name=System.IO.Path.GetFileName(c.s)});break;
            case "movie.pause":if(movie&&movie.paused!=c.b)movie.TogglePause();break;
            case "movie.skip":if(movie)movie.Skip(c.f);break;
            case "movie.seek":if(movie)movie.SeekTo(c.f);break;
            case "movie.stop":if(movie)movie.Stop();break;
            case "movie.volume":if(movie)movie.volume=Mathf.Clamp(c.i,0,100);break;
            case "movie.scale":if(movie)movie.scale=Mathf.Clamp(c.f,.2f,2);break;
            case "capture.scale":VisionCapture.Scale=Mathf.Clamp(c.f,.2f,2);break;
            case "movie.opacity":if(movie)movie.opacity=Mathf.Clamp(c.f,.2f,1);break;
            case "movie.background":if(movie)movie.background=c.b;break;
            case "movie.audio":if(movie){movie.SelectTrack(MoviePlayer.AudioTracks,c.s);trackRefresh=0;}break;
            case "movie.subtitles":if(movie){movie.SelectTrack(MoviePlayer.SubtitleTracks,c.s);trackRefresh=0;}break;
            case "movie.generated":if(movie){movie.generatedSubtitles=c.b;PlayerPrefs.SetInt("traigger.generatedSubtitles",c.b?1:0);}break;
            case "movie.generatedLanguage":if(movie){movie.subtitleLanguage=c.s??"";PlayerPrefs.SetString("traigger.subtitleLanguage",movie.subtitleLanguage);}break;
            case "movie.translate":if(movie){movie.translateTo=c.s??"";PlayerPrefs.SetString("traigger.translateTo",movie.translateTo);}break;
            // Navegador y cine nativos: desde la interfaz o desde la API (VisionWeb, VisionCinema).
            case "background.image":VisionBackground.SetImage(c.s);PlayerPrefs.SetString("traigger.backgroundImage",c.s??"");break;
            case "background.sky":VisionBackground.SetSky(c.s);PlayerPrefs.SetString("traigger.backgroundSky",c.s??"");break;
            case "control":ApplyControl(c.s,c.f);break;
            case "web.page":{var parts=(c.s??"").Split('\n');VisionWeb.Report(parts[0],parts.Length>1?parts[1]:"");break;}
            case "web.open":OnlySource("web");VisionWeb.Open(c.s);break;
            case "web.show":OnlySource("web");VisionWeb.Show();break;
            case "web.immersive":VisionWeb.SetImmersive(c.b);break;
            case "web.overlay":SetWebOverlay(c.b);break;
            case "avatar.drag":DragAvatar(c.s);break;
            case "avatar.feed":SetAvatarFeed(c.b);break;
            case "avatar.pinch":if(manipulator)manipulator.scale=Mathf.Clamp(manipulator.scale*Mathf.Clamp(c.f,.5f,2),manipulator.minScale,manipulator.maxScale);break;
            case "capture.start":OnlySource("hdmi");VisionCapture.Start(c.s,c.b);break;
            case "capture.stop":VisionCapture.Stop();break;
            case "stream.start":if(stream)stream.StartStream(JsonUtility.FromJson<StreamOutput.Settings>(c.s));break;
            case "stream.stop":if(stream)stream.StopStream();break;
            case "stream.gains":{var g=(c.s??"").Split(',');if(stream&&g.Length==3)stream.SetGains(P(g[0]),P(g[1]),P(g[2]));break;}
            case "capture.quality":VisionCapture.SetQualityJson(c.s);break;
            case "capture.vision":VisionCapture.SendToVision(c.b);break;
            case "capture.background":VisionCapture.Background=c.b;break;
            case "capture.audio":if(VisionCapture.Running)VisionCapture.Start(VisionCapture.DeviceId,c.b);break; // entrada de sonido: capturadora o micrófono
            case "capture.front":VisionCapture.Front=c.b;break;
            case "capture.opacity":VisionCapture.Opacity=Mathf.Clamp01(c.f);break;
            case "capture.window":VisionCapture.ShowWindow(c.b);break;
            case "cinema.window":VisionCinema.ShowWindow(c.b);if(movie&&c.b)movie.background=false;break;
            case "cinema.scale":VisionCinema.SetScale(c.f);break;
            case "cinema.immersive":VisionCinema.SetImmersive(c.b);if(movie&&c.b)movie.background=false;break;
            case "movie.addSubtitle":if(movie){movie.AddSubtitle(c.s);trackRefresh=0;}break;
            default:Debug.LogWarning("Traigger: orden desconocida «"+c.cmd+"»");break;
            }
        }

        public void SetLayout(string name){
            if(!manipulator||!avatarCamera||Array.IndexOf(Layouts,name)<0)return;
            layout=name;
            float half=avatarCamera.orthographicSize*avatarCamera.aspect;
            // «Junto a la película»: la película se encaja en los 3/4 izquierdos y Traigger va en la franja derecha; en una
            // pantalla vertical (iPhone) se apilan: el contenido arriba y Traigger debajo.
            bool portrait=avatarCamera.aspect<1;
            var content=name!="side"?Full:portrait?new Rect(0,0,1,.58f):new Rect(0,0,.76f,1);
            PlayerPrefs.SetString("traigger.layer.movie",Format(content));PlayerPrefs.SetString("traigger.layer.capture",Format(content));
            ApplyLayerRects();
            switch(name){
            case "side" when portrait:manipulator.MoveTo(new Vector2(0,-avatarCamera.orthographicSize*.5f),.46f);break;
            case "side":manipulator.MoveTo(new Vector2(half*.76f,-avatarCamera.orthographicSize*.38f),.42f);break;
            case "left":manipulator.MoveTo(new Vector2(-half*.62f,-avatarCamera.orthographicSize*.32f),.55f);break;
            case "right":manipulator.MoveTo(new Vector2(half*.62f,-avatarCamera.orthographicSize*.32f),.55f);break;
            case "corner" when portrait:manipulator.MoveTo(new Vector2(half*.6f,-avatarCamera.orthographicSize*.62f),.3f);break;
            case "corner":manipulator.MoveTo(new Vector2(half*.78f,-avatarCamera.orthographicSize*.52f),.38f);break;
            default:manipulator.MoveTo(Vector2.zero,1);break;
            }
        }
        // Modo cine: al empezar una película Traigger se aparta a una esquina, y vuelve al centro al pararla.
        // Traigger encima de la web (iPhone/iPad): fondo transparente y, cada fotograma, su zona en pantalla para que los
        // toques fuera de él lleguen al navegador. El fondo vuelve a ser el de antes al salir.
        Color backgroundBeforeOverlay;bool webOverlay;
        // La imagen de Traigger con transparencia la emite AvatarOverlayFeed hacia la capa nativa sobre el navegador.
        void SetWebOverlay(bool on){webOverlay=on;UpdateAvatarFeed();VisionWeb.SetOverlay(on);}
        // También con las ventanas flotantes de vídeo (película, capturadora): Traigger se dibuja por encima de ellas.
        bool avatarFeed;
        void SetAvatarFeed(bool on){avatarFeed=on;Debug.Log("Traigger: orden avatar.feed "+on);UpdateAvatarFeed();}
        void UpdateAvatarFeed(){
            var feed=GetComponent<AvatarOverlayFeed>();
            if(!feed&&avatarCamera){feed=gameObject.AddComponent<AvatarOverlayFeed>();feed.source=avatarCamera;}
            if(feed)feed.active=webOverlay||avatarFeed;
        }
        // Arrastrar o pellizcar a Traigger desde la capa nativa (fracciones de pantalla y factor de escala).
        void DragAvatar(string delta){
            if(!manipulator||!avatarCamera||string.IsNullOrEmpty(delta))return;
            var parts=delta.Split(',');
            if(parts.Length<2||!float.TryParse(parts[0],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float dx)||
               !float.TryParse(parts[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float dy))return;
            float height=avatarCamera.orthographicSize*2,width=height*avatarCamera.aspect;
            manipulator.offset+=new Vector2(dx*width,-dy*height);
        }
        void UpdateCinemaLayout(){
            if((webOverlay||avatarFeed)&&manipulator)VisionNative.AvatarRect(manipulator.ViewportRect());
            if(!movie)return;
            // En iPhone/iPad, si Traigger se envía al Mac, el sonido de la película va también allí (micrófono «Traigger»).
            if(Application.platform==RuntimePlatform.IPhonePlayer)movie.AudioToMac=virtualCamera&&virtualCamera.active;
            if(movie.playing!=movieWasPlaying){
                movieWasPlaying=movie.playing;
                if(cinemaLayout)SetLayout(movie.playing?"side":"center");
            }
        }
    }
}
