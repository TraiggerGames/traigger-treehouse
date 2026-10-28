using System.Collections.Generic;
using UnityEngine;
namespace Traigger {
    // Skins 3D experimentales: un modelo humanoide (p. ej. exportado de Meshy) que sustituye al dibujo 2D. El
    // estudio sigue calculando la pose como siempre (StudioController) y aquí se pasa a los huesos del modelo:
    // cabeza y cuello (girar, asentir, inclinar), torso, brazos y el balanceo. No hay animaciones ni Animator: los
    // huesos se colocan a mano cada fotograma partiendo de la pose de reposo del propio modelo (su «bind pose»), así
    // da igual en qué postura venga el archivo. Sin rig facial: la boca y los ojos no se mueven.
    public class Skin3DAvatar : MonoBehaviour {
        public const string Prefix="3d:";
        public static readonly (string id,string name)[] Available={("SkyPaw","Traigger 3D (experimental)")};
        public StudioController studio;
        Transform model;
        readonly Dictionary<string,Transform> bones=new Dictionary<string,Transform>();
        readonly List<(Transform bone,Vector3 position,Quaternion rotation)> rest=new List<(Transform,Vector3,Quaternion)>();
        Renderer[] hidden=new Renderer[0];
        float hideCheck,diagAt,mouthBlend;bool logged,mouthOpen;float armL,armR;
        Material material;SkinnedMeshRenderer face;int mouthClosedShape=-1,smileShape=-1,narrowShape=-1,fillShape=-1;
        // Muelles de la cola y las orejas (ángulo y velocidad), y lo que hicieron la cabeza y el cuerpo el fotograma anterior.
        float tailAngle,tailSpeed,tailLift,tailLiftSpeed,earL,earLSpeed,earR,earRSpeed,lastYaw,lastRoll,lastChest,twitchAt,twitchStart=-10,twitchSide;

        public static Skin3DAvatar Create(string id,StudioController studio,AvatarRig rig){
            var prefab=Resources.Load<GameObject>("Skins3D/"+id+"/"+id);
            if(!prefab)throw new System.Exception("No se encuentra el skin 3D «"+id+"»");
            var skin=rig.gameObject.AddComponent<Skin3DAvatar>();skin.studio=studio;skin.Build(prefab,id,rig);
            return skin;
        }

