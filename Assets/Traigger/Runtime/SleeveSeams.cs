using System;
using System.Collections.Generic;
using UnityEngine;
namespace Traigger {
    // Mejora local del rig existente. No recoloca huesos, no reconstruye el cuerpo ni modifica el atlas.
    [ExecuteAlways,DefaultExecutionOrder(500)]
    public class SleeveSeams : MonoBehaviour {
        public KemonoBodyRig body;
        public SkinnedMeshRenderer[] fronts=new SkinnedMeshRenderer[0],linings=new SkinnedMeshRenderer[0],arms=new SkinnedMeshRenderer[0];
        public float rootInner=.075f,rootOuter=.17f;
        public bool shouldersAligned;
        public bool joinsIntegrated;
        public Mesh[] shoulderSources=new Mesh[0];
        public void PrepareOpaqueSkin(){
            for(int side=0;side<arms.Length;side++){
                var arm=arms[side];var fore=Array.Find(body.parts,r=>r.name=="Kemono_Forearm"+(side==0?"Left":"Right"));
                var mesh=Instantiate(arm.sharedMesh);mesh.name=arm.sharedMesh.name+"_opaqueColorJoin";
                var grid=fore.sharedMesh.vertices;var uv=fore.sharedMesh.uv;var v=mesh.vertices;var under=new Vector2[v.Length];
                Vector3 a=grid[0],x=grid[10]-a,y=grid[396]-a;float determinant=x.x*y.y-x.y*y.x;
                if(Mathf.Abs(determinant)<1e-8f)throw new ArgumentException("Cuadrícula de antebrazo degenerada");
                for(int n=0;n<v.Length;n++){
                    var p=fore.transform.InverseTransformPoint(arm.transform.TransformPoint(v[n]))-a;
                    float u=(p.x*y.y-p.y*y.x)/determinant,t=(x.x*p.y-x.y*p.x)/determinant;
                    t=Mathf.Clamp(t,.18f,.95f);
                    under[n]=uv[0]+u*(uv[10]-uv[0])+t*(uv[396]-uv[0]);
                }
                mesh.uv2=under;arm.sharedMesh=mesh;
            }
        }
        public void RestoreSkinOverlap(){
            foreach(var arm in arms){
                var mesh=Instantiate(arm.sharedMesh);mesh.name=arm.sharedMesh.name+"_continuousOverlap";
                // El generador había eliminado la geometría del extremo al desvanecerlo.
                // Recuperarla permite fundir sobre piel, evitando el corte duro de la última fila.
                if(mesh.vertexCount!=11*37)throw new ArgumentException("La malla del brazo no corresponde a la cuadrícula esperada");
                var triangles=new List<int>();
                for(int y=0;y<36;y++)for(int x=0;x<10;x++){
                    int a=y*11+x,b=a+1,c=a+11,d=c+1;triangles.AddRange(new[]{a,c,b,b,c,d});
                }
                mesh.triangles=triangles.ToArray();mesh.RecalculateBounds();arm.sharedMesh=mesh;
            }
        }
        // Ajuste del casquete superior, sin desplazar el puño ni cambiar los huesos del usuario.
        public void AlignShoulderCaps(){
            if(shoulderSources.Length!=fronts.Length)shoulderSources=Array.ConvertAll(fronts,r=>r.sharedMesh);
            for(int side=0;side<fronts.Length;side++){
                var front=fronts[side];var mesh=Instantiate(shoulderSources[side]);mesh.name=shoulderSources[side].name+"_shoulderFit";
                var vertices=mesh.vertices;var uv=mesh.uv;
                Vector3 delta=front.transform.InverseTransformVector(body.transform.TransformVector(new Vector3(side==0?.025f:-.025f,-.07f,0)));
                for(int v=0;v<vertices.Length;v++){
                    float down=1-(uv[v].y-.544921875f)/.3310546875f;
                    float weight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,.62f,down));
                    vertices[v]+=delta*weight;
                }
                mesh.vertices=vertices;mesh.RecalculateBounds();front.sharedMesh=mesh;linings[side].sharedMesh=mesh;
            }
            shouldersAligned=true;
        }
        public void Sync(){SyncLayers();ApplyOpaque();}
        void SyncLayers(){
            for(int i=0;i<fronts.Length;i++)if(fronts[i]&&linings[i]&&arms[i]){
                // El antebrazo no debe pintar su casquete por encima del tramo superior.
                var forearm=Array.Find(body.parts,r=>r&&r.name=="Kemono_Forearm"+(i==0?"Left":"Right"));
                if(forearm&&body.torsoRenderer){
                    // Delante del bajo al descansar y al cruzar el cuerpo; detrás de la cabeza al elevarse.
                    bool behind=-body.InwardAngle(i)>65;
                    forearm.sortingOrder=body.torsoRenderer.sortingOrder+(behind?-2:1);
                    arms[i].sortingOrder=forearm.sortingOrder+1;
                    fronts[i].sortingOrder=Mathf.Max(body.torsoRenderer.sortingOrder+1,arms[i].sortingOrder+1);
                }
                // El casquete de manga pertenece al exterior de la camiseta: nunca detrás del torso.
                if(body&&body.torsoRenderer)fronts[i].sortingOrder=Mathf.Max(fronts[i].sortingOrder,Mathf.Max(body.torsoRenderer.sortingOrder+1,arms[i].sortingOrder+1));
                // El forro siempre queda detrás de la piel, también cuando el brazo pasa delante del torso.
                linings[i].sortingLayerID=fronts[i].sortingLayerID;
                linings[i].sortingOrder=Mathf.Min(fronts[i].sortingOrder-2,arms[i].sortingOrder-1);
                if(forearm)linings[i].sortingOrder=Mathf.Min(linings[i].sortingOrder,forearm.sortingOrder-1);
                linings[i].sharedMesh=fronts[i].sharedMesh;
                linings[i].sharedMaterial.SetFloat("_Volume",fronts[i].sharedMaterial.GetFloat("_Volume"));
                linings[i].enabled=fronts[i].enabled;
                // Abertura geométrica: no clasificar píxeles por color (produce motas en el antialias).
                var texture=fronts[i].sharedMaterial.mainTexture;
                var opening=new Vector4((i==0?200:1336)/(float)texture.width,1-399f/texture.height,.831f,i==0?-.556f:.556f);
                foreach(var material in new[]{fronts[i].sharedMaterial,linings[i].sharedMaterial}){
                    material.SetVector("_SleeveOpening",opening);
                    material.SetVector("_SleeveRadii",new Vector4(120,28,0,0));
                    material.SetFloat("_SleeveTube",tubeCorrected?1:0);
                }
                if(tubeCorrected){
                    foreach(var material in new[]{fronts[i].sharedMaterial,linings[i].sharedMaterial})material.SetVector("_SleeveRadii",new Vector4(124,21,0,0));
                    fronts[i].sharedMaterial.SetFloat("_VertexAlpha",0);linings[i].sharedMaterial.SetFloat("_VertexAlpha",0);
                    fronts[i].sharedMaterial.SetFloat("_JoinStrength",0);linings[i].sharedMaterial.SetFloat("_JoinStrength",0);
                    arms[i].sharedMaterial.SetFloat("_SkinOpaque",1);
                    arms[i].sharedMaterial.SetFloat("_SkinColorJoin",1);
                    arms[i].sharedMaterial.SetFloat("_SkinUnderCenter",i==0?.79329427f:.19563802f);
                    arms[i].sharedMaterial.SetVector("_JoinRect",i==0?new Vector4(.13541667f,.56640625f,.15690104f,.4169921875f):new Vector4(.4251302f,.56640625f,.15625f,.4169921875f));
                }
                if(joinsIntegrated&&!tubeCorrected){
                    var material=fronts[i].sharedMaterial;
                    material.SetFloat("_JoinMode",i==0?1:-1);
                    // Al alejar la manga del torso se retira el fundido para no agujerear la silueta.
                    float outward=Mathf.Max(0,-body.InwardAngle(i));
                    material.SetFloat("_JoinStrength",1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(15,65,outward)));
                    material.SetVector("_JoinRect",new Vector4(i==0?.06770833f:.74739583f,.544921875f,.185546875f,.3310546875f));
                    if(forearm){
                        forearm.sharedMaterial.SetFloat("_JoinMode",0);forearm.sharedMaterial.SetFloat("_JoinStrength",0);
                        forearm.sharedMaterial.SetVector("_JoinRect",i==0?new Vector4(.7376302f,.55078125f,.111328125f,.43359375f):new Vector4(.13997396f,.060546875f,.111328125f,.4375f));
                    }
                    arms[i].sharedMaterial.SetFloat("_JoinMode",3);arms[i].sharedMaterial.SetFloat("_JoinStrength",1);
                    arms[i].sharedMaterial.SetVector("_JoinRect",i==0?new Vector4(.13541667f,.56640625f,.15690104f,.4169921875f):new Vector4(.4251302f,.56640625f,.15625f,.4169921875f));
                }else{
                    // Sin fundido interno: la manga es opaca y el extremo del brazo bajo ella sigue oculto, así no se ven
                    // capas superpuestas (torso y brazo) a través de la manga.
                    fronts[i].sharedMaterial.SetFloat("_JoinStrength",0);arms[i].sharedMaterial.SetFloat("_JoinStrength",0);
                    if(forearm)forearm.sharedMaterial.SetFloat("_JoinStrength",0);
                }
            }
        }
        // Abertura ajustada al interior oscuro del puño dibujado: sólo ese hueco se recorta (el forro oscuro va detrás
        // del brazo y la tela, incluido el borde del puño, delante). Así el brazo se ve dentro del tubo de la manga,
        // sin transparencias en la tela. Los radios originales (120×28 px) también recortaban el borde del puño.
        public bool opaqueSleeves=true,tubeCorrected;
        public Vector2 openingRadii=new Vector2(100,19);
        public void ApplyOpaque(){
            if(!opaqueSleeves)return;
            for(int i=0;i<fronts.Length;i++){
                var radii=tubeCorrected?new Vector4(124,21,0,0):new Vector4(openingRadii.x,openingRadii.y,0,0);
                if(fronts[i]){fronts[i].sharedMaterial.SetFloat("_SleeveSplit",-1);fronts[i].sharedMaterial.SetVector("_SleeveRadii",radii);}
                if(linings[i]){linings[i].enabled=fronts[i]&&fronts[i].enabled;linings[i].sharedMaterial.SetFloat("_SleeveSplit",1);linings[i].sharedMaterial.SetVector("_SleeveRadii",radii);}
            }
        }
        void LateUpdate(){Sync();}
        public static SleeveSeams Install(KemonoBodyRig body){
            if(body.GetComponent<SleeveSeams>())throw new ArgumentException("Las mangas ya están separadas");
            var result=body.gameObject.AddComponent<SleeveSeams>();result.body=body;
            var fronts=new List<SkinnedMeshRenderer>();var backs=new List<SkinnedMeshRenderer>();var arms=new List<SkinnedMeshRenderer>();
            for(int i=0;i<2;i++){
                string label=i==0?"Left":"Right",side=i==0?"Right":"Left";
                var front=Array.Find(body.parts,r=>r.name=="Kemono_Sleeve"+label);var arm=Array.Find(body.parts,r=>r.name=="Kemono_UpperArm"+label);
                if(!front||!arm)throw new ArgumentException("Faltan manga y brazo "+label);
                front.sharedMesh=ConcentrateRoot(front,side,result.rootInner,result.rootOuter);
                arm.sharedMesh=ConcentrateRoot(arm,side,result.rootInner,result.rootOuter);
                front.sharedMaterial=new Material(front.sharedMaterial){name="SleeveFront_"+label};
                front.sharedMaterial.SetFloat("_SleeveSplit",-1);
                var texture=front.sharedMaterial.mainTexture;
                // Apertura dibujada en el atlas actual; valores en píxeles para no depender de la escala del sprite.
                var opening=new Vector4((i==0?202:1334)/(float)texture.width,1-393f/texture.height,.78f,i==0?-.6258f:.6258f);
                front.sharedMaterial.SetVector("_SleeveOpening",opening);
                front.sharedMaterial.SetVector("_SleeveRadii",new Vector4(115,26,0,0));
                front.sharedMaterial.SetVector("_SleeveDarkLimit",new Vector4(.14f,.26f,.42f,0));
                var go=new GameObject("Kemono_SleeveLining"+label);go.transform.SetParent(front.transform,false);
                var back=go.AddComponent<SkinnedMeshRenderer>();back.sharedMesh=front.sharedMesh;back.bones=front.bones;back.rootBone=front.rootBone;back.quality=front.quality;back.localBounds=front.localBounds;back.updateWhenOffscreen=true;
                back.sharedMaterial=new Material(front.sharedMaterial){name="SleeveLining_"+label};back.sharedMaterial.SetFloat("_SleeveSplit",1);
                fronts.Add(front);backs.Add(back);arms.Add(arm);
            }
            result.fronts=fronts.ToArray();result.linings=backs.ToArray();result.arms=arms.ToArray();result.joinsIntegrated=false;result.tubeCorrected=true;result.RestoreSkinOverlap();result.PrepareOpaqueSkin();result.AlignShoulderCaps();result.Sync();return result;
        }
        // La tela lejos de la sisa mantiene volumen. Se conservan pesos del codo, muñeca y muelles.
        // Sólo se redistribuye la influencia de hombro a brazo en el entorno de la manga.
        static Mesh ConcentrateRoot(SkinnedMeshRenderer renderer,string side,float inner,float outer){
            int upper=Array.FindIndex(renderer.bones,b=>b&&b.name==side+"UpperArm");if(upper<0)throw new ArgumentException("Sin brazo superior");
            var original=renderer.sharedMesh;var mesh=Instantiate(original);mesh.name=original.name+"_seamRoot";
            Vector3 pivot=original.bindposes[upper].inverse.MultiplyPoint3x4(Vector3.zero);
            var vertices=mesh.vertices;var weights=mesh.boneWeights;
            for(int i=0;i<weights.Length;i++){
                float transfer=Mathf.SmoothStep(0,1,Mathf.InverseLerp(inner,outer,Vector3.Distance(vertices[i],pivot)));
                var w=weights[i];var ids=new[]{w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};var values=new[]{w.weight0,w.weight1,w.weight2,w.weight3};
                var merged=new Dictionary<int,float>();
                void Add(int id,float value){if(value<=0)return;merged.TryGetValue(id,out float old);merged[id]=old+value;}
                for(int j=0;j<4;j++){
                    int id=ids[j];string name=id<renderer.bones.Length&&renderer.bones[id]?renderer.bones[id].name:"";
                    bool shoulder=name==side+"Shoulder"||name.StartsWith(side+"ShoulderBlend");
                    float move=shoulder?values[j]*transfer:0;Add(id,values[j]-move);Add(upper,move);
                }
                var pairs=new List<KeyValuePair<int,float>>(merged);pairs.Sort((a,b)=>b.Value.CompareTo(a.Value));if(pairs.Count>4)pairs.RemoveRange(4,pairs.Count-4);
                float sum=0;foreach(var pair in pairs)sum+=pair.Value;var packed=new BoneWeight();
                for(int j=0;j<pairs.Count;j++){int id=pairs[j].Key;float value=pairs[j].Value/sum;
                    if(j==0){packed.boneIndex0=id;packed.weight0=value;}else if(j==1){packed.boneIndex1=id;packed.weight1=value;}else if(j==2){packed.boneIndex2=id;packed.weight2=value;}else{packed.boneIndex3=id;packed.weight3=value;}}
                weights[i]=packed;
            }
            mesh.boneWeights=weights;return mesh;
        }
    }
}
