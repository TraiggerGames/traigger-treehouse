using System;
using System.Collections.Generic;
using UnityEngine;
namespace Traigger {
    // Construye mallas subdivididas, bind poses y pesos normalizados de hasta cuatro huesos.
    // Las texturas deben contener transparencia; no se intenta inventar zonas ocultas del dibujo.
    public static class AutoRig {
        public static void Validate(RigDefinition d) {
            if (!d || d.bones.Length==0) throw new ArgumentException("Faltan huesos");
            var names=new HashSet<string>();
            for(int i=0;i<d.bones.Length;i++) {
                var b=d.bones[i];
                if(string.IsNullOrWhiteSpace(b.name)||!names.Add(b.name)||b.parent < -1||b.parent>=i)
                    throw new ArgumentException("Nombres únicos y padres anteriores al hijo: "+i);
            }
            foreach(var l in d.layers) {
                if(l.rect.width<=0||l.rect.height<=0||l.columns<2||l.columns>40||l.rows<2||l.rows>40||l.bones==null||l.bones.Length==0)
                    throw new ArgumentException("Capa no válida: "+l.name);
                foreach(int b in l.bones) if(b<0||b>=d.bones.Length) throw new ArgumentException("Índice de hueso: "+l.name);
            }
        }
        public static AvatarRig Build(RigDefinition d, Transform parent=null) {
            Validate(d);
            var root=new GameObject("Avatar — "+d.name); root.transform.SetParent(parent,false);
            var rig=root.AddComponent<AvatarRig>(); rig.definition=d; rig.ownsResources=Application.isPlaying;
            rig.bones=new Transform[d.bones.Length];
            for(int i=0;i<rig.bones.Length;i++) {
                var spec=d.bones[i]; var b=new GameObject(spec.name).transform;
                b.SetParent(spec.parent<0?root.transform:rig.bones[spec.parent],false);
                Vector2 parentPosition=spec.parent<0?Vector2.zero:d.bones[spec.parent].position;
                b.localPosition=spec.position-parentPosition; rig.bones[i]=b;
            }
            var renderers=new List<SkinnedMeshRenderer>();
            foreach(var l in d.layers) {
                var go=new GameObject(l.name); go.transform.SetParent(root.transform,false);
                var r=go.AddComponent<SkinnedMeshRenderer>();
                r.sharedMesh=MeshFor(d,l); r.bones=rig.bones; r.rootBone=rig.bones[0];
                var shader=d.layerShader ? d.layerShader : Shader.Find("Traigger/UnlitAlpha");
                r.sharedMaterial=new Material(shader); r.sharedMaterial.mainTexture=l.texture;
                r.sharedMaterial.color=l.tint; r.sortingOrder=l.order;
                r.quality=SkinQuality.Bone4; r.updateWhenOffscreen=true;
                r.localBounds=new Bounds(new Vector3(0,2,0),new Vector3(12,12,4));
                renderers.Add(r);
            }
            rig.layers=renderers.ToArray();
            for(int i=0;i<d.layers.Length;i++)if(d.layers[i].face==FaceDeform.Mouth){
                int head=Array.FindIndex(d.bones,b=>b.name=="Head");
                if(head<0)break;
                var line=new GameObject("Closed mouth — neutral").AddComponent<LineRenderer>();
                line.transform.SetParent(rig.bones[head],false);line.useWorldSpace=false;line.positionCount=33;
                line.widthMultiplier=.007f;line.numCapVertices=4;line.numCornerVertices=3;
                line.sharedMaterial=new Material(d.layerShader?d.layerShader:Shader.Find("Traigger/UnlitAlpha"));
                line.sharedMaterial.color=new Color(.20f,.14f,.08f,1);line.sortingOrder=d.layers[i].order;
                var rect=d.layers[i].rect;
                for(int j=0;j<33;j++){
                    float t=j/32f;
                    line.SetPosition(j,new Vector3(rect.center.x+(t-.5f)*rect.width,
                        rect.center.y-.016f*Mathf.Abs(Mathf.Sin(t*Mathf.PI*2)),0)-(Vector3)d.bones[head].position);
                }
                rig.closedMouth=line;rig.layers[i].enabled=false;
                break;
            }
            rig.CaptureRest(); return rig;
        }
        public static Mesh MeshFor(RigDefinition d, LayerSpec l) {
            int nx=l.columns,ny=l.rows; var vertices=new Vector3[(nx+1)*(ny+1)];
            var uv=new Vector2[vertices.Length]; var weights=new BoneWeight[vertices.Length];
            var triangles=new List<int>();
            for(int y=0;y<=ny;y++) for(int x=0;x<=nx;x++) {
                int i=y*(nx+1)+x; var u=new Vector2(x/(float)nx,y/(float)ny);
                vertices[i]=new Vector3(l.rect.x+u.x*l.rect.width,l.rect.y+u.y*l.rect.height,0);
                float cheekBand=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,.28f,Mathf.Abs(vertices[i].y-3.45f)));
                float cheekEdge=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.78f,.99f,Mathf.Abs(u.x-.5f)*2));
                vertices[i].x+=Mathf.Sign(vertices[i].x)*l.cheekSpread*cheekBand*cheekEdge;
                float neckT=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.78f,1,u.y));
                float centerT=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.10f,.22f,Mathf.Abs(u.x-.5f)));
                vertices[i].y+=l.topCenterExtension*neckT*centerT;
                uv[i]=new Vector2(l.uvRect.x+u.x*l.uvRect.width,l.uvRect.y+u.y*l.uvRect.height); weights[i]=WeightAt(d,l,vertices[i]);
                if(x<nx&&y<ny) {
                    float cx=(x+.5f)/nx,cy=(y+.5f)/ny;
                    bool notch=l.bottomCenterCutout.x>0 && Mathf.Abs(cx-.5f)<l.bottomCenterCutout.x*.5f && cy<l.bottomCenterCutout.y;
                    var side=l.sideCutout;
                    bool sideCut=side.x>0 && Mathf.Min(cx,1-cx)<side.x && cy>side.y && cy<side.z;
                    if(!notch&&!sideCut){int a=i,b=i+1,c=i+nx+1,e=c+1;triangles.AddRange(new[]{a,c,b,b,c,e});}
                }
            }
            var mesh=new Mesh { name=l.name+"_deformable" };
            mesh.vertices=vertices; mesh.uv=uv; mesh.triangles=triangles.ToArray(); mesh.boneWeights=weights;
            var bind=new Matrix4x4[d.bones.Length];
            for(int i=0;i<bind.Length;i++) bind[i]=Matrix4x4.Translate(-(Vector3)d.bones[i].position);
            mesh.bindposes=bind;
            if(l.face!=FaceDeform.None) {
                var delta=new Vector3[vertices.Length];
                for(int i=0;i<delta.Length;i++) {
                    // La deformación se aplica a toda la malla, no a la escala del objeto.
                    float dy=vertices[i].y-l.rect.center.y;
                    delta[i]=new Vector3(l.face==FaceDeform.Mouth?(vertices[i].x-l.rect.center.x)*.15f:0,
                        dy*(l.face==FaceDeform.Mouth?2f:-.94f),0);
                }
                mesh.AddBlendShapeFrame(l.face.ToString(),100,delta,new Vector3[delta.Length],new Vector3[delta.Length]);
                if(l.face==FaceDeform.Mouth){
                    // Forma de la boca: ancha (sonrisa, «i/e») y estrecha y redonda («o/u»), alrededor de su centro.
                    var wide=new Vector3[vertices.Length];var narrow=new Vector3[vertices.Length];
                    for(int i=0;i<wide.Length;i++){float dx=vertices[i].x-l.rect.center.x,dy=vertices[i].y-l.rect.center.y;
                        wide[i]=new Vector3(dx*.45f,-dy*.25f+Mathf.Abs(dx)*.12f,0);narrow[i]=new Vector3(-dx*.45f,dy*.35f,0);}
                    mesh.AddBlendShapeFrame("MouthWide",100,wide,new Vector3[wide.Length],new Vector3[wide.Length]);
                    mesh.AddBlendShapeFrame("MouthNarrow",100,narrow,new Vector3[narrow.Length],new Vector3[narrow.Length]);
                }
            }
            AddLookShapes(mesh,l,vertices);AddHairShapes(mesh,l,vertices);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        // La raíz conserva su posición; el desplazamiento crece hacia las puntas.
        // Separar izquierda/derecha/flequillo evita girar toda la cabellera como una tabla.
        static void AddHairShapes(Mesh mesh,LayerSpec l,Vector3[] vertices){
            if(l.name!="FrontHair"&&l.name!="BackHair")return;
            string[] groups=l.name=="BackHair"?new[]{"HairBack"}:new[]{"HairLeft","HairRight","HairFringe"};
            foreach(string group in groups)for(int direction=0;direction<2;direction++){
                float sign=direction==0?1:-1;var delta=new Vector3[vertices.Length];
                for(int i=0;i<vertices.Length;i++){
                    var v=vertices[i];float tips=Mathf.Pow(Mathf.Clamp01((4.15f-v.y)/1.02f),1.8f);
                    float mask=1;
                    if(group=="HairLeft")mask=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.38f,-.04f,v.x));
                    if(group=="HairRight")mask=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.04f,.38f,v.x));
                    if(group=="HairFringe"){
                        mask=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.10f,.40f,Mathf.Abs(v.x)));
                        tips=Mathf.Pow(Mathf.Clamp01((4.35f-v.y)/.90f),1.8f);
                    }
                    float strength=group=="HairFringe"?.080f:group=="HairBack"?.125f:.160f;
                    delta[i]=new Vector3(sign*strength*tips*mask,sign*v.x*.040f*tips*mask,0);
                }
                mesh.AddBlendShapeFrame(group+(direction==0?"Positive":"Negative"),100,delta,new Vector3[vertices.Length],new Vector3[vertices.Length]);
            }
        }
        // Giro 2,5D limitado: se deforma la malla antes del skinning, conservando la textura.
        // No reconstruye una vista de perfil ni superficies que no están dibujadas.
        static void AddLookShapes(Mesh mesh,LayerSpec l,Vector3[] vertices){
            bool cheek=l.name=="CheekLeft" || l.name=="CheekRight";
            bool face=l.name=="HeadBase" || cheek || l.face!=FaceDeform.None || l.name=="Eyebrows";
            bool hair=l.name=="FrontHair" || l.name=="BackHair";
            bool ear=l.name=="EarLeft" || l.name=="EarRight";
            if(!face&&!hair&&!ear)return;
            for(int channel=0;channel<4;channel++){
                float sign=channel%2==0?1:-1;var delta=new Vector3[vertices.Length];
                for(int i=0;i<vertices.Length;i++){
                    var v=vertices[i];float nx=Mathf.Clamp(v.x/.70f,-1,1);
                    if(channel<2){
                        float dx=0;
                        if(l.name=="HeadBase" || cheek)dx=sign*.145f*(1-nx*nx)-v.x*.045f;
                        else if(l.face==FaceDeform.BlinkLeft||l.face==FaceDeform.BlinkRight){
                            float side=l.rect.center.x<0?-1:1;
                            dx=sign*.135f+(v.x-l.rect.center.x)*(side*sign<0?-.18f:.035f);
                        }else if(l.name=="BackHair")dx=-sign*.055f-v.x*.045f;
                        else if(l.name=="FrontHair")dx=sign*.050f-v.x*.035f;
                        else if(ear)dx=sign*.050f-v.x*.035f; // misma paralaje que el pelo que tapa su base
                        else dx=sign*.140f;
                        delta[i]=new Vector3(dx,face?sign*nx*.014f:0,0);
                    }else{
                        float dy=face?sign*.036f:hair||ear?sign*.014f:sign*.022f; // los rasgos se desplazan dentro de la cabeza: más recorrido sin tocar el cuello
                        if(l.name=="HeadBase" || cheek)dy*=1-Mathf.Abs(nx)*.4f;
                        delta[i]=new Vector3(0,dy,0);
                    }
                }
                mesh.AddBlendShapeFrame(new[]{"TurnRight","TurnLeft","NodUp","NodDown"}[channel],100,delta,new Vector3[vertices.Length],new Vector3[vertices.Length]);
            }
        }
        public static BoneWeight WeightAt(RigDefinition d,LayerSpec l,Vector2 point) {
            var candidates=new List<(int index,float weight)>();
            foreach(int index in l.bones) {
                var a=d.bones[index].position; var end=a; bool found=false;
                // Distancia al segmento del hueso: se prefiere el hijo dentro de la capa y, si no hay,
                // cualquier hijo. Así una manga sigue a todo el brazo y no a un punto en el hombro.
                foreach(int other in l.bones) if(d.bones[other].parent==index) { end=d.bones[other].position; found=true; break; }
                if(!found) for(int other=index+1;other<d.bones.Length;other++) if(d.bones[other].parent==index) { end=d.bones[other].position; break; }
                var ab=end-a; float raw=ab.sqrMagnitude<1e-6f?0:Vector2.Dot(point-a,ab)/ab.sqrMagnitude; float t=Mathf.Clamp01(raw);
                float dist=Vector2.Distance(point,a+t*ab);
                float weight=1/Mathf.Pow(Mathf.Max(.04f,dist),l.falloff);
                // Lo que queda por detrás de la articulación pertenece al padre (cintura, hombro, codo, rodilla).
                int parent=d.bones[index].parent;
                if(parent>=0&&Array.IndexOf(l.bones,parent)>=0&&raw<0){
                    float behind=-raw*ab.magnitude;
                    weight*=Mathf.Lerp(1,.03f,Mathf.SmoothStep(0,1,behind/.12f));
                }
                candidates.Add((index,weight));
            }
            candidates.Sort((a,b)=>b.weight.CompareTo(a.weight));
            int count=Math.Min(4,candidates.Count); float sum=0;
            for(int i=0;i<count;i++) sum+=candidates[i].weight;
            var ids=new int[4];var w=new float[4];
            for(int i=0;i<count;i++){ids[i]=candidates[i].index;w[i]=candidates[i].weight/sum;}
            return new BoneWeight {boneIndex0=ids[0],boneIndex1=ids[1],boneIndex2=ids[2],boneIndex3=ids[3],weight0=w[0],weight1=w[1],weight2=w[2],weight3=w[3]};
        }
    }
}
