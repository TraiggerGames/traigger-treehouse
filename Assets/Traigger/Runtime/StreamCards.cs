using System;
using UnityEngine;
namespace Traigger {
    // Carteles para directos y llamadas: «Vuelvo enseguida», «Empezamos pronto», «Pausa», «Fin» o un texto propio,
    // con color o una imagen de fondo. Se dibujan en la imagen de Unity, así salen igual en pantalla, en la cámara
    // «Traigger» del Mac y en la emisión. Van entre el contenido y Traigger (que sigue visible encima) o, si se pide,
    // por delante de todo. Capa 30: no se oculta al esconder al personaje ni entra en la imagen recortada de Traigger.
    public class StreamCards : MonoBehaviour {
        public const int Layer=30;
        public Camera target;
        // «titleMedia»: el título como imagen (PNG con transparencia) o vídeo (.mov HEVC con alfa / ProRes 4444) en vez
        // de texto. «image» (el fondo) también puede ser un vídeo.
        [Serializable]public class Card{public string kind="",title="",subtitle="",image="",titleMedia="";public Color color=new Color(.06f,.07f,.12f,.92f);public bool coverAvatar;}
        public Card current;
        public bool Showing=>current!=null&&!string.IsNullOrEmpty(current.kind);
        Transform root;Material panelMaterial,imageMaterial,titleMaterial;Transform panel,picture,titlePicture;TextMesh title,subtitle;
        readonly CardMedia background=new CardMedia(1),titleMedia=new CardMedia(0);

        public static Card Preset(string kind)=>kind switch{
            "brb"=>new Card{kind=kind,title="Vuelvo enseguida",subtitle="No te vayas"},
            "soon"=>new Card{kind=kind,title="Empezamos pronto",subtitle="Ponte cómodo"},
            "pause"=>new Card{kind=kind,title="Pausa",subtitle="Volvemos en unos minutos"},
            "end"=>new Card{kind=kind,title="¡Gracias por venir!",subtitle="Hasta la próxima"},
            _=>new Card{kind=string.IsNullOrEmpty(kind)?"":"custom"}
        };
        public void Show(Card card){current=card;Apply();}
        public void Hide(){current=null;Apply();}

        void Build(){
            root=new GameObject("Traigger Cards").transform;root.SetParent(target.transform,false);root.gameObject.layer=Layer;
            var mesh=new Mesh{vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(.5f,-.5f),new Vector3(-.5f,.5f),new Vector3(.5f,.5f)},
                uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)},triangles=new[]{0,2,1,2,3,1}};
            Transform Quad(string name,out Material material){
                var go=new GameObject(name){layer=Layer};go.transform.SetParent(root,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                material=new Material(Shader.Find("Traigger/UnlitAlpha"));
                go.AddComponent<MeshRenderer>().sharedMaterial=material;
                return go.transform;
            }
            panel=Quad("Panel",out panelMaterial);picture=Quad("Imagen",out imageMaterial);titlePicture=Quad("Título (imagen)",out titleMaterial);
            var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            TextMesh Text(string name,int size){
                var go=new GameObject(name){layer=Layer};go.transform.SetParent(root,false);
                var text=go.AddComponent<TextMesh>();text.font=font;text.fontSize=size;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.color=Color.white;
                go.GetComponent<MeshRenderer>().sharedMaterial=font.material;
                return text;
            }
            title=Text("Título",96);subtitle=Text("Subtítulo",56);
        }
        void Apply(){
            if(!target)return;
            if(!Showing){if(root)root.gameObject.SetActive(false);return;}
            if(!root)Build();
            root.gameObject.SetActive(true);
            title.text=current.title??"";subtitle.text=current.subtitle??"";
            panelMaterial.color=current.color;
            background.Load(current.image);titleMedia.Load(current.titleMedia);
            bool mediaTitle=titleMedia.Active;
            title.gameObject.SetActive(!mediaTitle);
            // Entre el contenido (≈ −32400) y Traigger, o por delante de todo.
            int order=current.coverAvatar?32000:-31500; // delante de la película (−32000) y la capturadora
            panel.GetComponent<MeshRenderer>().sortingOrder=order;picture.GetComponent<MeshRenderer>().sortingOrder=order+1;titlePicture.GetComponent<MeshRenderer>().sortingOrder=order+2;
            title.GetComponent<MeshRenderer>().sortingOrder=order+2;subtitle.GetComponent<MeshRenderer>().sortingOrder=order+2;
        }
        void OnDestroy(){background.Load(null);titleMedia.Load(null);}
        void LateUpdate(){
            if(!Showing||!root||!target){if(!Showing){background.Load(null);titleMedia.Load(null);}return;}
            float height=target.orthographicSize*2,width=height*target.aspect;
            float depth=current.coverAvatar?target.nearClipPlane+.05f:target.farClipPlane*.85f;
            root.localPosition=new Vector3(0,0,depth);root.localRotation=Quaternion.identity;root.localScale=Vector3.one;
            panel.localScale=new Vector3(width,height,1);
            // Fondo (imagen o vídeo): llena el cartel recortando lo que sobre.
            var back=background.Texture();picture.gameObject.SetActive(back);imageMaterial.mainTexture=back;
            if(back){float a=(float)back.width/back.height,w=width/height>a?width:height*a;picture.localScale=new Vector3(w,w/a*(background.FlipY?-1:1),1);}
            // Título como imagen o vídeo: centrado, como mucho el 70 % del ancho y el 35 % del alto, sin deformar.
            var art=titleMedia.Texture();titlePicture.gameObject.SetActive(art);titleMaterial.mainTexture=art;
            if(art){
                float a=(float)art.width/art.height,h=Mathf.Min(height*.35f,width*.7f/a);
                titlePicture.localScale=new Vector3(h*a,h*(titleMedia.FlipY?-1:1),1);titlePicture.localPosition=new Vector3(0,height*.06f,-.005f);
            }
            // Texto: el título grande en el centro, el subtítulo debajo (tamaño relativo a la pantalla).
            float unit=height/(title.fontSize*.1f);
            title.transform.localScale=Vector3.one*unit*.11f;title.transform.localPosition=new Vector3(0,height*.06f,-.01f);
            subtitle.transform.localScale=Vector3.one*unit*.065f;subtitle.transform.localPosition=new Vector3(0,-height*.1f,-.01f);
        }
    }
}

