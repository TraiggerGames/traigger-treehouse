using UnityEngine;
namespace Traigger {
    // La escena entregada referencia una receta y crea el rig real al pulsar Play.
    public class StudioBootstrap : MonoBehaviour {
        public RigDefinition definition;
        public AvatarRig preparedRig;
        public Camera preparedCamera, preparedControlCamera;
        [Tooltip("Activa para recibir el iPhone en una red local de confianza.")] public bool listenOnLAN;
        public int udpPort=49983;
        public static string CharacterStatus="";
        void Awake() {
            if(!definition){Debug.LogError("Falta la definición del avatar");enabled=false;return;}
            // Personaje elegido por el usuario (paquete de personaje, CharacterPack.cs); si falla, vuelve a Traigger.
            AvatarRig rig=null;float cameraSize=2.8f,cameraHeight=2.45f,cameraX=0;
            string pack=PlayerPrefs.GetString("traigger.character","");
            bool skin3D=pack.StartsWith(Skin3DAvatar.Prefix);
            if(pack.Length>0&&!skin3D){
                try{
                    var custom=CharacterPack.Load(pack,out var info);
                    if(preparedRig)preparedRig.gameObject.SetActive(false);
                    rig=AutoRig.Build(custom);definition=custom;cameraSize=info.cameraSize;cameraHeight=info.cameraHeight;cameraX=info.cameraX;
                    CharacterStatus="«"+custom.name+"» cargado";
                }catch(System.Exception e){
                    CharacterStatus="No se pudo cargar el personaje: "+e.Message;Debug.LogError("Traigger: "+CharacterStatus);
                    PlayerPrefs.DeleteKey("traigger.character");
                }
            }
            if(!rig)rig=preparedRig ? preparedRig : AutoRig.Build(definition);
            // Outfit de Traigger (ropa encima del rig montado, StreamingAssets/Outfits/Traigger).
            if(pack.Length==0){try{if(Outfits.Apply(rig,"Traigger"))CharacterStatus=Outfits.Status;}catch(System.Exception e){Debug.LogError("Traigger: outfit: "+e);}}
            var camera=preparedCamera ? preparedCamera : new GameObject("Avatar Camera").AddComponent<Camera>();
            camera.transform.position=new Vector3(cameraX,cameraHeight,-10);camera.orthographic=true;camera.orthographicSize=cameraSize;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.08f,.12f,0);
            var controlCamera=preparedControlCamera ? preparedControlCamera : new GameObject("Control Camera").AddComponent<Camera>();
            controlCamera.cullingMask=0;controlCamera.depth=-1;controlCamera.backgroundColor=new Color(.07f,.08f,.12f);
            var controller=gameObject.AddComponent<StudioController>();controller.rig=rig;controller.avatarCamera=camera;
            // Skin 3D: el rig 2D sigue calculando la pose (oculto) y el modelo 3D la copia.
            if(skin3D){
                try{Skin3DAvatar.Create(pack.Substring(Skin3DAvatar.Prefix.Length),controller,rig);CharacterStatus="Skin 3D cargado (experimental)";}
                catch(System.Exception e){CharacterStatus="No se pudo cargar el skin 3D: "+e.Message;Debug.LogError("Traigger: "+CharacterStatus);PlayerPrefs.DeleteKey("traigger.character");}
            }
            var network=new GameObject("Face Network");network.SetActive(false);
            var receiver=network.AddComponent<FaceReceiver>();receiver.listenOnLAN=listenOnLAN;receiver.port=udpPort;
            controller.receiver=receiver;network.SetActive(true);Application.runInBackground=true;
            controller.mic=gameObject.AddComponent<MicMouth>();
            controller.vision=Object.FindAnyObjectByType<VisionMotionInput>();
            // Vision Pro: el host nativo envía la cabeza y las manos a este objeto (TraiggerVisionInput.OnNative).
            if(Application.platform==RuntimePlatform.VisionOS){
                if(!controller.vision){var input=new GameObject("TraiggerVisionInput");controller.vision=input.AddComponent<VisionMotionInput>();}
                Application.targetFrameRate=90;
            }
            // En iPhone/iPad, la cámara frontal (ARKit) mueve al personaje sin otro dispositivo.
            if(Application.platform==RuntimePlatform.IPhonePlayer){
                controller.deviceFace=gameObject.AddComponent<ARKitFaceSource>();
                Application.targetFrameRate=60;Screen.sleepTimeout=SleepTimeout.NeverSleep;
            }
        }
    }
}
