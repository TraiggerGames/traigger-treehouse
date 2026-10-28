using System;using System.Collections.Generic;using UnityEngine;
namespace Traigger {
 // Cada mechón tiene un sprite, una malla deformable y dos muelles: cuerpo y punta.
 // El dibujo original sirve de registro; nunca se recalculan los pesos de los huesos.
 public class HairDetailRig:MonoBehaviour {
  public AvatarRig rig;
  [Range(0,.8f)]public float sharpness=0;
  [Range(0,1)]public float gradient=0;
  [Range(0,2)]public float crownFreedom=1;
  [Range(0,2)]public float fringeFreedom=1;
  // Calibración (interfaz: Ajustar → Pelo; se guarda): rigidez y amortiguación de los muelles, cuánto se curva la
  // punta, cuánto reacciona a girar / inclinar / desplazarse, y el escalonado entre mechones vecinos.
  [Range(.3f,2.5f)]public float stiffness=1;
  [Range(.3f,2.5f)]public float damping=1;
  [Range(0,2)]public float tipFreedom=1;
  [Range(0,2)]public float turnResponse=1,tiltResponse=1,swayResponse=1,nodResponse=1;
  [Range(0,1)]public float variation=.5f;
  [Serializable] public class StrandDefinition {
   public string name,resource;public float[] rect,root,tip;public float angle,mass;public int order,ownedPixels;public bool crown;
  }
  [Serializable] class HairManifest {public int version;public float[] sourceSize;public StrandDefinition[] strands;}
  class Layer {public string name;public SkinnedMeshRenderer renderer;public Mesh original,copy;public Material originalMaterial,material;public SkinnedMeshRenderer originalRenderer;public bool originalEnabled;public int index;}
  class Strand {
   public StrandDefinition definition;public SkinnedMeshRenderer renderer,source;public Mesh mesh;public Material material;
   public Vector2[] sourceUV;public int originalShapes,swingPositive,swingNegative,tipPositive,tipNegative;
   public float angle,velocity,tipAngle,tipVelocity;
  }
  readonly List<Layer> layers=new List<Layer>();readonly List<Strand> strands=new List<Strand>();
  float sourceWidth=2508,sourceHeight=2508;
  public float PeakMotion {get;private set;}
  public int SeparatedLockCount=>strands.Count;
  public float MaxStrandAngle {get;private set;}
  public void Initialize(){
   if(layers.Count>0||!rig)return;
   for(int i=0;i<rig.layers.Length;i++){
    string name=rig.definition.layers[i].name;if(name!="FrontHair"&&name!="BackHair")continue;
    var r=rig.layers[i];var layer=new Layer{name=name,renderer=r,original=r.sharedMesh,originalMaterial=r.sharedMaterial,index=i};
    layer.material=new Material(r.sharedMaterial){name=name+" · pelo original por capas"};r.sharedMaterial=layer.material;
    bool atlas=r.sharedMaterial.mainTexture&&r.sharedMaterial.mainTexture.width==r.sharedMaterial.mainTexture.height;
    var painted=Resources.Load<Texture2D>("Hair/"+(name=="FrontHair"?"FrontHairHD":atlas?"BackHairAtlasHD":"BackHairHD"));
    if(painted){layer.material.mainTexture=painted;layer.material.SetFloat("_HeadShadeStrength",0);}
    layer.material.SetFloat("_HairSharpness",0);layer.material.SetFloat("_HairGradient",0);
    if(name=="FrontHair"){
     layer.copy=Subdivide(layer.original);var second=Subdivide(layer.copy);Remove(layer.copy);layer.copy=second;
     layer.copy.name=layer.original.name+" · raíces comunes";
     var strandShader=Shader.Find("Traigger/HairStrand");
     if(strandShader){layer.material.shader=strandShader;layer.material.SetTexture("_HairSource",painted);layer.material.SetVector("_AtlasRect",new Vector4(0,0,1,1));}
     // Un renderer nuevo evita reutilizar el buffer de skinning de la malla pequeña original.
     // Unity 6.5 conserva ese buffer en algunos cambios de malla en el mismo renderer.
     var capObject=new GameObject("FrontHair · raíces y costuras");capObject.transform.SetParent(r.transform.parent,false);
     capObject.transform.localPosition=r.transform.localPosition;capObject.transform.localRotation=r.transform.localRotation;capObject.transform.localScale=r.transform.localScale;
     var cap=capObject.AddComponent<SkinnedMeshRenderer>();cap.bones=r.bones;cap.rootBone=r.rootBone;cap.quality=SkinQuality.Bone4;cap.updateWhenOffscreen=true;
     cap.sortingLayerID=r.sortingLayerID;cap.sortingOrder=r.sortingOrder;cap.sharedMaterial=layer.material;cap.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;cap.receiveShadows=false;
     layer.originalRenderer=r;layer.originalEnabled=r.enabled;r.enabled=false;rig.layers[i]=cap;layer.renderer=cap;r=cap;
     // La superficie continua comparte TODOS los correctivos de las piezas: nunca es una
     // copia estática. Mantiene opacas las uniones y conserva el antialias original exterior.
     var seamBacking=Resources.Load<Texture2D>("Hair/FrontHairBaseHD");
     if(painted&&seamBacking&&CreateStrands(layer))layer.material.mainTexture=seamBacking;
     // Asignar una sola vez, después de añadir todos los canales: Unity cachea el formato de skinning.
     r.sharedMesh=layer.copy;r.localBounds=layer.copy.bounds;
    }
    layers.Add(layer);
   }
  }
  bool CreateStrands(Layer front){
   var json=Resources.Load<TextAsset>("Hair/FrontHairLayers");if(!json)return false;
   var manifest=JsonUtility.FromJson<HairManifest>(json.text);
   if(manifest==null||manifest.version!=3||manifest.strands==null)return false;
   // Verificar todo primero: si faltase un sprite, conservar el pelo completo.
   var sprites=new Sprite[manifest.strands.Length];
   for(int i=0;i<sprites.Length;i++){sprites[i]=Resources.Load<Sprite>(manifest.strands[i].resource);if(!sprites[i]){Debug.LogError("Falta el sprite del mechón: "+manifest.strands[i].resource);return false;}}
   sourceWidth=manifest.sourceSize[0];sourceHeight=manifest.sourceSize[1];
   var labels=Resources.Load<Texture2D>("Hair/FrontHairOwnership");
   if(!labels||!labels.isReadable){Debug.LogError("Falta el mapa legible de uniones del pelo");return false;}
   var field=BuildSeamField(front.copy.uv,labels,manifest.strands.Length);
   for(int k=0;k<manifest.strands.Length;k++)AddPhysicsShapes(front.copy,front.copy.uv,manifest.strands[k],field,k);

   for(int i=0;i<sprites.Length;i++){
    var def=manifest.strands[i];var mesh=CropMesh(front.copy,def.rect,sourceWidth,sourceHeight);
    var sourceUV=mesh.uv;
    var uv=(Vector2[])sourceUV.Clone();
    for(int n=0;n<uv.Length;n++)uv[n]=new Vector2((uv[n].x*sourceWidth-def.rect[0])/def.rect[2],1-((1-uv[n].y)*sourceHeight-def.rect[1])/def.rect[3]);
    mesh.uv=uv;mesh.name="FrontHair"+def.name+" · malla del mechón";
    var go=new GameObject("FrontHair"+def.name);go.transform.SetParent(front.renderer.transform.parent,false);
    go.transform.localPosition=front.renderer.transform.localPosition;go.transform.localRotation=front.renderer.transform.localRotation;go.transform.localScale=front.renderer.transform.localScale;
    var renderer=go.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=mesh;renderer.bones=front.renderer.bones;renderer.rootBone=front.renderer.rootBone;
    renderer.quality=SkinQuality.Bone4;renderer.updateWhenOffscreen=true;renderer.localBounds=mesh.bounds;renderer.sortingLayerID=front.renderer.sortingLayerID;
    renderer.sortingOrder=front.renderer.sortingOrder+def.order;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
    var material=new Material(front.material){name="FrontHair"+def.name+" · sprite independiente"};material.mainTexture=sprites[i].texture;
    material.SetFloat("_StrandOverlay",1);material.SetVector("_AtlasRect",new Vector4(def.rect[0]/sourceWidth,1-(def.rect[1]+def.rect[3])/sourceHeight,def.rect[2]/sourceWidth,def.rect[3]/sourceHeight));renderer.sharedMaterial=material;
    strands.Add(new Strand{definition=def,renderer=renderer,mesh=mesh,source=front.renderer,material=material,sourceUV=sourceUV,originalShapes=front.copy.blendShapeCount,
     swingPositive=mesh.GetBlendShapeIndex("Strand"+def.name+"LockPositive"),swingNegative=mesh.GetBlendShapeIndex("Strand"+def.name+"LockNegative"),tipPositive=mesh.GetBlendShapeIndex("Strand"+def.name+"TipPositive"),tipNegative=mesh.GetBlendShapeIndex("Strand"+def.name+"TipNegative")});
   }
   return strands.Count==manifest.strands.Length;
  }
  // Inercia ligada al movimiento real: sin oscilación artificial en reposo.
  public void Step(float rollSpeed,float yawSpeed,float pitchSpeed,float sideSpeed,float physics,float dt){
   Initialize();dt=Mathf.Clamp(dt,0,.05f);PeakMotion=0;MaxStrandAngle=0;
   foreach(var layer in layers)if(layer.name=="FrontHair"&&strands.Count>0)
    for(int s=0;s<layer.original.blendShapeCount;s++)if(layer.original.GetBlendShapeName(s).StartsWith("Hair",StringComparison.Ordinal))layer.renderer.SetBlendShapeWeight(s,0);
   int steps=Mathf.Max(1,Mathf.CeilToInt(dt/.005f));float h=dt/steps;
   for(int i=0;i<strands.Count;i++){
    var strand=strands[i];var def=strand.definition;float freedom=def.crown?crownFreedom:fringeFreedom;
    float response=1+(i%3-1)*.14f*variation;
    float target=Mathf.Clamp((-rollSpeed*.034f*tiltResponse-yawSpeed*.026f*turnResponse-sideSpeed*.62f*swayResponse+pitchSpeed*(i%2==0?.010f:-.008f)*nodResponse)*physics*freedom*response,-1,1);
    float mass=Mathf.Max(.3f,def.mass),stiffness=72/mass*this.stiffness,damping=12/Mathf.Sqrt(mass)*this.damping;
    for(int n=0;n<steps;n++){
     strand.velocity+=(stiffness*(target-strand.angle)-damping*strand.velocity)*h;
     strand.angle+=strand.velocity*h;
     float tipTarget=Mathf.Clamp((strand.angle*.55f+strand.velocity*.08f)*tipFreedom,-1,1);
     strand.tipVelocity+=(100/mass*this.stiffness*(tipTarget-strand.tipAngle)-13/Mathf.Sqrt(mass)*this.damping*strand.tipVelocity)*h;
     strand.tipAngle+=strand.tipVelocity*h;
    }
    strand.angle=Mathf.Clamp(strand.angle,-1.15f,1.15f);strand.tipAngle=Mathf.Clamp(strand.tipAngle,-1.15f,1.15f);
    strand.source.SetBlendShapeWeight(strand.swingPositive,Mathf.Max(0,strand.angle)*100);strand.source.SetBlendShapeWeight(strand.swingNegative,Mathf.Max(0,-strand.angle)*100);
    strand.source.SetBlendShapeWeight(strand.tipPositive,Mathf.Max(0,strand.tipAngle)*100);strand.source.SetBlendShapeWeight(strand.tipNegative,Mathf.Max(0,-strand.tipAngle)*100);
    PeakMotion=Mathf.Max(PeakMotion,Mathf.Abs(strand.angle));MaxStrandAngle=Mathf.Max(MaxStrandAngle,Mathf.Abs(strand.angle)*def.angle);
   }
   // Todas las piezas se actualizan DESPUÉS de calcular todos los muelles. La misma coordenada
   // del atlas recibe exactamente la misma deformación, incluidas las dos caras de una costura.
   foreach(var strand in strands){for(int shape=0;shape<strand.originalShapes;shape++)strand.renderer.SetBlendShapeWeight(shape,strand.source.GetBlendShapeWeight(shape));strand.renderer.enabled=strand.source.enabled;}
  }
  float[,] BuildSeamField(Vector2[] uv,Texture2D labels,int count){
   var pixels=labels.GetPixels32();var field=new float[uv.Length,count];
   for(int i=0;i<uv.Length;i++){
    int x=Mathf.RoundToInt(uv[i].x*labels.width),y=Mathf.RoundToInt(uv[i].y*labels.height);float total=0;
    for(int dy=-54;dy<=54;dy+=9)for(int dx=-54;dx<=54;dx+=9){
     int sx=Mathf.Clamp(x+dx,0,labels.width-1),sy=Mathf.Clamp(y+dy,0,labels.height-1),owner=pixels[sy*labels.width+sx].r-1;
     if(owner<0||owner>=count)continue;float weight=Mathf.Exp(-(dx*dx+dy*dy)/1458f);field[i,owner]+=weight;total+=weight;
    }
    if(total>0)for(int k=0;k<count;k++)field[i,k]/=total;
   }
   return field;
  }
  void AddPhysicsShapes(Mesh mesh,Vector2[] uv,StrandDefinition def,float[,] field,int strandIndex){
   var root=new Vector2(def.root[0],def.root[1]);var tip=new Vector2(def.tip[0],def.tip[1]);var axis=tip-root;
   // .0045 unidades por píxel del atlas antiguo; el sprite actual tiene el doble de resolución.
   float scale=.0045f*1254/sourceWidth;var zero=new Vector3[uv.Length];
   for(int part=0;part<2;part++)for(int sign=0;sign<2;sign++){
    var delta=new Vector3[uv.Length];
    for(int i=0;i<uv.Length;i++){
     Vector2 p=new Vector2(uv[i].x*sourceWidth,(1-uv[i].y)*sourceHeight);float t=Vector2.Dot(p-root,axis)/axis.sqrMagnitude;
     // Toda raíz solapada queda inmóvil. El segundo muelle sólo curva el último tercio.
     float flex=Mathf.SmoothStep(0,1,Mathf.InverseLerp(part==0?.34f:.62f,1,t));
     Vector2 pivot=part==0?root:root+axis*.55f;
     Vector2 radius=new Vector2(p.x-pivot.x,-(p.y-pivot.y))*scale;
     float angle=(sign==0?1:-1)*def.angle*(part==0?1:.35f)*Mathf.Deg2Rad*flex*field[i,strandIndex],c=Mathf.Cos(angle),s=Mathf.Sin(angle);
     delta[i]=new Vector3(radius.x*c-radius.y*s-radius.x,radius.x*s+radius.y*c-radius.y,0);
    }
    mesh.AddBlendShapeFrame("Strand"+def.name+(part==0?"Lock":"Tip")+(sign==0?"Positive":"Negative"),100,delta,zero,zero);
   }
   mesh.RecalculateBounds();
  }
  // Recortar sólo la malla (con margen para filtrar alfa), conservando huesos y todos los correctivos.
  static Mesh CropMesh(Mesh source,float[] rect,float width,float height){
   var uv=source.uv;var tri=source.triangles;var selected=new List<int>();var map=new Dictionary<int,int>();var indices=new List<int>();
   float x0=(rect[0]-2)/width,x1=(rect[0]+rect[2]+2)/width,y0=1-(rect[1]+rect[3]+2)/height,y1=1-(rect[1]-2)/height;
   for(int i=0;i<tri.Length;i+=3){var a=uv[tri[i]];var b=uv[tri[i+1]];var c=uv[tri[i+2]];
    if(Mathf.Max(a.x,Mathf.Max(b.x,c.x))<x0||Mathf.Min(a.x,Mathf.Min(b.x,c.x))>x1||Mathf.Max(a.y,Mathf.Max(b.y,c.y))<y0||Mathf.Min(a.y,Mathf.Min(b.y,c.y))>y1)continue;
    for(int n=0;n<3;n++){int old=tri[i+n];if(!map.TryGetValue(old,out int id)){id=selected.Count;map.Add(old,id);selected.Add(old);}indices.Add(id);}
   }
   var vertices=source.vertices;var weights=source.boneWeights;var positions=new Vector3[selected.Count];var tex=new Vector2[selected.Count];var bones=new BoneWeight[selected.Count];
   for(int i=0;i<selected.Count;i++){positions[i]=vertices[selected[i]];tex[i]=uv[selected[i]];bones[i]=weights[selected[i]];}
   var mesh=new Mesh{indexFormat=selected.Count>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16,vertices=positions,uv=tex,boneWeights=bones,bindposes=source.bindposes};
   if(source.colors.Length==vertices.Length){var colors=new Color[selected.Count];for(int i=0;i<colors.Length;i++)colors[i]=source.colors[selected[i]];mesh.colors=colors;}
   if(source.uv2.Length==vertices.Length){var second=new Vector2[selected.Count];for(int i=0;i<second.Length;i++)second[i]=source.uv2[selected[i]];mesh.uv2=second;}
   mesh.SetTriangles(indices,0);
   for(int shape=0;shape<source.blendShapeCount;shape++)for(int frame=0;frame<source.GetBlendShapeFrameCount(shape);frame++){
    var d=new Vector3[vertices.Length];var n=new Vector3[d.Length];var t=new Vector3[d.Length];source.GetBlendShapeFrameVertices(shape,frame,d,n,t);
    var delta=new Vector3[selected.Count];var normal=new Vector3[selected.Count];var tangent=new Vector3[selected.Count];
    for(int i=0;i<selected.Count;i++){delta[i]=d[selected[i]];normal[i]=n[selected[i]];tangent[i]=t[selected[i]];}
    mesh.AddBlendShapeFrame(source.GetBlendShapeName(shape),source.GetBlendShapeFrameWeight(shape,frame),delta,normal,tangent);
   }
   mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
  }
  // Subdivisión lineal: interpola también los correctivos, nunca recalcula los pesos desde cero.
  public static Mesh Subdivide(Mesh source){
   var vertices=new List<Vector3>(source.vertices);var uv=new List<Vector2>(source.uv);var weights=new List<BoneWeight>(source.boneWeights);
   var colors=new List<Color>(source.colors);bool hasColors=colors.Count==vertices.Count;
   var uv2=new List<Vector2>(source.uv2);bool hasUV2=uv2.Count==vertices.Count;
   var pairs=new List<Vector2Int>();var edges=new Dictionary<ulong,int>();var triangles=new List<int>();
   int Mid(int a,int b){
    ulong key=((ulong)(uint)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);if(edges.TryGetValue(key,out int index))return index;
    index=vertices.Count;edges.Add(key,index);pairs.Add(new Vector2Int(a,b));vertices.Add((vertices[a]+vertices[b])*.5f);uv.Add((uv[a]+uv[b])*.5f);weights.Add(Mix(weights[a],weights[b]));if(hasColors)colors.Add((colors[a]+colors[b])*.5f);if(hasUV2)uv2.Add((uv2[a]+uv2[b])*.5f);return index;
   }
   var input=source.triangles;for(int i=0;i<input.Length;i+=3){int a=input[i],b=input[i+1],c=input[i+2],ab=Mid(a,b),bc=Mid(b,c),ca=Mid(c,a);triangles.AddRange(new[]{a,ab,ca,ab,b,bc,ca,bc,c,ab,bc,ca});}
   var mesh=new Mesh{indexFormat=vertices.Count>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);if(hasUV2)mesh.SetUVs(1,uv2);if(hasColors)mesh.SetColors(colors);mesh.boneWeights=weights.ToArray();mesh.bindposes=source.bindposes;mesh.SetTriangles(triangles,0);
   for(int shape=0;shape<source.blendShapeCount;shape++)for(int frame=0;frame<source.GetBlendShapeFrameCount(shape);frame++){
    var d=new Vector3[source.vertexCount];var n=new Vector3[d.Length];var t=new Vector3[d.Length];source.GetBlendShapeFrameVertices(shape,frame,d,n,t);
    mesh.AddBlendShapeFrame(source.GetBlendShapeName(shape),source.GetBlendShapeFrameWeight(shape,frame),Expand(d,pairs),Expand(n,pairs),Expand(t,pairs));
   }
   mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
  }
  static Vector3[] Expand(Vector3[] values,List<Vector2Int> pairs){var result=new List<Vector3>(values);foreach(var pair in pairs)result.Add((result[pair.x]+result[pair.y])*.5f);return result.ToArray();}
  static BoneWeight Mix(BoneWeight a,BoneWeight b){
   var map=new Dictionary<int,float>();void Add(int id,float w){if(w>0)map[id]=map.TryGetValue(id,out float old)?old+w*.5f:w*.5f;}
   Add(a.boneIndex0,a.weight0);Add(a.boneIndex1,a.weight1);Add(a.boneIndex2,a.weight2);Add(a.boneIndex3,a.weight3);Add(b.boneIndex0,b.weight0);Add(b.boneIndex1,b.weight1);Add(b.boneIndex2,b.weight2);Add(b.boneIndex3,b.weight3);
   var list=new List<KeyValuePair<int,float>>(map);list.Sort((x,y)=>y.Value.CompareTo(x.Value));var ids=new int[4];var ws=new float[4];float total=0;for(int i=0;i<Mathf.Min(4,list.Count);i++){ids[i]=list[i].Key;ws[i]=list[i].Value;total+=ws[i];}if(total>0)for(int i=0;i<4;i++)ws[i]/=total;
   return new BoneWeight{boneIndex0=ids[0],boneIndex1=ids[1],boneIndex2=ids[2],boneIndex3=ids[3],weight0=ws[0],weight1=ws[1],weight2=ws[2],weight3=ws[3]};
  }
  public void Release(){
   foreach(var strand in strands){if(strand.renderer)Remove(strand.renderer.gameObject);Remove(strand.mesh);Remove(strand.material);}strands.Clear();
   foreach(var layer in layers){
    if(layer.originalRenderer){rig.layers[layer.index]=layer.originalRenderer;layer.originalRenderer.sharedMaterial=layer.originalMaterial;layer.originalRenderer.enabled=layer.originalEnabled;if(layer.renderer)Remove(layer.renderer.gameObject);}
    else if(layer.renderer){if(layer.copy)layer.renderer.sharedMesh=layer.original;layer.renderer.sharedMaterial=layer.originalMaterial;}
    Remove(layer.copy);Remove(layer.material);
   }layers.Clear();PeakMotion=0;MaxStrandAngle=0;
  }
  void OnDisable(){Release();}void OnDestroy(){Release();}
  static void Remove(UnityEngine.Object value){if(!value)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
 }
}