namespace Traigger {
    // Imagen (PNG/JPG, con transparencia) o vídeo en bucle (Plugins/Apple/TraiggerClip.swift) como textura de un cartel.
    public class CardMedia {
        readonly int slot;string path="";Texture2D texture;byte[] pixels;long shown=-1;bool video;
        public CardMedia(int slot){this.slot=slot;}
        public bool Active=>!string.IsNullOrEmpty(path);
        public bool FlipY=>video; // los vídeos llegan de arriba abajo
        static bool IsVideo(string p){var e=System.IO.Path.GetExtension(p).ToLowerInvariant();return e==".mov"||e==".mp4"||e==".m4v";}
#if (UNITY_IOS || UNITY_VISIONOS) && !UNITY_EDITOR
        const string Lib="__Internal";
#else
        const string Lib="TraiggerMoviePlayer";
#endif
#if UNITY_IOS || UNITY_VISIONOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        [System.Runtime.InteropServices.DllImport(Lib)]static extern int TraiggerClipOpen(int slot,string path);
        [System.Runtime.InteropServices.DllImport(Lib)]static extern void TraiggerClipClose(int slot);
        [System.Runtime.InteropServices.DllImport(Lib)]static extern long TraiggerClipFrameInfo(int slot,out int w,out int h);
        [System.Runtime.InteropServices.DllImport(Lib)]static extern int TraiggerClipCopyFrame(int slot,IntPtr destination,int capacity);
#else
        static int TraiggerClipOpen(int slot,string path)=>0;static void TraiggerClipClose(int slot){}static long TraiggerClipFrameInfo(int slot,out int w,out int h){w=h=0;return -1;}static int TraiggerClipCopyFrame(int s,IntPtr d,int c)=>0;
#endif
        public void Load(string newPath){
            newPath??="";if(newPath==path)return;
            if(video)try{TraiggerClipClose(slot);}catch(Exception){}
            if(texture){UnityEngine.Object.Destroy(texture);texture=null;}
            path=newPath;video=false;shown=-1;
            if(path.Length==0||!System.IO.File.Exists(path)){path="";return;}
            if(IsVideo(path)){
                try{video=TraiggerClipOpen(slot,path)==1;}catch(Exception e){Debug.LogWarning("Traigger: no se puede abrir el vídeo del cartel: "+e.Message);}
                if(!video)path="";
            }else{
                texture=new Texture2D(2,2,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
                if(!texture.LoadImage(System.IO.File.ReadAllBytes(path))){UnityEngine.Object.Destroy(texture);texture=null;path="";}
            }
        }
        // Textura lista para dibujar (en vídeo, con el último fotograma).
        public Texture2D Texture(){
            if(!video)return texture;
            long serial=TraiggerClipFrameInfo(slot,out int w,out int h);
            if(serial<0||w<=0||h<=0)return texture;
            if(serial!=shown){
                if(!texture||texture.width!=w||texture.height!=h){if(texture)UnityEngine.Object.Destroy(texture);texture=new Texture2D(w,h,TextureFormat.BGRA32,false){wrapMode=TextureWrapMode.Clamp};pixels=new byte[w*h*4];}
                var handle=System.Runtime.InteropServices.GCHandle.Alloc(pixels,System.Runtime.InteropServices.GCHandleType.Pinned);
                try{if(TraiggerClipCopyFrame(slot,handle.AddrOfPinnedObject(),pixels.Length)==1){texture.LoadRawTextureData(pixels);texture.Apply(false);}}finally{handle.Free();}
                shown=serial;
            }
            return texture;
        }
    }
}
