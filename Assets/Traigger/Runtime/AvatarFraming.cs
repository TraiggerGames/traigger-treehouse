using UnityEngine;
namespace Traigger {
    // Encuadre de una sola acción: mide la geometría dibujada, no los enormes bounds de seguridad del rig.
    // Después conserva el encuadre y sólo lo adapta si cambia el tamaño de ventana o se oculta el panel.
    [DefaultExecutionOrder(850)] public class AvatarFraming:MonoBehaviour {
        public StudioController studio;
        public bool Active {get;private set;}
        public bool faceView {get;private set;}
        public Bounds framedBounds {get;private set;}
        bool pending;int requestedFrame;Rect lastRect;int width,height;
        public void Center(bool face){
            faceView=face;pending=true;requestedFrame=Time.frameCount;
            if(studio&&studio.manipulator)studio.manipulator.ResetView();
        }
        void LateUpdate(){
            if(!studio||!studio.rig||!studio.avatarCamera)return;
            bool refit=false;
            if(pending&&Time.frameCount>requestedFrame){framedBounds=Measure(studio.rig,faceView);Active=true;pending=false;refit=true;}
            if(!Active)return;
            var camera=studio.avatarCamera;Rect viewport=studio.AvatarViewport;
            bool changed=camera.rect!=viewport||width!=Screen.width||height!=Screen.height||lastRect!=viewport;
            if(changed)camera.rect=viewport;
            // Aplica el encuadre sólo al activar/cambiar formato: no persigue la respiración ni los gestos.
            if(changed||refit){Apply(camera,camera.aspect);lastRect=viewport;width=Screen.width;height=Screen.height;}
        }
        public void Apply(Camera camera,float aspect){
            if(!Active||!camera)return;
            camera.orthographic=true;camera.transform.rotation=Quaternion.identity;
            var center=framedBounds.center;camera.transform.position=new Vector3(center.x,center.y,camera.transform.position.z);
            camera.orthographicSize=Mathf.Max(.1f,Mathf.Max(framedBounds.extents.y,framedBounds.extents.x/Mathf.Max(.1f,aspect))*1.12f);
        }
        public static Bounds Measure(AvatarRig rig,bool face){
            bool found=false;Bounds result=new Bounds();var baked=new Mesh();
            try{foreach(var renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>()){
                int layer=System.Array.IndexOf(rig.layers,renderer);if(!renderer||!renderer.enabled||!renderer.sharedMesh)continue;
                if(face&&layer<0)continue;
                string name=layer>=0?rig.definition.layers[layer].name:renderer.name;
                if(face&&name!="HeadBase"&&name!="FrontHair"&&name!="BackHair"&&name!="EarLeft"&&name!="EarRight"&&name!="EyeLeft"&&name!="EyeRight"&&name!="CheekLeft"&&name!="CheekRight")continue;
                renderer.BakeMesh(baked);var vertices=baked.vertices;
                // Las manos tienen vértices no usados: sólo participan los índices que se dibujan.
                foreach(int index in baked.triangles){Vector3 point=renderer.transform.TransformPoint(vertices[index]);if(!found){result=new Bounds(point,Vector3.zero);found=true;}else result.Encapsulate(point);}
            }}finally{if(Application.isPlaying)Destroy(baked);else DestroyImmediate(baked);}
            if(!found)result=new Bounds(rig.transform.position+Vector3.up*2.5f,new Vector3(3,5,0));
            if(face){var lower=result.min;lower.y-=result.size.y*.30f;result.Encapsulate(lower);}
            return result;
        }
    }
}
