using System;
using System.Collections.Generic;
using UnityEngine;
namespace Traigger {
    // Cuerpo dibujado por piezas para animación: torso, mangas y perneras no comparten una sábana de pesos.
    // La cabeza original y la hoja del usuario permanecen intactas. Build se ejecuta desde el editor.
    // Cada pieza conserva su proporción original: los huesos se colocan donde caen las articulaciones
    // del dibujo, en vez de estirar el dibujo hasta los huesos. El cuerpo de cada pieza es rígido; sólo
    // una franja estrecha en hombro, codo y muñeca sigue a los dos huesos, igual en las dos piezas que
    // se solapan, y el extremo de la pieza de encima se desvanece: el brazo se dobla de una pieza.
    [ExecuteAlways]
    public class KemonoBodyRig : MonoBehaviour {
        // scale: unidades por píxel (cada pieza se generó a su propia escala). prox/dist: articulaciones
        // en coordenadas normalizadas de la celda (u a la derecha, v hacia abajo). order: capa de dibujo.
        [Serializable] public class ArtPart {public string name,texture;public float x,y,width,height,pixelWidth,pixelHeight,ax,ay,bx,by,scale;public Vector2 prox,dist;public int order;}
        // Encaje global: cuello del torso, hombro y cadera (lado izquierdo de la pantalla; el otro es simétrico)
        // y ángulos de reposo desde la vertical, hacia fuera.
        // Franjas de unión (unidades a lo largo del eje, 0 en la articulación): de dónde a dónde pasa el peso
        // al hueso siguiente y dónde se desvanece el extremo de la pieza de encima.
        [Serializable] public class Fit {public Vector2 neck,shoulder,pelvisAt,hip;public float armAngle,foreAngle,legAngle,calfAngle,
            shoulderBlendFrom,shoulderBlendTo,elbowBlend,elbowFadeFrom,elbowFadeTo,
            shoulderRadius,elbowRadius,shoulderFadeFrom,shoulderFadeTo,shoulderArmBlendTo,
            // Límites anatómicos en grados desde el reposo (0 = sin límite). "Fuera" aleja el miembro del cuerpo.
            shoulderRaiseLimit,shoulderInwardLimit,shrugLimit,elbowLimit,wristLimit,hipOutwardLimit,hipInwardLimit,kneeLimit,ankleLimit,toesLimit,
            // Piernas y tela: franja de cadera (a lo largo de la pierna), parte del giro de rodilla que sigue el bajo,
            // franja angular del tobillo y desvanecido del trozo de pierna dentro del calcetín.
            hipBlendFrom,hipBlendTo,kneeClothFollow,collar,neckFillHeight,crotchWidth,pelvisEdgeFade,pelvisSideFadeFrom,pelvisSideFadeTo,thighTopFadeFrom,thighTopFadeTo,shoulderFollow,shoulderFollowStart,shoulderFollowMax,sleeveDrop,sleeveInset,wristHideFrom,wristHideTo,wristBendFrom,wristBendTo,wristBendFollow,
            // Capas del brazo: detrás de la camiseta (la sisa del torso tapa la manga) o delante cuando el brazo
            // cruza hacia el cuerpo más de frontAngle grados (con histéresis de frontHysteresis).
            sleeveBackOrder,upperArmBackOrder,frontAngle,frontHysteresis,ankleBlend,ankleFadeFrom,ankleFadeTo,pelvisFadeFrom,pelvisFadeTo,
            // Movimiento secundario de la tela (sólo en Play): rigidez, amortiguación, inercia, arrastre lateral y tope.
            springStiffness,springDamping,springInertia,springSway,springMax,shirtSway,sleeveSway,shortsSway,
            shirtColumnSpread,springBounce,springMaxOffset,springFlutter;
            public bool swapThighs;}
        [Serializable] public class Layout {public ArtPart[] parts;public Fit fit;}
        public AvatarRig rig;
        public SkinnedMeshRenderer[] parts;
        public SkinnedMeshRenderer torsoRenderer;
        public int recipeVersion=15;
        // Huesos auxiliares de hombro, codo y muñeca: giran 1/6, 2/6… 5/6 de la articulación, así la franja de
        // unión se dobla en seis pasos cortos (casi una curva) y no se pliega en giros grandes.
        public const int HelpersPerJoint=5;
        public Transform[] blendHelpers=new Transform[0],blendDrivers=new Transform[0];public float[] blendFractions=new float[0];
        public void SyncHelpers(){for(int i=0;i<blendHelpers.Length;i++)if(blendHelpers[i]&&blendDrivers[i])blendHelpers[i].localRotation=Quaternion.Slerp(Quaternion.identity,blendDrivers[i].localRotation,i<blendFractions.Length?blendFractions[i]:.5f);}
        // Constraints: rango natural de cada articulación. Se aplican después del controlador y del IK,
        // así ningún control ni objetivo puede llevar una articulación más allá de su tope.
        public Transform[] limitBones=new Transform[0];public float[] limitMin=new float[0],limitMax=new float[0];
        public void ApplyConstraints(){
            for(int i=0;i<limitBones.Length;i++){var bone=limitBones[i];if(!bone)continue;
                float angle=Mathf.DeltaAngle(0,bone.localEulerAngles.z),clamped=Mathf.Clamp(angle,limitMin[i],limitMax[i]);
                if(!Mathf.Approximately(angle,clamped))bone.localRotation=Quaternion.Euler(0,0,clamped);}
        }
        // Movimiento secundario (física de tela): huesos de muelle hijos de pecho, brazos y muslos. Cada uno tiene un
        // muelle de giro y otro de desplazamiento. Al girar su padre conservan parte de su orientación (inercia); al
        // desplazarse el cuerpo la tela se queda atrás (arrastre) y, al subir o bajar, rebota. En movimiento rápido
        // aletea un poco. Cada hueso tiene una rigidez algo distinta para que la tela no se mueva en bloque.
        public Transform[] springBones=new Transform[0];public float stiffness=60,damping=7,inertia=.8f,sway=18,maxAngle=12;
        public float bounce=.05f,maxOffset=.035f,flutter=1.5f;
        [NonSerialized] float[] springAngle,springVelocity,springParent,offsetY,offsetVelocity,springVariation;[NonSerialized] Vector3[] springRest;
        [NonSerialized] Vector3 lastRoot;[NonSerialized] bool springReady;
        public void ResetSprings(){springReady=false;}
        void UpdateSprings(float dt){
            if(springBones.Length==0||dt<=0)return;dt=Mathf.Min(dt,.05f);
            if(!springReady||springAngle==null||springAngle.Length!=springBones.Length){
                int n=springBones.Length;springAngle=new float[n];springVelocity=new float[n];springParent=new float[n];
                offsetY=new float[n];offsetVelocity=new float[n];springVariation=new float[n];springRest=new Vector3[n];
                for(int i=0;i<n;i++)if(springBones[i]){
                    springParent[i]=springBones[i].parent.eulerAngles.z;springRest[i]=springBones[i].localPosition;
                    springVariation[i]=.85f+.3f*((Mathf.Abs(springBones[i].name.GetHashCode())%97)/96f);
                }
                lastRoot=transform.position;springReady=true;
            }
            Vector3 velocity=(transform.position-lastRoot)/dt;lastRoot=transform.position;
            float speed=Mathf.Min(1,velocity.magnitude*2);
            int steps=Mathf.Max(1,Mathf.CeilToInt(dt/.008f));float h=dt/steps;
            for(int i=0;i<springBones.Length;i++){var bone=springBones[i];if(!bone)continue;
                float parent=bone.parent.eulerAngles.z,delta=Mathf.DeltaAngle(springParent[i],parent);springParent[i]=parent;
                springAngle[i]-=delta*inertia;
                float k=stiffness*springVariation[i];
                float wind=flutter*speed*(Mathf.PerlinNoise(Time.time*3.1f*springVariation[i],i*1.7f)*2-1);
                // La tela se queda atrás; la curva suave evita que choque de golpe contra su límite.
                float raw=(-velocity.x*sway+wind)/maxAngle,target=maxAngle*(raw/(1+Mathf.Abs(raw)))*1.6f;target=Mathf.Clamp(target,-maxAngle,maxAngle);
                float lift=Mathf.Clamp(-bone.parent.InverseTransformVector(velocity).y*bounce,-maxOffset,maxOffset);
                for(int s=0;s<steps;s++){
                    springVelocity[i]+=(k*(target-springAngle[i])-damping*springVelocity[i])*h;springAngle[i]+=springVelocity[i]*h;
                    offsetVelocity[i]+=(k*1.6f*(lift-offsetY[i])-damping*1.2f*offsetVelocity[i])*h;offsetY[i]+=offsetVelocity[i]*h;
                }
                springAngle[i]=Mathf.Clamp(springAngle[i],-maxAngle,maxAngle);offsetY[i]=Mathf.Clamp(offsetY[i],-maxOffset,maxOffset);
                bone.localRotation=Quaternion.Euler(0,0,springAngle[i]);bone.localPosition=springRest[i]+new Vector3(0,offsetY[i],0);
            }
        }
        // El hombro acompaña al brazo: al levantarlo más allá de shoulderFollowStart, el hombro sube una parte
        // (shoulderFollow) y el brazo compensa, así la dirección del brazo no cambia pero hombro y camiseta se deforman.
        // Sólo en Play, donde el controlador repone la pose en cada fotograma (si no, se acumularía).
        public Transform[] followShoulders=new Transform[0],followArms=new Transform[0];public int[] followSides=new int[0];
        public float shoulderFollow=.3f,shoulderFollowStart=15,shoulderFollowMax=22;
        public void ApplyShoulderFollow(){
            for(int i=0;i<followArms.Length;i++){var sh=followShoulders[i];var arm=followArms[i];if(!sh||!arm)continue;
                float angle=Mathf.DeltaAngle(0,arm.localEulerAngles.z),outward=angle*followSides[i];
                float lift=Mathf.Min(Mathf.Max(0,outward-shoulderFollowStart)*shoulderFollow,shoulderFollowMax)*followSides[i];
                if(Mathf.Approximately(lift,0))continue;
                sh.localRotation*=Quaternion.Euler(0,0,lift);arm.localRotation=Quaternion.Euler(0,0,angle-lift);
            }
        }
        // Capas dinámicas del brazo: con el brazo colgando o levantado, manga y brazo van detrás de la camiseta y la
        // sisa del torso tapa la unión; si el brazo cruza por delante del cuerpo, pasan delante.
        public SkinnedMeshRenderer[] armLayers=new SkinnedMeshRenderer[0];public int[] backOrders=new int[0],frontOrders=new int[0];
        public Transform[] layerShoulders=new Transform[0],layerArms=new Transform[0];public int[] layerSides=new int[0];
        public float frontAngle=14,frontHysteresis=3;[NonSerialized] bool[] inFront;
        // Grados que el brazo se ha girado hacia el cuerpo respecto al reposo (hombro + brazo).
        public float InwardAngle(int i)=>-layerSides[i]*(Mathf.DeltaAngle(0,layerShoulders[i].localEulerAngles.z)+Mathf.DeltaAngle(0,layerArms[i].localEulerAngles.z));
        public void UpdateLayering(){
            if(layerArms.Length==0||armLayers.Length==0)return;
            if(inFront==null||inFront.Length!=layerArms.Length)inFront=new bool[layerArms.Length];
            for(int i=0;i<layerArms.Length;i++){if(!layerArms[i]||!layerShoulders[i])continue;
                float inward=InwardAngle(i);
                inFront[i]=inFront[i]?inward>frontAngle-frontHysteresis:inward>frontAngle+frontHysteresis;
                for(int k=0;k<2;k++){var renderer=armLayers[i*2+k];if(renderer)renderer.sortingOrder=inFront[i]?frontOrders[i*2+k]:backOrders[i*2+k];}
            }
        }
        // Restos de una versión anterior (costura de la sisa, ya retirada): si siguen en la escena, no se dibujan.
        void HideRemovedPieces(){foreach(var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))if(renderer.name.StartsWith("Kemono_TorsoSeam"))renderer.enabled=false;}
        void OnEnable(){HideRemovedPieces();}
        void LateUpdate(){if(Application.isPlaying)ApplyShoulderFollow();ApplyConstraints();SyncHelpers();UpdateLayering();if(Application.isPlaying)UpdateSprings(Time.deltaTime);}
        public static readonly string[] oldBodyNames={"Shirt","Shorts","ArmLeft","ArmRight","LegLeft","LegRight"};
        static int Bone(AvatarRig r,string name){int i=Array.FindIndex(r.bones,b=>b&&b.name==name);if(i<0)throw new ArgumentException("Falta hueso: "+name);return i;}
        static void Place(AvatarRig r,string name,Vector3 point){var b=r.bones[Bone(r,name)];b.position=r.transform.TransformPoint(point);}
        // Coloca una pieza: su punto prox coincide con 'at' y su eje prox->dist apunta en 'direction'
        // (grados desde la vertical hacia abajo, antihorario). Sin eje, conserva la orientación dibujada.
        struct Placement {
            public ArtPart art;public Vector2 at;public float angle;
            public Vector3 Point(float u,float v){
                var local=new Vector2((u-art.prox.x)*art.pixelWidth,-(v-art.prox.y)*art.pixelHeight)*art.scale;
                float c=Mathf.Cos(angle*Mathf.Deg2Rad),s=Mathf.Sin(angle*Mathf.Deg2Rad);
                return new Vector3(at.x+local.x*c-local.y*s,at.y+local.x*s+local.y*c,0);
            }
            public Vector3 Point(Vector2 p)=>Point(p.x,p.y);
        }
        static float AxisAngle(ArtPart a){
            var v=new Vector2((a.dist.x-a.prox.x)*a.pixelWidth,-(a.dist.y-a.prox.y)*a.pixelHeight);
            return Mathf.Atan2(v.x,-v.y)*Mathf.Rad2Deg;
        }
        // Articulación entre dos huesos de una cadena. El peso del hueso siguiente depende del ángulo del punto
        // alrededor del pivote (no de su distancia a lo largo del eje): así cada punto gira una fracción del
        // ángulo según su dirección y la franja se comprime sin plegarse, también lejos del eje (manga ancha).
        // Limb: codo/muñeca, simétrico: 0 detrás (|ángulo| >= from) y 1 delante (|ángulo| <= to).
        // Shoulder: costura y parte alta de la manga con el torso; puño y axila con el brazo.
        enum JointKind {Limb,Shoulder,Slab}
        struct Joint {
            public Vector2 at,axis;public JointKind kind;public float from,to;public int side;
            // Radio de influencia: más allá de rigidTo el punto vuelve a ser rígido con su hueso (0 = sin límite).
            public float rigidFrom,rigidTo;
            // Fracción máxima del giro que sigue la zona (tela que acompaña sólo en parte); 0 = toda.
            public float maxFollow;
            public float Along(Vector3 p)=>Vector2.Dot((Vector2)p-at,axis);
            public float Weight(Vector3 p){
                float follow=maxFollow>0?maxFollow:1;
                if(kind==JointKind.Slab)return Linear(from,to,Along(p))*follow;
                return RawWeight(p)*follow;
            }
            float RawWeight(Vector3 p){
                // Junto al pivote, el ángulo cambia mucho en muy poco espacio: esa zona sigue al auxiliar en bloque.
                var d=(Vector2)p-at;float distance=d.magnitude;float near=Mathf.Clamp01(distance/.05f);near=near*near*(3-2*near);
                float w=Mathf.Lerp(.5f,AngularWeight(d),near);
                if(rigidTo>0){
                    float angle=d.sqrMagnitude<1e-10f?0:Vector2.SignedAngle(axis,d);
                    float far=kind==JointKind.Limb?Linear(100,80,Mathf.Abs(angle)):1;
                    w=Mathf.Lerp(w,far,Step(rigidFrom,rigidTo,distance));
                }
                return w;
            }
            float AngularWeight(Vector2 d){
                if(d.sqrMagnitude<1e-10f)return .5f;
                float angle=Vector2.SignedAngle(axis,d);
                // Rampa lineal: reparte el giro por igual en toda la franja. No se pliega mientras el giro
                // no supere la anchura de la franja (una curva suavizada sólo aguanta dos tercios).
                if(kind==JointKind.Limb)return Linear(from,to,Mathf.Abs(angle));
                float outer=angle*side; // positivo hacia fuera (lejos del torso)
                if(outer>=-90&&outer<=to)return 1;
                if(outer>to)return 1-Linear(to,from,outer);
                return Linear(-160,-90,outer);
            }
        }
        static float Linear(float a,float b,float x)=>Mathf.Approximately(a,b)?(x>=a?1:0):Mathf.Clamp01((x-a)/(b-a));
        static float Step(float a,float b,float x){if(Mathf.Approximately(a,b))return x>=a?1:0;float t=Mathf.Clamp01((x-a)/(b-a));return t*t*(3-2*t);}
        // Zona de tela que se reparte con un hueso de muelle (movimiento secundario).
        // mask (opcional) limita la zona en horizontal: así varios muelles se reparten el bajo de la camiseta.
        struct SpringZone {public string bone;public Joint region;public float amount;public Func<Vector3,float> mask;}
        class Piece {public string name;public Placement place;public string[] chain;public Joint[] joints;public Joint[] fades=new Joint[0];
            public Func<Vector3,(string bone,float weight)[]> custom;public Func<Vector3,float> opacity;
            // Difumina sólo el contorno inferior del dibujo (en píxeles), donde edgeMask vale 1.
            public float bottomEdgeFade;public Func<Vector3,float> edgeMask;public SpringZone[] springs=new SpringZone[0];}
        static Joint[] Concat(params Joint[] parts)=>parts;
        static string[] HelperNames(string prefix){var names=new string[HelpersPerJoint];for(int i=0;i<names.Length;i++)names[i]=prefix+(i+1);return names;}
        // Cadena de huesos: padre, sus auxiliares, hijo, auxiliares del siguiente, nieto...
        static string[] Chain(params object[] items){var list=new List<string>();foreach(var item in items)if(item is string one)list.Add(one);else list.AddRange((string[])item);return list.ToArray();}
        static Piece Rigid(string name,Placement place,string bone)=>new Piece{name=name,place=place,chain=new[]{bone},joints=new Joint[0]};
        static Placement Put(ArtPart art,Vector2 at,float? direction)=>new Placement{art=art,at=at,angle=direction.HasValue?direction.Value-AxisAngle(art):0};
        public static KemonoBodyRig Build(AvatarRig r,Layout layout,Func<string,Texture2D> load){
            if(r.GetComponent<KemonoBodyRig>())throw new ArgumentException("Este rig ya tiene el cuerpo nuevo");
            var fit=layout.fit;if(fit==null)throw new ArgumentException("El layout no tiene datos de encaje (fit)");
            ArtPart Find(string name){var p=Array.Find(layout.parts,a=>a.name==name);if(p==null)throw new ArgumentException("Falta dibujo: "+name);if(p.scale<=0)throw new ArgumentException("Falta escala: "+name);return p;}
            Vector2 Mirror(Vector2 p,int side)=>side<0?p:new Vector2(1-p.x,p.y);
            // 1. Encaje en reposo, derivado del dibujo: torso y pelvis, después cada cadena de brazo y pierna.
            var plan=new List<Piece>();
            var torso=Put(Find("Torso"),fit.neck,null);
            var pelvis=Put(Find("Pelvis"),torso.Point(fit.pelvisAt),null);
            var joints=new Dictionary<string,Vector3>();
            var helpers=new List<(string name,string parent,string driver,Vector2 at,float fraction)>();
            var springs=new List<(string name,string parent,Vector2 at)>();
            Vector2 neckJoint=r.transform.InverseTransformPoint(r.bones[Bone(r,"Neck")].position),chestJoint=r.transform.InverseTransformPoint(r.bones[Bone(r,"Chest")].position);
            Vector2 headJoint=r.transform.InverseTransformPoint(r.bones[Bone(r,"Head")].position);
            Vector2 hipsJoint=r.transform.InverseTransformPoint(r.bones[Bone(r,"Hips")].position);
            Vector2 torsoShoulderL=torso.Point(Mirror(fit.shoulder,-1)),torsoShoulderR=torso.Point(Mirror(fit.shoulder,1));
            // Sólo el tallo del cuello, por encima del cuello de la camiseta, sigue al hueso Neck.
            float collar=torso.Point(.5f,fit.collar>0?fit.collar:.1f).y;
            // Camiseta: el tallo del cuello sigue al cuello, la parte alta al pecho, la media a la columna y el bajo
            // acompaña en parte a la cadera. Los hombros siguen al encogimiento. Nunca a los brazos.
            (string,float)[] TorsoWeights(Vector3 p){
                float y=p.y,n=Step(collar,collar+.10f,y)*(1-Step(.10f,.18f,Mathf.Abs(p.x-neckJoint.x))),h=.5f*Step(hipsJoint.y+.30f,hipsJoint.y,y),sp=Step(chestJoint.y+.20f,chestJoint.y-.20f,y);
                float rest=1-n,hips=rest*h,spine=rest*(1-h)*sp,chest=rest*(1-h)*(1-sp);
                float shL=.6f*(1-Step(.04f,.40f,Vector2.Distance(p,torsoShoulderL))),shR=.6f*(1-Step(.04f,.40f,Vector2.Distance(p,torsoShoulderR)));
                float takeL=Mathf.Min(chest,shL),takeR=Mathf.Min(chest-takeL,shR);
                // El tallo del cuello se dobla: abajo sigue al cuello y arriba, bajo la barbilla, a la cabeza. Así al
                // inclinar la cabeza el cuello la acompaña y no se abre hueco bajo la barbilla.
                float toHead=Step(collar+.02f,headJoint.y-.02f,y);
                return new[]{("Neck",n*(1-toHead)),("Head",n*toHead),("Hips",hips),("Spine",spine),("Chest",chest-takeL-takeR),("RightShoulder",takeL),("LeftShoulder",takeR)};
            }
            // Bajo de la camiseta: tres muelles (izquierda, centro y derecha) con pivote a la altura del pecho. Se
            // reparten el peso con una partición suave en horizontal, así el bajo ondula en lugar de girar en bloque.
            var hemRegion=new Joint{at=chestJoint,axis=Vector2.down,from=chestJoint.y-hipsJoint.y-.35f,to=chestJoint.y-hipsJoint.y+.05f};
            float hemSpread=fit.shirtColumnSpread>0?fit.shirtColumnSpread:.38f,cx=neckJoint.x;
            Func<Vector3,float> hemLeft=p=>1-Step(cx-hemSpread,cx,p.x),hemRight=p=>Step(cx,cx+hemSpread,p.x);
            var hemZones=new List<SpringZone>();
            if(fit.shirtSway>0){
                springs.Add(("ShirtSwayLeft","Chest",new Vector2(cx-hemSpread,chestJoint.y)));springs.Add(("ShirtSway","Chest",chestJoint));springs.Add(("ShirtSwayRight","Chest",new Vector2(cx+hemSpread,chestJoint.y)));
                hemZones.Add(new SpringZone{bone="ShirtSwayLeft",region=hemRegion,amount=fit.shirtSway,mask=hemLeft});
                hemZones.Add(new SpringZone{bone="ShirtSway",region=hemRegion,amount=fit.shirtSway,mask=p=>1-hemLeft(p)-hemRight(p)});
                hemZones.Add(new SpringZone{bone="ShirtSwayRight",region=hemRegion,amount=fit.shirtSway,mask=hemRight});
            }
            plan.Add(new Piece{name="Torso",place=torso,custom=TorsoWeights,springs=hemZones.ToArray()});
            // Relleno del cuello: una franja del propio cuello dibujado, estirada desde el cuello de la camiseta hasta
            // dentro de la cabeza y detrás de ella. Sigue al cuello abajo y a la cabeza arriba, así que al girar,
            // inclinar o adelantar la cabeza nunca se abre un hueco bajo la barbilla.
            if(fit.neckFillHeight>0){
                var t=torso.art;float u0=.40f,u1=.595f,v0=.012f,v1=.07f;
                float bottom=collar+.02f,top=headJoint.y+fit.neckFillHeight,height=top-bottom;
                var fill=new ArtPart{name="NeckFill",texture=t.texture,x=t.x+u0*t.width,width=(u1-u0)*t.width,y=t.y+(1-v1)*t.height,height=(v1-v0)*t.height,
                    pixelWidth=(u1-u0)*t.pixelWidth,pixelHeight=height/t.scale,scale=t.scale,prox=new Vector2(.5f,1),dist=new Vector2(.5f,0),order=t.order-1};
                var fillPlace=new Placement{art=fill,at=new Vector2(neckJoint.x,bottom),angle=0};
                (string,float)[] FillWeights(Vector3 p){float k=Step(collar+.02f,headJoint.y,p.y);return new[]{("Neck",1-k),("Head",k)};}
                plan.Add(new Piece{name="NeckFill",place=fillPlace,custom=FillWeights});
            }
            // Cintura con bolsillos a la vista: su borde inferior se desvanece sobre las perneras.
            float pelvisHeight=pelvis.art.pixelHeight*pelvis.art.scale;Vector2 pelvisTop=pelvis.Point(.5f,0);
            // El centro (entrepierna) queda opaco: tapa el hueco entre las dos perneras.
            float crotch=fit.crotchWidth>0?fit.crotchWidth:.12f;
            var hipChains=new Dictionary<int,(string[] chain,Joint joint)>();
            foreach(int side in new[]{-1,1}){
                // Lado -1: izquierda de la pantalla = huesos Right del personaje, dibujos *Left.
                string label=side<0?"Left":"Right",bone=side<0?"Right":"Left";
                Vector2 shoulder=torso.Point(Mirror(fit.shoulder,side));
                var upper=Put(Find("UpperArm"+label),shoulder,side*fit.armAngle);
                Vector2 elbow=upper.Point(upper.art.dist);
                var fore=Put(Find("Forearm"+label),elbow,side*fit.foreAngle);
                Vector2 wrist=fore.Point(fore.art.dist);
                var hand=Put(Find("Hand"+label),wrist,side*fit.foreAngle);
                // La manga se desliza a lo largo del brazo (sleeveDrop) y hacia el cuerpo (sleeveInset) para que
                // su copa quede a la altura del hombro de la camiseta y no por encima.
                float armRad=side*fit.armAngle*Mathf.Deg2Rad;Vector2 armDown=new Vector2(Mathf.Sin(armRad),-Mathf.Cos(armRad));
                Vector2 towardBody=new Vector2(-side,0);
                var sleeve=Put(Find("Sleeve"+label),shoulder+armDown*fit.sleeveDrop+towardBody*fit.sleeveInset,side*fit.armAngle);
                Vector2 hip=pelvis.Point(Mirror(fit.hip,side));
                string thighArt=fit.swapThighs?(label=="Left"?"ThighRight":"ThighLeft"):"Thigh"+label;
                var thigh=Put(Find(thighArt),hip,side*fit.legAngle);
                Vector2 knee=thigh.Point(thigh.art.dist);
                var calf=Put(Find("Calf"+label),knee,side*fit.calfAngle);
                Vector2 ankle=calf.Point(calf.art.dist);
                var foot=Put(Find("Foot"+label),ankle,null);
                // Ejes de cada articulación (hacia el extremo de la cadena) en reposo.
                Vector2 armAxis=((Vector2)elbow-shoulder).normalized,foreAxis=((Vector2)wrist-elbow).normalized;
                var shoulderJoint=new Joint{at=shoulder,axis=armAxis,kind=JointKind.Shoulder,from=fit.shoulderBlendFrom,to=fit.shoulderBlendTo,side=side};
                var elbowJoint=new Joint{at=elbow,axis=(armAxis+foreAxis).normalized,kind=JointKind.Limb,from=90+fit.elbowBlend,to=90-fit.elbowBlend,rigidFrom=fit.elbowRadius*.45f,rigidTo=fit.elbowRadius};
                // El brazo bajo la manga sigue al hombro en bloque fuera del entorno del pivote; la manga no tiene límite.
                var shoulderArm=shoulderJoint;shoulderArm.rigidFrom=fit.shoulderRadius*.4f;shoulderArm.rigidTo=fit.shoulderRadius;
                // El brazo sigue del todo al hombro a lo largo de su eje; sólo su lado exterior acompaña a la manga.
                if(fit.shoulderArmBlendTo!=0)shoulderArm.to=fit.shoulderArmBlendTo;
                string sh=bone+"Shoulder",ua=bone+"UpperArm",la=bone+"LowerArm",hd=bone+"Hand";
                string[] shB=HelperNames(bone+"ShoulderBlend"),elB=HelperNames(bone+"ElbowBlend"),wrB=HelperNames(bone+"WristBlend");
                plan.Add(new Piece{name="Sleeve"+label,place=sleeve,chain=Chain(sh,shB,ua),joints=new[]{shoulderJoint},
                    springs=new[]{new SpringZone{bone=bone+"SleeveSway",region=new Joint{at=shoulder,axis=armAxis,from=.28f,to=.48f},amount=fit.sleeveSway}}});
                plan.Add(new Piece{name="UpperArm"+label,place=upper,chain=Chain(sh,shB,ua,elB,la),joints=Concat(shoulderArm,elbowJoint),
                    // El extremo superior va siempre dentro de la manga: se oculta para que no asome junto al cuello.
                    fades=new[]{new Joint{at=elbow,axis=elbowJoint.axis,from=fit.elbowFadeFrom,to=fit.elbowFadeTo},
                        new Joint{at=shoulder,axis=armAxis,from=fit.shoulderFadeFrom,to=fit.shoulderFadeTo}}});
                // Muñeca con guante: el puño es tela rígida, así que la mano no se deforma; gira desde el centro del
                // puño. El extremo del antebrazo acompaña una parte del giro (wristBendFollow) para seguir dentro
                // del puño, y se desvanece (wristHide*) antes de su punta para no asomar.
                var wristJoint=new Joint{at=wrist,axis=foreAxis,kind=JointKind.Slab,from=fit.wristBendFrom,to=fit.wristBendTo,maxFollow=fit.wristBendFollow};
                plan.Add(new Piece{name="Forearm"+label,place=fore,chain=Chain(ua,elB,la,wrB,hd),joints=Concat(elbowJoint,wristJoint),
                    fades=fit.wristHideTo>fit.wristHideFrom?new[]{new Joint{at=wrist,axis=foreAxis,from=fit.wristHideFrom,to=fit.wristHideTo}}:new Joint[0]});
                plan.Add(new Piece{name="Hand"+label,place=hand,chain=new[]{hd},joints=new Joint[0]});
                for(int h=0;h<HelpersPerJoint;h++){
                    float f=(h+1f)/(HelpersPerJoint+1);
                    helpers.Add((shB[h],sh,ua,shoulder,f));helpers.Add((elB[h],ua,la,elbow,f));helpers.Add((wrB[h],la,hd,wrist,f));
                }
                // Piernas: la pernera queda unida a la cintura (cadera) y su bajo acompaña en parte a la rodilla;
                // el tobillo se dobla como el codo y el trozo de pierna dibujado dentro del calcetín se desvanece.
                string hp="Hips",ul=bone+"UpperLeg",ll=bone+"LowerLeg",ft=bone+"Foot";
                string[] hpB=HelperNames(bone+"HipBlend"),knB=HelperNames(bone+"KneeBlend"),anB=HelperNames(bone+"AnkleBlend");
                Vector2 legAxis=((Vector2)knee-hip).normalized,calfAxis=((Vector2)ankle-knee).normalized;
                var hipJoint=new Joint{at=hip,axis=legAxis,kind=JointKind.Slab,from=fit.hipBlendFrom,to=fit.hipBlendTo};
                hipChains[side]=(Chain(hp,hpB,ul),hipJoint);
                var kneeCloth=new Joint{at=knee,axis=legAxis,kind=JointKind.Slab,from=-.25f,to=.05f,maxFollow=fit.kneeClothFollow};
                var ankleJoint=new Joint{at=ankle,axis=calfAxis,kind=JointKind.Limb,from=90+fit.ankleBlend,to=90-fit.ankleBlend};
                if(fit.shortsSway>0)springs.Add((bone+"ShortsSway",ul,hip));if(fit.sleeveSway>0)springs.Add((bone+"SleeveSway",ua,shoulder));
                // La boca superior de la pernera (forro azul) se funde con la cintura, que queda detrás.
                plan.Add(new Piece{name="Thigh"+label,place=thigh,chain=Chain(hp,hpB,ul,knB,ll),joints=Concat(hipJoint,kneeCloth),
                    fades=fit.thighTopFadeFrom!=fit.thighTopFadeTo?new[]{new Joint{at=hip,axis=legAxis,from=fit.thighTopFadeFrom,to=fit.thighTopFadeTo}}:new Joint[0],
                    springs=new[]{new SpringZone{bone=bone+"ShortsSway",region=new Joint{at=hip,axis=legAxis,from=.30f,to=.80f},amount=fit.shortsSway}}});
                plan.Add(new Piece{name="Calf"+label,place=calf,chain=Chain(ll,anB,ft),joints=new[]{ankleJoint}});
                plan.Add(new Piece{name="Foot"+label,place=foot,chain=Chain(ll,anB,ft),joints=new[]{ankleJoint},
                    fades=new[]{new Joint{at=ankle,axis=calfAxis,from=fit.ankleFadeFrom,to=fit.ankleFadeTo}}});
                for(int h=0;h<HelpersPerJoint;h++){
                    float f=(h+1f)/(HelpersPerJoint+1);
                    helpers.Add((hpB[h],hp,ul,hip,f));helpers.Add((knB[h],ul,ll,knee,f));helpers.Add((anB[h],ll,ft,ankle,f));
                }
                joints[bone+"Shoulder"]=new Vector3(shoulder.x*.55f,shoulder.y,0);
                joints[bone+"UpperArm"]=shoulder;joints[bone+"LowerArm"]=elbow;joints[bone+"Hand"]=wrist;
                joints[bone+"UpperLeg"]=hip;joints[bone+"LowerLeg"]=knee;joints[bone+"Foot"]=ankle;joints[bone+"Toes"]=foot.Point(foot.art.dist);
            }
            // Cintura: el centro y la pretina siguen a la cadera; cada lado, la parte que cubre la pernera,
            // se reparte con su muslo igual que la propia pernera, así se mueven juntas sin abrir huecos.
            (string,float)[] PelvisWeights(Vector3 p){
                var (chain,joint)=hipChains[p.x<pelvisTop.x?-1:1];
                float side=Step(crotch*.15f,crotch*1.6f,Mathf.Abs(p.x-pelvisTop.x));
                var w=ChainWeights(chain.Length,new[]{joint},p);var result=new (string,float)[chain.Length+1];
                for(int i=0;i<chain.Length;i++)result[i]=(chain[i],w[i]*side);
                result[chain.Length]=("Hips",1-side);return result;
            }
            // Opaca y detrás de las perneras: su borde inferior queda tapado y la entrepierna se ve entre ellas.
            // bottomEdgeFade permite además difuminar su contorno inferior a los lados (0 = no).
            // Los costados que sobresalen de las perneras se difuminan: la silueta la marcan las perneras.
            plan.Add(new Piece{name="Pelvis",place=pelvis,custom=PelvisWeights,bottomEdgeFade=fit.pelvisEdgeFade,
                opacity=fit.pelvisSideFadeTo>0?p=>1-Step(fit.pelvisSideFadeFrom,fit.pelvisSideFadeTo,Mathf.Abs(p.x-pelvisTop.x)):(Func<Vector3,float>)null,
                edgeMask=p=>Step(crotch*.5f,crotch,Mathf.Abs(p.x-pelvisTop.x))});
            // 2. Los huesos van a las articulaciones del dibujo (de padre a hijo). Cabeza, columna y cola no se tocan.
            foreach(string side in new[]{"Right","Left"})
                foreach(string joint in new[]{"Shoulder","UpperArm","LowerArm","Hand","UpperLeg","LowerLeg","Foot","Toes"})
                    Place(r,side+joint,joints[side+joint]);
            // Auxiliares: se reutilizan al reconstruir; si faltan se añaden al final de la lista de huesos.
            var helperBones=new List<Transform>();var drivers=new List<Transform>();var fractions=new List<float>();var allBones=new List<Transform>(r.bones);
            foreach(var h in helpers){
                var t=allBones.Find(b=>b&&b.name==h.name);
                if(!t){t=new GameObject(h.name).transform;allBones.Add(t);}
                t.SetParent(r.bones[Bone(r,h.parent)],false);t.position=r.transform.TransformPoint(h.at);t.localRotation=Quaternion.identity;
                helperBones.Add(t);drivers.Add(r.bones[Bone(r,h.driver)]);fractions.Add(h.fraction);
            }
            var springBones=new List<Transform>();
            foreach(var sp in springs){
                var t=allBones.Find(b=>b&&b.name==sp.name);
                if(!t){t=new GameObject(sp.name).transform;allBones.Add(t);}
                t.SetParent(r.bones[Bone(r,sp.parent)],false);t.position=r.transform.TransformPoint(sp.at);t.localRotation=Quaternion.identity;springBones.Add(t);
            }
            r.bones=allBones.ToArray();
            foreach(var layer in r.layers)if(Array.IndexOf(oldBodyNames,layer.name)>=0)layer.enabled=false;
            var body=r.gameObject.AddComponent<KemonoBodyRig>();body.rig=r;body.blendHelpers=helperBones.ToArray();body.blendDrivers=drivers.ToArray();body.blendFractions=fractions.ToArray();
            body.springBones=springBones.ToArray();
            if(fit.springStiffness>0){body.stiffness=fit.springStiffness;body.damping=fit.springDamping;body.inertia=fit.springInertia;body.sway=fit.springSway;body.maxAngle=fit.springMax;}
            if(fit.springBounce>0){body.bounce=fit.springBounce;body.maxOffset=fit.springMaxOffset;body.flutter=fit.springFlutter;}
            var renderers=new List<SkinnedMeshRenderer>();
            var alphaCache=new Dictionary<Texture2D,(byte[] a,int w,int h)>();
            var bind=new Matrix4x4[r.bones.Length];for(int i=0;i<bind.Length;i++)bind[i]=r.bones[i].worldToLocalMatrix*r.transform.localToWorldMatrix;
            // 3. Una malla por pieza: rígida en su parte central y deformable en uniones, cintura y tela.
            foreach(var piece in plan){
                var art=piece.place.art;var texture=load(art.texture);if(!texture)throw new ArgumentException("Falta textura: "+art.texture);
                var go=new GameObject("Kemono_"+piece.name);go.transform.SetParent(r.transform,false);
                var renderer=go.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=MeshFor(r,piece,bind,AlphaOf(texture,alphaCache));
                renderer.bones=r.bones;renderer.rootBone=r.bones[0];renderer.updateWhenOffscreen=true;renderer.quality=SkinQuality.Bone4;renderer.sortingOrder=art.order;
                renderer.localBounds=new Bounds(new Vector3(0,2.4f,0),new Vector3(9,9,3));
                var material=new Material(Shader.Find("Traigger/UnlitAlpha"));material.mainTexture=texture;material.color=Color.white;material.SetFloat("_Volume",0);material.SetFloat("_VertexAlpha",1);renderer.sharedMaterial=material;
                renderers.Add(renderer);if(piece.name=="Torso")body.torsoRenderer=renderer;
            }
            // Límites por lado: en el lado -1 (izquierda de la pantalla) "hacia fuera" es giro negativo.
            var lb=new List<Transform>();var lmin=new List<float>();var lmax=new List<float>();
            void Limit(string name,int side,float outward,float inward){
                if(outward<=0&&inward<=0)return;
                float o=outward>0?outward:180,n=inward>0?inward:180;
                lb.Add(r.bones[Bone(r,name)]);if(side<0){lmin.Add(-o);lmax.Add(n);}else{lmin.Add(-n);lmax.Add(o);}
            }
            foreach(int side in new[]{-1,1}){
                string bone=side<0?"Right":"Left";
                Limit(bone+"Shoulder",side,fit.shrugLimit,fit.shrugLimit);
                Limit(bone+"UpperArm",side,fit.shoulderRaiseLimit,fit.shoulderInwardLimit);
                Limit(bone+"LowerArm",side,fit.elbowLimit,fit.elbowLimit);
                Limit(bone+"Hand",side,fit.wristLimit,fit.wristLimit);
                Limit(bone+"UpperLeg",side,fit.hipOutwardLimit,fit.hipInwardLimit);
                Limit(bone+"LowerLeg",side,fit.kneeLimit,fit.kneeLimit);
                Limit(bone+"Foot",side,fit.ankleLimit,fit.ankleLimit);
                Limit(bone+"Toes",side,fit.toesLimit,fit.toesLimit);
            }
            body.limitBones=lb.ToArray();body.limitMin=lmin.ToArray();body.limitMax=lmax.ToArray();
            // Capas dinámicas: [manga, brazo] por lado, en el mismo orden que followSides.
            var layerList=new List<SkinnedMeshRenderer>();var backList=new List<int>();var frontList=new List<int>();
            foreach(string label in new[]{"Left","Right"})foreach(string piece in new[]{"Sleeve","UpperArm"}){
                var renderer=renderers.Find(x=>x.name=="Kemono_"+piece+label);layerList.Add(renderer);frontList.Add(renderer.sortingOrder);
                backList.Add(Mathf.RoundToInt(piece=="Sleeve"?fit.sleeveBackOrder:fit.upperArmBackOrder));
            }
            // Hombros y brazos para InwardAngle (lo usan también SleeveSeams y TorsoSeam), aunque no haya capas dinámicas.
            body.layerShoulders=new[]{r.bones[Bone(r,"RightShoulder")],r.bones[Bone(r,"LeftShoulder")]};
            body.layerArms=new[]{r.bones[Bone(r,"RightUpperArm")],r.bones[Bone(r,"LeftUpperArm")]};body.layerSides=new[]{-1,1};
            if(fit.sleeveBackOrder!=0||fit.upperArmBackOrder!=0){
                body.armLayers=layerList.ToArray();body.backOrders=backList.ToArray();body.frontOrders=frontList.ToArray();
                if(fit.frontAngle>0){body.frontAngle=fit.frontAngle;body.frontHysteresis=fit.frontHysteresis;}
                body.UpdateLayering();
            }
            body.followShoulders=new[]{r.bones[Bone(r,"RightShoulder")],r.bones[Bone(r,"LeftShoulder")]};
            body.followArms=new[]{r.bones[Bone(r,"RightUpperArm")],r.bones[Bone(r,"LeftUpperArm")]};body.followSides=new[]{-1,1};
            if(fit.shoulderFollow>0){body.shoulderFollow=fit.shoulderFollow;body.shoulderFollowStart=fit.shoulderFollowStart;body.shoulderFollowMax=fit.shoulderFollowMax;}
            body.parts=renderers.ToArray();r.CaptureRest();return body;
        }
        // Lee el alfa de la textura aunque no esté marcada como legible (copia por GPU, sólo al construir).
        static (byte[] a,int w,int h) AlphaOf(Texture2D texture,Dictionary<Texture2D,(byte[] a,int w,int h)> cache){
            if(cache.TryGetValue(texture,out var hit))return hit;
            var rt=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
            Graphics.Blit(texture,rt);RenderTexture.active=rt;
            var copy=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);copy.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);copy.Apply();
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);
            var pixels=copy.GetPixels32();var alpha=new byte[pixels.Length];for(int i=0;i<pixels.Length;i++)alpha[i]=pixels[i].a;
            if(Application.isPlaying)Destroy(copy);else DestroyImmediate(copy);
            return cache[texture]=(alpha,texture.width,texture.height);
        }
        // Borde inferior del dibujo en la columna u (v normalizado, hacia abajo).
        static float BottomEdge(ArtPart art,(byte[] a,int w,int h) alpha,float u){
            int x=Mathf.Clamp(Mathf.RoundToInt((art.x+u*art.width)*alpha.w),0,alpha.w-1);
            int y0=Mathf.Clamp(Mathf.FloorToInt(art.y*alpha.h),0,alpha.h-1),y1=Mathf.Clamp(Mathf.CeilToInt((art.y+art.height)*alpha.h),0,alpha.h-1);
            for(int y=y0;y<=y1;y++)for(int dx=-2;dx<=2;dx++){int xx=Mathf.Clamp(x+dx,0,alpha.w-1);if(alpha.a[y*alpha.w+xx]>40)return 1-((y-art.y*alpha.h)/(art.height*alpha.h));}
            return 1;
        }
        // ¿Hay dibujo en este rectángulo de la celda (u,v normalizados, v hacia abajo)? Con margen de 3 px.
        static bool HasInk(ArtPart art,(byte[] a,int w,int h) alpha,float u0,float v0,float u1,float v1){
            int x0=Mathf.FloorToInt((art.x+u0*art.width)*alpha.w)-3,x1=Mathf.CeilToInt((art.x+u1*art.width)*alpha.w)+3;
            // Filas de la textura desde abajo: v=0 (arriba de la celda) corresponde a art.y+art.height.
            int y0=Mathf.FloorToInt((art.y+(1-v1)*art.height)*alpha.h)-3,y1=Mathf.CeilToInt((art.y+(1-v0)*art.height)*alpha.h)+3;
            x0=Mathf.Clamp(x0,0,alpha.w-1);x1=Mathf.Clamp(x1,0,alpha.w-1);y0=Mathf.Clamp(y0,0,alpha.h-1);y1=Mathf.Clamp(y1,0,alpha.h-1);
            for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)if(alpha.a[y*alpha.w+x]>4)return true;
            return false;
        }
        static Mesh MeshFor(AvatarRig r,Piece piece,Matrix4x4[] bind,(byte[] a,int w,int h) ink){
            // Malla densa a lo largo del eje para que las franjas de unión y el desvanecido sean suaves.
            const int nx=10,ny=36;var art=piece.place.art;var vertices=new Vector3[(nx+1)*(ny+1)];var uv=new Vector2[vertices.Length];
            var colors=new Color[vertices.Length];var weights=new BoneWeight[vertices.Length];var triangles=new List<int>();
            var bones=piece.chain!=null?Array.ConvertAll(piece.chain,name=>Bone(r,name)):new int[0];
            for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++){
                int i=y*(nx+1)+x;float u=x/(float)nx,v=y/(float)ny;var p=piece.place.Point(u,v);
                vertices[i]=p;uv[i]=new Vector2(art.x+u*art.width,art.y+(1-v)*art.height);
                // Cada articulación reparte el peso a lo largo de padre → auxiliares → hijo según la fracción de giro.
                // Lo que llega al hijo pasa a la articulación siguiente. Las dos piezas que se solapan
                // reciben exactamente los mismos pesos, así que se doblan juntas.
                var weight=new Dictionary<int,float>();
                void Add(int bone,float value){if(value<=0)return;weight.TryGetValue(bone,out float old);weight[bone]=old+value;}
                if(piece.custom!=null)foreach(var (name,value) in piece.custom(p))Add(Bone(r,name),value);
                else{
                    var w=ChainWeights(bones.Length,piece.joints,p);
                    for(int b=0;b<bones.Length;b++)Add(bones[b],w[b]);
                }
                // Tela con muelle: una parte del peso pasa a los huesos de muelle de cada zona (todas a la vez, para
                // que las zonas que se reparten el bajo sumen exactamente su parte).
                float total=0;var shares=new List<(string bone,float f)>();
                foreach(var zone in piece.springs){
                    if(zone.amount<=0)continue;
                    float f=zone.amount*Step(zone.region.from,zone.region.to,zone.region.Along(p))*(zone.mask!=null?zone.mask(p):1);
                    if(f>0){shares.Add((zone.bone,f));total+=f;}
                }
                if(total>0){
                    float scale=1-Mathf.Min(total,1);foreach(var key in new List<int>(weight.Keys))weight[key]*=scale;
                    foreach(var (bone,f) in shares)Add(Bone(r,bone),f*Mathf.Min(total,1)/total);
                }
                var keys=new int[weight.Count];var values=new float[weight.Count];int n=0;foreach(var kv in weight){keys[n]=kv.Key;values[n]=kv.Value;n++;}
                weights[i]=Pack(keys,values);

                float alpha=piece.opacity!=null?piece.opacity(p):1;foreach(var f in piece.fades)alpha*=1-Step(f.from,f.to,f.Along(p));
                if(piece.bottomEdgeFade>0){
                    float edge=BottomEdge(art,ink,u),distance=(edge-v)*art.pixelHeight;
                    alpha*=Mathf.Lerp(1,Step(0,piece.bottomEdgeFade,distance),piece.edgeMask!=null?piece.edgeMask(p):1);
                }
                colors[i]=new Color(1,1,1,alpha);
            }
            // Sin triángulos donde no se ve nada (sprite transparente o extremo desvanecido del todo):
            // menos geometría y nada que plegar fuera de lo visible.
            for(int y=0;y<ny;y++)for(int x=0;x<nx;x++){
                int a=y*(nx+1)+x,b=a+1,c=a+nx+1,d=c+1;
                if(Mathf.Max(Mathf.Max(colors[a].a,colors[b].a),Mathf.Max(colors[c].a,colors[d].a))<.004f)continue;
                if(HasInk(art,ink,x/(float)nx,y/(float)ny,(x+1f)/nx,(y+1f)/ny))triangles.AddRange(new[]{a,c,b,b,c,d});
            }
            var mesh=new Mesh{name="Kemono_"+piece.name+"_mesh"};mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.boneWeights=weights;mesh.bindposes=bind;mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        // Reparto a lo largo de una cadena padre → auxiliares → hijo → ... según cada articulación.
        static float[] ChainWeights(int count,Joint[] joints,Vector3 p){
            var w=new float[count];float carried=1;int k=0;
            foreach(var jt in joints){
                // step en [0, N+1]: 0 = padre, 1..N = auxiliares, N+1 = hijo (punto de partida de la siguiente).
                float step=jt.Weight(p)*(HelpersPerJoint+1);int i0=Mathf.Min(Mathf.FloorToInt(step),HelpersPerJoint);float frac=step-i0;
                w[k+i0]+=carried*(1-frac);
                if(i0<HelpersPerJoint){w[k+i0+1]+=carried*frac;carried=0;}else carried*=frac;
                k+=HelpersPerJoint+1;
            }
            w[k]+=carried;return w;
        }
        static BoneWeight Pack(int[] bones,float[] w){
            var order=new List<int>();for(int i=0;i<bones.Length;i++)if(w[i]>1e-4f)order.Add(i);
            order.Sort((a,b)=>w[b].CompareTo(w[a]));if(order.Count>4)order.RemoveRange(4,order.Count-4);
            float sum=0;foreach(int i in order)sum+=w[i];
            var bw=new BoneWeight();
            for(int k=0;k<order.Count;k++){int i=order[k];float value=w[i]/sum;
                if(k==0){bw.boneIndex0=bones[i];bw.weight0=value;}else if(k==1){bw.boneIndex1=bones[i];bw.weight1=value;}
                else if(k==2){bw.boneIndex2=bones[i];bw.weight2=value;}else{bw.boneIndex3=bones[i];bw.weight3=value;}}
            return bw;
        }
        public void SetVolume(float value){foreach(var r in parts)r.sharedMaterial.SetFloat("_Volume",Mathf.Clamp(value,0,.12f));}
    }
}