        void Build(GameObject prefab,string id,AvatarRig rig){
            // Tamaño y sitio del dibujo 2D, para que el 3D ocupe lo mismo en el encuadre.
            var bounds=new Bounds(rig.transform.position,Vector3.zero);bool any=false;
            foreach(var r in rig.GetComponentsInChildren<Renderer>()){if(!r.enabled)continue;if(!any){bounds=r.bounds;any=true;}else bounds.Encapsulate(r.bounds);}
            hidden=rig.GetComponentsInChildren<Renderer>();HideDrawing();

            model=Instantiate(prefab,rig.transform,false).transform;model.name="Skin 3D · "+id;
            var material=new Material(Shader.Find("Traigger/Skin3D")){name="Skin 3D · "+id};
            material.mainTexture=Resources.Load<Texture2D>("Skins3D/"+id+"/"+id+"_color");
            var normal=Resources.Load<Texture2D>("Skins3D/"+id+"/"+id+"_normal");if(normal)material.SetTexture("_BumpMap",normal);
            // Parpadeo por textura (Tools/Skins3D/closed.py): ojos cerrados pintados y la máscara de cada ojo.
            var closedEyes=Resources.Load<Texture2D>("Skins3D/"+id+"/"+id+"_closed");var blinkMask=Resources.Load<Texture2D>("Skins3D/"+id+"/"+id+"_blinkmask");
            if(closedEyes&&blinkMask){material.SetTexture("_ClosedTex",closedEyes);material.SetTexture("_BlinkMask",blinkMask);}
            this.material=material;
            SkinnedMeshRenderer skinned=null;
            foreach(var r in model.GetComponentsInChildren<Renderer>()){
                r.sharedMaterial=material;r.gameObject.layer=rig.gameObject.layer;
                if(r is SkinnedMeshRenderer s){s.updateWhenOffscreen=true;skinned??=s;}
                if(r is SkinnedMeshRenderer withShapes&&withShapes.sharedMesh&&withShapes.sharedMesh.GetBlendShapeIndex("MouthClosed")>=0){
                    face=withShapes;mouthClosedShape=withShapes.sharedMesh.GetBlendShapeIndex("MouthClosed");smileShape=withShapes.sharedMesh.GetBlendShapeIndex("Smile");narrowShape=withShapes.sharedMesh.GetBlendShapeIndex("MouthNarrow");fillShape=withShapes.sharedMesh.GetBlendShapeIndex("MouthFill");}
            }
            foreach(var t in model.GetComponentsInChildren<Transform>()){t.gameObject.layer=rig.gameObject.layer;bones[t.name]=t;}
            // Los huesos que de verdad deforman la malla mandan sobre otros nodos con el mismo nombre.
            if(skinned)foreach(var b in skinned.bones)if(b)bones[b.name]=b;
            Debug.Log("Traigger: skin 3D «"+id+"» con "+(skinned?skinned.bones.Length:0)+" huesos: "+(skinned?string.Join(", ",System.Array.ConvertAll(skinned.bones,b=>b?b.name:"-")):"sin malla con huesos"));
            // Pose de reposo: la del enlace de la malla con sus huesos (T o A), no el primer fotograma de la animación.
            if(skinned&&skinned.bones.Length==skinned.sharedMesh.bindposes.Length){
                var root=skinned.transform.localToWorldMatrix;var poses=skinned.sharedMesh.bindposes;
                var order=new List<int>();for(int i=0;i<skinned.bones.Length;i++)order.Add(i);
                order.Sort((a,b)=>Depth(skinned.bones[a]).CompareTo(Depth(skinned.bones[b])));   // padres antes que hijos
                foreach(int i in order){var bone=skinned.bones[i];if(!bone)continue;var world=root*poses[i].inverse;bone.SetPositionAndRotation(world.GetColumn(3),world.rotation);}
            }
            if(skinned)FixCollar(skinned);
            // Mirando a la cámara: el hueso «headfront» (delante de la cara) dice hacia dónde mira el modelo.
            if(bones.TryGetValue("headfront",out var front)&&bones.TryGetValue("Head",out var head)){
                var facing=front.position-head.position;facing.y=0;
                if(facing.sqrMagnitude>1e-8f)model.rotation=Quaternion.FromToRotation(facing.normalized,Vector3.back)*model.rotation;
            }else model.rotation=Quaternion.Euler(0,180,0)*model.rotation;
            // Escala y posición: misma altura que el dibujo, pies abajo del todo, centrado.
            var size=ModelBounds();
            float scale=any&&size.size.y>1e-4f?bounds.size.y/size.size.y:1;
            model.localScale*=scale;size=ModelBounds();
            model.position+=new Vector3(bounds.center.x-size.center.x,bounds.min.y-size.min.y,rig.transform.position.z-size.center.z);
            foreach(var t in model.GetComponentsInChildren<Transform>())if(t!=model)rest.Add((t,t.localPosition,t.localRotation));
        }
        // Meshy pesa el cuello de la camiseta al hueso Neck y se pega al cuello al girar la cabeza. Se pasa ese peso al
        // pecho en la base del cuello, con transición suave hacia arriba (la piel del cuello sigue al cuello).
        void FixCollar(SkinnedMeshRenderer skinned){
            var mesh=skinned.sharedMesh;if(!mesh.isReadable){Debug.LogWarning("Traigger: el skin 3D no es legible; no se corrige el cuello");return;}
            int neck=System.Array.FindIndex(skinned.bones,b=>b&&b.name=="Neck");
            int chest=System.Array.FindIndex(skinned.bones,b=>b&&b.name=="UpperChest");if(chest<0)chest=System.Array.FindIndex(skinned.bones,b=>b&&b.name=="Chest");
            int head=System.Array.FindIndex(skinned.bones,b=>b&&b.name=="Head");
            if(neck<0||chest<0||head<0)return;
            var poses=mesh.bindposes;
            Vector3 neckAt=poses[neck].inverse.GetColumn(3),headAt=poses[head].inverse.GetColumn(3); // en el espacio de la malla
            var axis=headAt-neckAt;float length=axis.magnitude;if(length<1e-5f)return;axis/=length;
            // Hacia dónde mira la cara, en el espacio de la malla (hueso «headfront», delante de la cabeza).
            int frontBone=System.Array.FindIndex(skinned.bones,b=>b&&b.name=="headfront");
            Vector3 forward=frontBone>=0?(Vector3)poses[frontBone].inverse.GetColumn(3)-headAt:Vector3.zero;
            // Si «headfront» no deforma la malla no está en la lista de la piel: se toma del nodo (ya en la pose de reposo).
            if(frontBone<0&&bones.TryGetValue("headfront",out var frontNode))forward=skinned.transform.InverseTransformPoint(frontNode.position)-headAt;
            forward-=axis*Vector3.Dot(forward,axis);forward=forward.sqrMagnitude>1e-10f?forward.normalized:Vector3.zero;
            var vertices=mesh.vertices;var weights=mesh.boneWeights;int changed=0,chinCount=0,withHead=0;
            for(int i=0;i<weights.Length;i++){
                float t=Vector3.Dot(vertices[i]-neckAt,axis)/length;   // 0 = base del cuello, 1 = cabeza
                Vector3 radial=vertices[i]-neckAt-axis*Vector3.Dot(vertices[i]-neckAt,axis);
                // Barbilla (delante y por encima de la base del cuello): todo su peso de cuello pasa a la cabeza, para que se
                // mueva solo con ella. Cuello de la camiseta (abajo, alrededor): el peso de cuello pasa al pecho.
                // Debajo de la barbilla Meshy puso casi el 60 % del peso en los hombros: al mover la cabeza esa piel se
                // quedaba atrás. Toda la piel de la barbilla (la que ya tiene algo de peso de cabeza; la camiseta no tiene) pasa
                // entera a la cabeza.
                float headShare=WeightOf(weights[i],head);
                float chinAmount=headShare>0.05f&&t>-.20f?Step01(.02f,.05f,Vector3.Dot(radial,forward)):0;
                float collarAmount=(1-Step01(-.05f,.45f,t))*(1-chinAmount);
                bool touched=false;
                if(headShare>0.05f)withHead++;
                if(chinAmount>0){chinCount++;touched|=AllTo(ref weights[i],head,chinAmount);}
                touched|=Transfer(ref weights[i],neck,chest,collarAmount);
                if(touched)changed++;
            }
            Debug.Log($"Traigger: skin 3D cuello: delante={forward:F2} eje={axis:F2} hueso frontal={(frontBone>=0)} vértices con cabeza={withHead} barbilla={chinCount}");
            if(changed==0)return;
            var copy=Instantiate(mesh);copy.name=mesh.name+" · cuello corregido";copy.boneWeights=weights;skinned.sharedMesh=copy;
            Debug.Log("Traigger: skin 3D, pesos del cuello corregidos en "+changed+" vértices (camiseta al pecho; barbilla y bajo la barbilla, a la cabeza)");
        }
        // Transición suave de 0 a 1 entre e0 y e1 (Mathf.SmoothStep NO es esto: interpola entre sus dos primeros números).
        static float Step01(float e0,float e1,float x){float t=Mathf.Clamp01((x-e0)/(e1-e0));return t*t*(3-2*t);}
        static float WeightOf(BoneWeight w,int bone)=>(w.boneIndex0==bone?w.weight0:0)+(w.boneIndex1==bone?w.weight1:0)+(w.boneIndex2==bone?w.weight2:0)+(w.boneIndex3==bone?w.weight3:0);
        // Lleva una fracción del peso de todos los demás huesos al hueso «to».
        static bool AllTo(ref BoneWeight w,int to,float amount){
            int[] ids={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] ws={w.weight0,w.weight1,w.weight2,w.weight3};
            int c=System.Array.IndexOf(ids,to);if(c<0)return false;
            float moved=0;for(int k=0;k<4;k++)if(k!=c){float give=ws[k]*amount;ws[k]-=give;moved+=give;}
            if(moved<=1e-5f)return false;ws[c]+=moved;
            w=new BoneWeight{boneIndex0=ids[0],boneIndex1=ids[1],boneIndex2=ids[2],boneIndex3=ids[3],weight0=ws[0],weight1=ws[1],weight2=ws[2],weight3=ws[3]};
            return true;
        }
        // Pasa una fracción del peso del hueso «from» al hueso «to» en un vértice (máximo 4 huesos, se conserva el total).
        static bool Transfer(ref BoneWeight w,int from,int to,float amount){
            if(amount<=0)return false;
            int[] ids={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] ws={w.weight0,w.weight1,w.weight2,w.weight3};
            int n=System.Array.IndexOf(ids,from);if(n<0||ws[n]<=0)return false;
            float give=ws[n]*amount;ws[n]-=give;
            int c=-1;for(int k=0;k<4;k++)if(ids[k]==to&&ws[k]>0)c=k;
            if(c<0){for(int k=0;k<4;k++)if(k!=n&&(c<0||ws[k]<ws[c]))c=k;give+=ws[c];ids[c]=to;ws[c]=0;}   // sin hueco: sustituye al de menos peso
            ws[c]+=give;
            w=new BoneWeight{boneIndex0=ids[0],boneIndex1=ids[1],boneIndex2=ids[2],boneIndex3=ids[3],weight0=ws[0],weight1=ws[1],weight2=ws[2],weight3=ws[3]};
            return true;
        }
        static int Depth(Transform t){int d=0;while(t.parent){d++;t=t.parent;}return d;}
        Bounds ModelBounds(){
            var renderers=model.GetComponentsInChildren<Renderer>();var b=renderers.Length>0?renderers[0].bounds:new Bounds(model.position,Vector3.one);
            foreach(var r in renderers)b.Encapsulate(r.bounds);return b;
        }
        void HideDrawing(){foreach(var r in hidden)if(r&&(!model||!r.transform.IsChildOf(model)))r.forceRenderingOff=true;}

