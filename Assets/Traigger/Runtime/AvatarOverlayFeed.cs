using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
namespace Traigger {
    // Vision Pro: la misma imagen alimenta a Traigger «libre» (volumen sin fondo) y la ventana para compartir.
    // iPhone/iPad, modo «Traigger encima de la web»: dibuja a Traigger con fondo transparente en una imagen del tamaño
    // de la pantalla (a media resolución) y se la pasa a la capa nativa que va por encima del navegador. Así el
    // navegador recibe sus toques con normalidad y Traigger se ve delante.
    public class AvatarOverlayFeed : MonoBehaviour {
        public Camera source;
        public bool active;
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void TraiggerAvatarOverlayFrame(IntPtr pixels,int width,int height);
#else
        static void TraiggerAvatarOverlayFrame(IntPtr pixels,int width,int height){}
#endif
#if UNITY_VISIONOS
        // En Vision Pro la ventana para compartir dibuja los carteles por su cuenta: aquí solo va Traigger.
        const int CardsMask=1<<StreamCards.Layer;
#else
        const int CardsMask=0;
#endif
        RenderTexture target;byte[] pixels;int pending;float next;
#if UNITY_IOS && !UNITY_VISIONOS
        // iPhone/iPad: mientras esta capa está activa, Traigger se ve SOLO en ella. Si Unity lo dibuja también en la
        // pantalla, las dos copias (desfasadas un par de fotogramas) se superponen y parece que le da un tic, sobre todo
        // en los ojos. Se quita únicamente del dibujado a pantalla: la cámara «Traigger» y la emisión lo siguen teniendo.
        const bool HideMainAvatar=true;
#else
        const bool HideMainAvatar=false;
#endif
        int savedMask;bool hidingMain;
        void OnEnable(){Camera.onPreCull+=PreCull;Camera.onPostRender+=PostRender;}
        void OnDisable(){Camera.onPreCull-=PreCull;Camera.onPostRender-=PostRender;}
        void PreCull(Camera camera){
            if(!HideMainAvatar||!active||camera!=source||camera.targetTexture!=null)return;
            savedMask=camera.cullingMask;hidingMain=true;
            camera.cullingMask=savedMask&(1<<MoviePlayer.Layer); // solo el fondo (película, capturadora, imagen)
        }
        void PostRender(Camera camera){if(hidingMain&&camera==source){camera.cullingMask=savedMask;hidingMain=false;}}
        // Si la lectura asíncrona falla (o no existe en la plataforma), se lee directamente con ReadPixels.
        int asyncErrors;Texture2D readback;bool logged,wasActive;
        void LateUpdate(){
            if(active!=wasActive){wasActive=active;Debug.Log("Traigger: imagen de Traigger para la capa nativa "+(active?"activada":"desactivada")+(SystemInfo.supportsAsyncGPUReadback?"":" (sin lectura asíncrona)"));}
            if(!active||!source)return;
            // A 60 fps y a tres cuartos de la resolución de la pantalla: es la única copia de Traigger que se ve.
            if(Time.unscaledTime<next)return;next=Mathf.Max(next+1f/60,Time.unscaledTime-.5f/60);
            float scale=Mathf.Min(.75f,2048f/Mathf.Max(1,Screen.width));
            int width=Mathf.Max(64,Mathf.RoundToInt(Screen.width*scale)),height=Mathf.Max(64,Mathf.RoundToInt(Screen.height*scale));
            if(!target||target.width!=width||target.height!=height){if(target)target.Release();target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){name="Traigger Overlay"};target.Create();}
            // Fondo transparente solo para esta imagen; la pantalla y la cámara «Traigger» siguen como estaban.
            var previous=source.targetTexture;var background=source.backgroundColor;var mask=source.cullingMask;
            var clear=background;clear.a=0;source.backgroundColor=clear;source.cullingMask=mask&~(1<<MoviePlayer.Layer)&~CardsMask;
            source.targetTexture=target;source.Render();
            source.targetTexture=previous;source.backgroundColor=background;source.cullingMask=mask;source.ResetAspect();
            if(!SystemInfo.supportsAsyncGPUReadback||asyncErrors>3){ReadDirect(width,height);return;}
            if(pending>=2)return;pending++;
            AsyncGPUReadback.Request(target,0,TextureFormat.RGBA32,request=>{
                pending--;
                if(request.hasError){asyncErrors++;Debug.LogWarning("Traigger: falla la lectura asíncrona de la imagen ("+asyncErrors+")");return;}
                if(!active)return;
                var data=request.GetData<byte>();
                if(pixels==null||pixels.Length!=data.Length)pixels=new byte[data.Length];
                data.CopyTo(pixels);
                var handle=GCHandle.Alloc(pixels,GCHandleType.Pinned);
                try{TraiggerAvatarOverlayFrame(handle.AddrOfPinnedObject(),width,height);}finally{handle.Free();}
                if(!logged){logged=true;Debug.Log("Traigger: primer fotograma de la imagen de Traigger enviado ("+width+"x"+height+")");}
            });
        }
        void ReadDirect(int width,int height){
            if(!readback||readback.width!=width||readback.height!=height){if(readback)Destroy(readback);readback=new Texture2D(width,height,TextureFormat.RGBA32,false);}
            var previous=RenderTexture.active;RenderTexture.active=target;
            readback.ReadPixels(new Rect(0,0,width,height),0,0,false);
            RenderTexture.active=previous;
            var data=readback.GetRawTextureData<byte>();
            if(pixels==null||pixels.Length!=data.Length)pixels=new byte[data.Length];
            data.CopyTo(pixels);
            var handle=GCHandle.Alloc(pixels,GCHandleType.Pinned);
            try{TraiggerAvatarOverlayFrame(handle.AddrOfPinnedObject(),width,height);}finally{handle.Free();}
            if(!logged){logged=true;Debug.Log("Traigger: primer fotograma (lectura directa) enviado ("+width+"x"+height+")");}
        }
        void OnDestroy(){if(target)target.Release();if(readback)Destroy(readback);}
    }
}
