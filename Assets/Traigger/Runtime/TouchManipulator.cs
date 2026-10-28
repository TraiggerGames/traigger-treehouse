using System.Collections.Generic;
using UnityEngine;
namespace Traigger {
    // Agarrar y escalar al personaje con los dedos (o con el ratón en Mac).
    // Un dedo que empieza sobre el personaje lo arrastra. Dos dedos lo escalan con el pellizco y lo mueven con
    // su punto medio, manteniendo bajo los dedos el mismo punto del personaje. Con ratón: arrastrar con el botón
    // izquierdo sobre el personaje y rueda para escalar alrededor del cursor.
    // Sólo calcula offset y scale (en el espacio del padre del rig); StudioController los aplica cada fotograma.
    public class TouchManipulator : MonoBehaviour {
        public Camera avatarCamera;
        public Transform target;            // rig del avatar
        public Vector2 offset;              // desplazamiento del usuario, en unidades del padre del rig
        public float scale=1;
        [Range(.1f,1)]public float minScale=.3f;
        [Range(1,6)]public float maxScale=3f;
        public float hitMargin=.25f;        // margen alrededor de los huesos para aceptar el toque
        public bool remember=true;          // guarda posición y tamaño entre sesiones
        public System.Func<Vector2,bool> blockedAt;   // zonas de interfaz que no deben iniciar un gesto
        public bool dragging {get;private set;}
        public bool gesturesLocked {get;private set;}
        const string Key="traigger.manipulator.";
        readonly Dictionary<int,Vector2> active=new Dictionary<int,Vector2>();
        Vector2 grabWorld,grabOffset;float pinchStartDistance,pinchStartScale;Vector2 pinchAnchorLocal;
        bool pinching,mouseDrag;
        void Awake(){
            gesturesLocked=PlayerPrefs.GetInt(Key+"locked",Application.platform==RuntimePlatform.VisionOS?1:0)!=0;
            if(remember&&PlayerPrefs.HasKey(Key+"scale")){
                offset=new Vector2(PlayerPrefs.GetFloat(Key+"x"),PlayerPrefs.GetFloat(Key+"y"));
                scale=Mathf.Clamp(PlayerPrefs.GetFloat(Key+"scale",1),minScale,maxScale);
            }
        }
        // Cambiar el bloqueo cancela también un gesto que ya estaba en curso.
        public void SetGesturesLocked(bool locked){gesturesLocked=locked;CancelGestures();PlayerPrefs.SetInt(Key+"locked",locked?1:0);PlayerPrefs.Save();}
        void CancelGestures(){active.Clear();dragging=pinching=mouseDrag=false;}
        void OnDisable(){CancelGestures();}
        public void ResetView(){MoveTo(Vector2.zero,1);}
        // Lleva al personaje a una posición y tamaño con un deslizamiento suave (botones «apartar a Traigger»).
        Vector2 moveFrom,moveTo;float scaleFrom,scaleTo,moveTime=-1;
        public void MoveTo(Vector2 newOffset,float newScale){moveFrom=offset;scaleFrom=scale;moveTo=newOffset;scaleTo=Mathf.Clamp(newScale,minScale,maxScale);moveTime=0;}
        void Glide(){
            if(moveTime<0)return;
            if(dragging||pinching){moveTime=-1;return;}
            moveTime=Mathf.Min(1,moveTime+Time.unscaledDeltaTime/.6f);float k=Mathf.SmoothStep(0,1,moveTime);
            offset=Vector2.Lerp(moveFrom,moveTo,k);scale=Mathf.Lerp(scaleFrom,scaleTo,k);
            if(moveTime>=1){moveTime=-1;Save();}
        }
        void Save(){if(!remember)return;PlayerPrefs.SetFloat(Key+"x",offset.x);PlayerPrefs.SetFloat(Key+"y",offset.y);PlayerPrefs.SetFloat(Key+"scale",scale);PlayerPrefs.Save();}
        Transform Space=>target&&target.parent?target.parent:null;
        Vector2 ToSpace(Vector2 screen){
            var world=avatarCamera.ScreenToWorldPoint(new Vector3(screen.x,screen.y,Mathf.Abs(avatarCamera.transform.position.z)));
            return Space?(Vector2)Space.InverseTransformPoint(world):(Vector2)world;
        }
        // Rectángulo del personaje en pantalla: los huesos del rig más un margen.
        // Zona del personaje en pantalla (0–1, origen arriba a la izquierda): los huesos más el margen de toque.
        public Rect ViewportRect(){
            if(!target||!avatarCamera)return Rect.zero;
            var rig=target.GetComponent<AvatarRig>();if(!rig||rig.bones==null)return Rect.zero;
            var min=new Vector2(float.MaxValue,float.MaxValue);var max=-min;
            foreach(var b in rig.bones){if(!b)continue;min=Vector2.Min(min,b.position);max=Vector2.Max(max,b.position);}
            float margin=hitMargin*target.lossyScale.x;
            Vector3 a=avatarCamera.WorldToViewportPoint(new Vector3(min.x-margin,min.y-margin,0)),c=avatarCamera.WorldToViewportPoint(new Vector3(max.x+margin,max.y+margin,0));
            return Rect.MinMaxRect(a.x,1-c.y,c.x,1-a.y);
        }
        public bool OverCharacter(Vector2 screen){
            if(!target||!avatarCamera)return false;
            var rig=target.GetComponent<AvatarRig>();if(!rig||rig.bones==null)return false;
            Vector2 p=avatarCamera.ScreenToWorldPoint(new Vector3(screen.x,screen.y,Mathf.Abs(avatarCamera.transform.position.z)));
            var min=new Vector2(float.MaxValue,float.MaxValue);var max=-min;
            foreach(var b in rig.bones){if(!b)continue;min=Vector2.Min(min,b.position);max=Vector2.Max(max,b.position);}
            float margin=hitMargin*target.lossyScale.x;
            return p.x>min.x-margin&&p.x<max.x+margin&&p.y>min.y-margin&&p.y<max.y+margin;
        }
        void Update(){
            Glide();
            if(gesturesLocked){CancelGestures();return;}
            if(!avatarCamera||!target)return;
            if(Input.touchSupported&&Input.touchCount>0)UpdateTouches();
            else UpdateMouse();
        }
        void UpdateTouches(){
            for(int i=0;i<Input.touchCount;i++){
                var t=Input.GetTouch(i);
                if(t.phase==TouchPhase.Began){
                    bool blocked=blockedAt!=null&&blockedAt(t.position);
                    // Un dedo debe empezar sobre el personaje; el segundo dedo puede caer en cualquier sitio.
                    if(!blocked&&(active.Count>0||OverCharacter(t.position)))active[t.fingerId]=t.position;
                }else if(active.ContainsKey(t.fingerId)){
                    if(t.phase==TouchPhase.Ended||t.phase==TouchPhase.Canceled)active.Remove(t.fingerId);
                    else active[t.fingerId]=t.position;
                }
            }
            Apply();
        }
        // El personaje no puede salir del todo de la pantalla: su base queda dentro del encuadre de la cámara.
        void KeepVisible(){
            if(!avatarCamera||!avatarCamera.orthographic)return;
            Vector2 center=Space?(Vector2)Space.InverseTransformPoint(avatarCamera.transform.position):(Vector2)avatarCamera.transform.position;
            float halfH=avatarCamera.orthographicSize,halfW=halfH*avatarCamera.aspect;
            Vector2 root=RestPosition+offset;
            root.x=Mathf.Clamp(root.x,center.x-halfW,center.x+halfW);
            root.y=Mathf.Clamp(root.y,center.y-halfH-3.5f*scale,center.y+halfH-1.6f*scale); // se ve al menos la cabeza o las piernas
            offset=root-RestPosition;
        }
        void LateUpdate(){if(!gesturesLocked)KeepVisible();}
        void Apply(){
            int count=active.Count;
            if(count==0){if(dragging||pinching)Save();dragging=pinching=false;return;}
            var e=active.Values.GetEnumerator();e.MoveNext();Vector2 a=e.Current;
            if(count==1){
                if(pinching||!dragging){pinching=false;dragging=true;grabWorld=ToSpace(a);grabOffset=offset;}
                offset=grabOffset+ToSpace(a)-grabWorld;
            }else{
                e.MoveNext();Vector2 b=e.Current;Vector2 mid=(a+b)*.5f;float distance=Vector2.Distance(a,b);
                if(!pinching){pinching=true;dragging=true;pinchStartDistance=Mathf.Max(1,distance);pinchStartScale=scale;
                    // Punto del personaje bajo el centro del pellizco, en coordenadas sin escala.
                    pinchAnchorLocal=(ToSpace(mid)-offset-RestPosition)/scale;}
                scale=Mathf.Clamp(pinchStartScale*distance/pinchStartDistance,minScale,maxScale);
                offset=ToSpace(mid)-RestPosition-pinchAnchorLocal*scale;
            }
        }
        // Posición de reposo del rig (la fija el controlador); el escalado se hace alrededor de ella.
        public Vector2 RestPosition;
        void UpdateMouse(){
            Vector2 mouse=Input.mousePosition;
            bool inside=mouse.x>=0&&mouse.y>=0&&mouse.x<=Screen.width&&mouse.y<=Screen.height;
            if(Input.GetMouseButtonDown(0)&&inside&&!(blockedAt!=null&&blockedAt(mouse))&&OverCharacter(mouse)){mouseDrag=true;dragging=true;grabWorld=ToSpace(mouse);grabOffset=offset;}
            if(mouseDrag&&Input.GetMouseButton(0))offset=grabOffset+ToSpace(mouse)-grabWorld;
            if(mouseDrag&&Input.GetMouseButtonUp(0)){mouseDrag=false;dragging=false;Save();}
            float wheel=Input.mouseScrollDelta.y;
            if(Mathf.Abs(wheel)>.01f&&inside&&!(blockedAt!=null&&blockedAt(mouse))){
                Vector2 anchor=(ToSpace(mouse)-offset-RestPosition)/scale;
                scale=Mathf.Clamp(scale*Mathf.Pow(1.1f,wheel),minScale,maxScale);
                offset=ToSpace(mouse)-RestPosition-anchor*scale;Save();
            }
        }
        // Para pruebas: simula un gesto con puntos de pantalla (id → posición). Una lista vacía suelta los dedos.
        public void SimulateTouches(Dictionary<int,Vector2> touches){
            if(gesturesLocked){CancelGestures();return;}
            foreach(var id in new List<int>(active.Keys))if(!touches.ContainsKey(id))active.Remove(id);
            foreach(var kv in touches)if(active.ContainsKey(kv.Key)||active.Count>0||OverCharacter(kv.Value))active[kv.Key]=kv.Value;
            Apply();
        }
    }
}