        Transform Bone(string name)=>bones.TryGetValue(name,out var t)?t:null;
        static void Turn(Transform bone,float yaw,float pitch,float roll){
            if(!bone)return;
            // En ejes del mundo (la cámara mira a +Z): no depende de cómo vengan orientados los huesos en el archivo.
            bone.rotation=Quaternion.AngleAxis(roll,Vector3.forward)*Quaternion.AngleAxis(pitch,Vector3.right)*Quaternion.AngleAxis(-yaw,Vector3.up)*bone.rotation;
        }
        // Apunta un hueso (de él a su hijo) hacia una dirección del mundo.
        static void Aim(Transform bone,Transform child,Vector3 direction){
            if(!bone||!child)return;var current=child.position-bone.position;if(current.sqrMagnitude<1e-10f)return;
            bone.rotation=Quaternion.FromToRotation(current,direction)*bone.rotation;
        }
        static Vector3 ArmDirection(float side,float raise,float forward){
            float e=(18+Mathf.Clamp(raise,-10,150))*Mathf.Deg2Rad;   // 0° = brazo colgando
            return new Vector3(side*Mathf.Sin(e),-Mathf.Cos(e),-forward).normalized;
        }

        void LateUpdate(){
            if(!model||!studio)return;
            if(Time.unscaledTime>hideCheck){hideCheck=Time.unscaledTime+1;HideDrawing();}   // otros sistemas pueden volver a mostrar capas
            foreach(var r in rest){r.bone.localPosition=r.position;r.bone.localRotation=r.rotation;}
            var p=studio.Pose;
            // Torso y cadera: la inclinación del cuerpo y el balanceo lateral.
            Turn(Bone("Hips"),0,0,p.hips*.6f);
            Turn(Bone("Spine"),p.yaw*.10f,0,p.chest*.35f);
            Turn(Bone("Chest"),p.yaw*.10f,0,p.chest*.35f);
            Turn(Bone("UpperChest"),p.yaw*.05f,p.breathe*.6f,p.chest*.2f);
            // Cuello y cabeza se reparten el gesto (el 3D aguanta más recorrido arriba/abajo que el dibujo).
            float yaw=p.yaw*1.6f,pitch=p.pitch*1.8f,roll=p.roll;
            Turn(Bone("Neck"),yaw*.25f,pitch*.25f,roll*.25f);
            Turn(Bone("Head"),yaw*.55f,pitch*.75f,roll*.75f);
            // Brazos: colgando y un poco separados; el seguimiento los levanta. El de la izquierda de la pantalla son los
            // huesos «Right» del personaje (mira a la cámara).
            // Suavizado propio (los brazos 3D delatan cualquier temblor) con zona muerta para el ruido del seguimiento.
            float k=1-Mathf.Exp(-Time.deltaTime*6f);
            armL=Mathf.Lerp(armL,Mathf.Abs(p.armLeft)<4?0:p.armLeft,k);armR=Mathf.Lerp(armR,Mathf.Abs(p.armRight)<4?0:p.armRight,k);
            Aim(Bone("RightUpperArm"),Bone("RightLowerArm"),ArmDirection(-1,armL,.12f));
            Aim(Bone("RightLowerArm"),Bone("RightHand"),ArmDirection(-1,armL*.8f,.35f));
            Aim(Bone("LeftUpperArm"),Bone("LeftLowerArm"),ArmDirection(1,armR,.12f));
            Aim(Bone("LeftLowerArm"),Bone("LeftHand"),ArmDirection(1,armR*.8f,.35f));
            if(!logged){logged=true;var arm=Bone("RightUpperArm");var low=Bone("RightLowerArm");Debug.Log("Traigger: skin 3D brazo "+(arm&&low?(low.position-arm.position).normalized.ToString("F2"):"no encontrado"));}
            // Cara. La boca pintada está abierta: cerrada del todo va por textura (boca de gato «ω», Tools/Skins3D/closed.py)
            // y entre medias los labios se juntan con la forma «MouthClosed» (como mucho al 60 %, sin tocar la barbilla).
            // Ojos: parpadeo por textura, nítido (sin quedarse a medias, que se veía translúcido).
            // Umbral con histéresis: el seguimiento lee la mandíbula algo abierta en reposo (0,2–0,35); la boca se abre
            // de verdad pasado 0,4 y no vuelve a cerrarse hasta bajar de 0,25.
            mouthOpen=mouthOpen?p.mouth>0.25f:p.mouth>0.40f;
            float m=mouthOpen?Mathf.Clamp01((p.mouth-0.25f)/0.6f):0;
            mouthBlend=Mathf.MoveTowards(mouthBlend,mouthOpen?1:0,Time.deltaTime*14f);   // cambio rápido pero sin parpadeos
            float textureClosed=1-mouthBlend;
            float squeeze=0.6f*(1-Step01(0f,0.8f,m))*mouthBlend;
            if(face){
                if(mouthClosedShape>=0)face.SetBlendShapeWeight(mouthClosedShape,squeeze*100);
                if(smileShape>=0)face.SetBlendShapeWeight(smileShape,Mathf.Clamp01(p.smile)*100);
                // La boca es un hueco con la lengua al fondo: cerrada, se rellena hasta el morro (si no, de lado se ve el hueco).
                if(fillShape>=0)face.SetBlendShapeWeight(fillShape,textureClosed*100);
                if(narrowShape>=0)face.SetBlendShapeWeight(narrowShape,Mathf.Clamp01(-p.mouthShape)*m*100);   // «o/u» al hablar
            }else Turn(Bone("Head"),0,-p.mouth*3f,0);   // sin formas: un pequeño cabeceo al hablar
            if(Time.unscaledTime>diagAt){diagAt=Time.unscaledTime+3;Debug.Log($"Traigger: skin 3D cara: formas {(face?"sí":"no")} (cerrada {mouthClosedShape}, sonrisa {smileShape}, «o» {narrowShape}), boca {p.mouth:0.00} → textura cerrada {textureClosed:0.00}, labios {squeeze:0.00}, parpadeo {p.blinkLeft:0.00}/{p.blinkRight:0.00}");}
            if(material){
                material.SetFloat("_MouthClosed",face?textureClosed:0);
                material.SetFloat("_BlinkR",Step01(0.35f,0.7f,p.blinkRight));material.SetFloat("_BlinkL",Step01(0.35f,0.7f,p.blinkLeft));
            }
            Physics(p);
        }

