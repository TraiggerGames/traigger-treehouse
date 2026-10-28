using System;
using System.Collections.Generic;
using UnityEngine;
namespace Traigger {
    // Mejora temporal de las mallas existentes: nunca reconstruye huesos ni modifica assets de reposo.
    // Conserva exclusivamente el atlas original: no sustituye dibujos ni añade poses de dedos inventadas.
    public sealed class BodyRigUpgrade : IDisposable {
        readonly AvatarRig rig;
        readonly Vector3[] anchors;
        readonly List<SkinnedMeshRenderer> originals=new List<SkinnedMeshRenderer>();
        readonly List<Mesh> oldMeshes=new List<Mesh>(),meshes=new List<Mesh>();
        readonly List<Material> oldMaterials=new List<Material>(),materials=new List<Material>();
        readonly List<int> oldOrders=new List<int>();
        readonly List<bool> oldEnabled=new List<bool>();
        public BodyRigUpgrade(AvatarRig avatar){
            rig=avatar;
            anchors=new Vector3[rig.bones.Length];
            Matrix4x4[] bind=rig.layers[0].sharedMesh.bindposes;
            for(int i=0;i<anchors.Length;i++)anchors[i]=i<bind.Length?bind[i].inverse.MultiplyPoint3x4(Vector3.zero):Vector3.zero;
            foreach(var r in rig.layers){
                if(!r||!r.sharedMesh)continue;
                originals.Add(r);oldMeshes.Add(r.sharedMesh);oldMaterials.Add(r.sharedMaterial);oldEnabled.Add(r.enabled);oldOrders.Add(r.sortingOrder);
                var mesh=UnityEngine.Object.Instantiate(r.sharedMesh);mesh.name=r.sharedMesh.name+"_body_runtime";
                // Calcular en el espacio original de la malla, usando bind poses reales del usuario.
                var v=mesh.vertices;var weights=mesh.boneWeights;
                for(int i=0;i<v.Length;i++){
                    if(r.name=="ArmLeft")weights[i]=ArmWeight(v[i],"Right");
                    else if(r.name=="ArmRight")weights[i]=ArmWeight(v[i],"Left");
                    else if(r.name=="LegLeft")weights[i]=LegWeight(v[i],"Right");
                    else if(r.name=="LegRight")weights[i]=LegWeight(v[i],"Left");
                    else if(r.name=="Shirt")weights[i]=ShirtWeight(v[i]);
                    else if(r.name=="Shorts")weights[i]=ShortsWeight(v[i]);
                }
                mesh.boneWeights=weights;r.sharedMesh=mesh;meshes.Add(mesh);
                var material=UnityEngine.Object.Instantiate(r.sharedMaterial);r.sharedMaterial=material;materials.Add(material);
                SetLayerRegion(material,mesh);
            }
        }
        int Index(string name)=>Array.FindIndex(rig.bones,b=>b&&b.name==name);
        Vector3 Anchor(int index){
            return index>=0&&index<anchors.Length?anchors[index]:Vector3.zero;
        }
        BoneWeight Pair(int a,int b,float t){
            if(a<0)a=Index("Hips");if(b<0)b=a;t=Mathf.Clamp01(t);
            return new BoneWeight{boneIndex0=a,boneIndex1=b,weight0=1-t,weight1=t};
        }
        float Along(Vector3 p,Vector3 a,Vector3 b){var ab=b-a;return ab.sqrMagnitude<1e-8f?0:Vector3.Dot(p-a,ab)/ab.sqrMagnitude;}
        BoneWeight Joint(Vector3 p,int proximal,int distal,int next,float width){
            if(proximal<0||distal<0)return Pair(proximal,distal,0);
            Vector3 joint=Anchor(distal);Vector3 direction=next>=0?Anchor(next)-joint:joint-Anchor(proximal);
            float distance=Vector3.Dot(p-joint,direction.normalized);
            return Pair(proximal,distal,Mathf.SmoothStep(0,1,Mathf.InverseLerp(-width,width,distance)));
        }
        BoneWeight ArmWeight(Vector3 p,string side){
            int upper=Index(side+"UpperArm"),lower=Index(side+"LowerArm"),hand=Index(side+"Hand");
            if(lower<0||hand<0)return Pair(upper,lower,0);
            float wrist=Along(p,Anchor(lower),Anchor(hand));
            return wrist>.65f?Joint(p,lower,hand,-1,.065f):Joint(p,upper,lower,hand,.10f);
        }
        BoneWeight LegWeight(Vector3 p,string side){
            int thigh=Index(side+"UpperLeg"),calf=Index(side+"LowerLeg"),foot=Index(side+"Foot"),toes=Index(side+"Toes");
            if(calf<0||foot<0)return Pair(thigh,calf,0);
            float t=Along(p,Anchor(calf),Anchor(foot));
            if(t>1.02f&&toes>=0)return Joint(p,foot,toes,-1,.035f);
            return t>.6f?Joint(p,calf,foot,toes,.075f):Joint(p,thigh,calf,foot,.10f);
        }
        BoneWeight ShirtWeight(Vector3 p){
            string side=p.x<0?"Right":"Left";int chest=Index("Chest"),shoulder=Index(side+"Shoulder"),upper=Index(side+"UpperArm");
            if(upper<0)return Pair(chest,chest,0);
            float lateral=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.26f,.57f,Mathf.Abs(p.x)));
            float sleeve=Mathf.SmoothStep(0,1,Mathf.InverseLerp(2.20f,2.55f,p.y));
            var w=Pair(chest,upper,lateral*sleeve);
            // Una pequeña transición sobre el hombro evita cortar la manga en un único pivote.
            if(shoulder>=0){float share=w.weight1*.22f;w.weight1-=share;w.boneIndex2=shoulder;w.weight2=share;}
            return w;
        }
        BoneWeight ShortsWeight(Vector3 p){
            int hips=Index("Hips"),thigh=Index((p.x<0?"Right":"Left")+"UpperLeg");
            float down=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.35f,1.96f,p.y));
            float split=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.03f,.18f,Mathf.Abs(p.x)));
            return Pair(hips,thigh,down*split);
        }
        static void SetLayerRegion(Material mat,Mesh mesh){
            var uv=mesh.uv;if(uv.Length==0)return;Vector2 min=uv[0],max=uv[0];foreach(var u in uv){min=Vector2.Min(min,u);max=Vector2.Max(max,u);}
            mat.SetVector("_LayerRect",new Vector4(min.x,min.y,Mathf.Max(.001f,max.x-min.x),Mathf.Max(.001f,max.y-min.y)));
        }
        public void SetVolume(float amount){
            for(int i=0;i<originals.Count;i++){
                string name=originals[i].name;float value=(name.StartsWith("Eye")||name=="Mouth")?0:amount;
                originals[i].sharedMaterial.SetFloat("_Volume",value);
            }

        }
        static void Release(UnityEngine.Object obj){if(!obj)return;if(Application.isPlaying)UnityEngine.Object.Destroy(obj);else UnityEngine.Object.DestroyImmediate(obj);}
        public void Dispose(){
            for(int i=0;i<originals.Count;i++)if(originals[i]){originals[i].sharedMesh=oldMeshes[i];originals[i].sharedMaterial=oldMaterials[i];originals[i].enabled=oldEnabled[i];originals[i].sortingOrder=oldOrders[i];}
            foreach(var m in meshes)Release(m);foreach(var m in materials)Release(m);
        }
    }
}
