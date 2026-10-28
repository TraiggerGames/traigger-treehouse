using System.IO;
using UnityEngine;
namespace Traigger {
    // Imagen de fondo detrás de Traigger (y de la película), llenando el encuadre. Va en la capa de la película, así se
    // ve aunque se oculte al personaje, y la cámara «Traigger» la incluye. La elige VisionBackground.SetImage.
    public class BackgroundImage : MonoBehaviour {
        public Camera target;
        Transform screen;Material material;Texture2D texture;
        void OnEnable(){VisionBackground.ImageChanged+=Load;if(!string.IsNullOrEmpty(VisionBackground.Image))Load(VisionBackground.Image);}
        void OnDisable(){VisionBackground.ImageChanged-=Load;if(screen)screen.gameObject.SetActive(false);}
        void Load(string path){
            if(texture){Destroy(texture);texture=null;}
            path=Locate(path);
            if(string.IsNullOrEmpty(path)){if(screen)screen.gameObject.SetActive(false);return;}
            texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name="Traigger Background",wrapMode=TextureWrapMode.Clamp};
            if(!texture.LoadImage(File.ReadAllBytes(path))){Debug.LogWarning("Traigger: no se pudo leer la imagen de fondo (usa JPG o PNG): "+path);return;}
            if(!screen){
                var go=new GameObject("Traigger Background");go.layer=MoviePlayer.Layer;
                var mesh=new Mesh{vertices=new[]{new Vector3(-.5f,-.5f),new Vector3(.5f,-.5f),new Vector3(-.5f,.5f),new Vector3(.5f,.5f)},
                    uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)},triangles=new[]{0,2,1,2,3,1}};
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                material=new Material(Shader.Find("Traigger/UnlitAlpha"));
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.sortingOrder=-32500;
                screen=go.transform;screen.SetParent(target.transform,false);
            }
            material.mainTexture=texture;screen.gameObject.SetActive(true);
        }
        // La carpeta de la app cambia al reinstalar: si la ruta guardada ya no existe, se busca por nombre en «Fondos».
        static string Locate(string path){
            if(string.IsNullOrEmpty(path))return null;
            if(File.Exists(path))return path;
            string name=Path.GetFileName(path),support=Path.Combine(Path.GetDirectoryName(Application.persistentDataPath)??"","Library","Application Support","Fondos",name);
            if(File.Exists(support))return support;
            Debug.LogWarning("Traigger: no encuentro la imagen de fondo: "+path);return null;
        }
        void LateUpdate(){
            if(!screen||!texture||!target)return;
            // Rellena el encuadre sin deformar (recortando lo que sobre).
            float viewHeight=target.orthographicSize*2,viewWidth=viewHeight*target.aspect,aspect=(float)texture.width/texture.height;
            float w=viewWidth/viewHeight>aspect?viewWidth:viewHeight*aspect,h=w/aspect;
            screen.localPosition=new Vector3(0,0,target.farClipPlane*.95f);screen.localRotation=Quaternion.identity;screen.localScale=new Vector3(w,h,1);
        }
    }
}