        // Cola y orejas con muelles amortiguados: reaccionan a los giros de la cabeza y del cuerpo y, en reposo, la cola
        // se mece despacio y de vez en cuando una oreja se sacude. Giros en ejes del mundo, repartidos por la cadena.
        void Physics(StudioController.PoseState p){
            float dt=Mathf.Clamp(Time.deltaTime,0.001f,0.05f),t=Time.time;
            float yawSpeed=(p.yaw-lastYaw)/dt,rollSpeed=(p.roll-lastRoll)/dt,chestSpeed=(p.chest-lastChest)/dt;lastYaw=p.yaw;lastRoll=p.roll;lastChest=p.chest;
            void Spring(ref float x,ref float v,float target,float stiffness,float damping){v+=(stiffness*(target-x)-damping*v)*dt;x+=v*dt;}
            // Cola: vaivén lento + inercia del cuerpo (va por detrás de los giros).
            float wag=Mathf.Sin(t*1.7f)*7f+Mathf.Sin(t*0.63f)*3f;
            Spring(ref tailAngle,ref tailSpeed,wag-Mathf.Clamp(yawSpeed*.12f+chestSpeed*.4f,-25,25),40,7);
            Spring(ref tailLift,ref tailLiftSpeed,Mathf.Clamp(p.mouth*6f+p.smile*8f,0,14),30,8);   // contento o hablando: un poco más alta
            for(int k=1;k<=5;k++){var b=Bone("Tail"+k);if(!b)break;float share=.12f+.05f*k;Turn(b,tailAngle*share,tailLift*share*.6f,0);}
            // Orejas: inercia de la cabeza y un tic ocasional.
            // Tic: cada 3–9 s una oreja da un golpe rápido (el muelle hace el resto).
            if(t>twitchAt){twitchStart=t;twitchAt=t+Random.Range(3f,9f);twitchSide=Random.value<.5f?-1:1;}
            float pulse=t-twitchStart<0.12f?14f:0;
            Spring(ref earL,ref earLSpeed,Mathf.Clamp(-rollSpeed*.05f-yawSpeed*.03f,-18,18)+(twitchSide>0?pulse:0),70,9);
            Spring(ref earR,ref earRSpeed,Mathf.Clamp(-rollSpeed*.05f+yawSpeed*.03f,-18,18)+(twitchSide<0?pulse:0),70,9);
            Turn(Bone("LeftEar"),0,0,-earL);Turn(Bone("RightEar"),0,0,earR);
        }

        void OnDestroy(){
            foreach(var r in hidden)if(r)r.forceRenderingOff=false;
            if(model){foreach(var r in model.GetComponentsInChildren<Renderer>())if(r.sharedMaterial)Destroy(r.sharedMaterial);Destroy(model.gameObject);}
        }
    }
}
