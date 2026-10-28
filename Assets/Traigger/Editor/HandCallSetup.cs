using System;using System.IO;using System.Collections.Generic;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;
namespace Traigger.Editor {
 public static class HandCallSetup {
  [Serializable] class Anchor {public float[] root,tip;}
  [Serializable] class Regions {public int[] left,right;public Anchor[] leftAnchors,rightAnchors;}
  static Vector3 Point(Mesh m,Vector2 uv){var v=m.vertices;var t=m.uv;float x=(uv.x-t[0].x)/(t[10].x-t[0].x)*10,y=(uv.y-t[0].y)/(t[396].y-t[0].y)*36;int a=Mathf.Clamp(Mathf.FloorToInt(x),0,9),b=Mathf.Clamp(Mathf.FloorToInt(y),0,35),i=b*11+a;return Vector3.Lerp(Vector3.Lerp(v[i],v[i+1],x-a),Vector3.Lerp(v[i+11],v[i+12],x-a),y-b);}
  static BoneWeight Pack(Dictionary<int,float> values){var list=new List<KeyValuePair<int,float>>(values);list.Sort((a,b)=>b.Value.CompareTo(a.Value));float sum=0;for(int i=0;i<Mathf.Min(4,list.Count);i++)sum+=list[i].Value;var w=new BoneWeight();for(int i=0;i<Mathf.Min(4,list.Count);i++){float f=list[i].Value/sum;int id=list[i].Key;if(i==0){w.boneIndex0=id;w.weight0=f;}if(i==1){w.boneIndex1=id;w.weight1=f;}if(i==2){w.boneIndex2=id;w.weight2=f;}if(i==3){w.boneIndex3=id;w.weight3=f;}}return w;}
  [MenuItem("Traigger/Llamadas/Preparar manos y salida")]
  public static void Prepare(){
   if(EditorApplication.isPlaying)throw new Exception("Sal de Play para preparar las manos");
   var body=UnityEngine.Object.FindAnyObjectByType<KemonoBodyRig>();if(!body)throw new Exception("Abre la escena kemono");var rig=body.rig;
   Directory.CreateDirectory("Validation/Hands");if(!File.Exists("Validation/Hands/scene-before-fingers.unity.bak"))File.Copy(rig.gameObject.scene.path,"Validation/Hands/scene-before-fingers.unity.bak");
   Undo.RegisterFullObjectHierarchyUndo(rig.gameObject,"Preparar manos articuladas");
   var asset=ScriptableObject.CreateInstance<RigDefinition>();AssetDatabase.CreateAsset(asset,AssetDatabase.GenerateUniqueAssetPath("Assets/Traigger/Art/BodyV12/FingerMeshes.asset"));
   for(int side=0;side<2;side++){
    string label=side==0?"Left":"Right";var hand=Array.Find(body.parts,r=>r.name=="Kemono_Hand"+label);var previousDriver=hand.GetComponent<FingerPoseRig>();
    if(previousDriver){
     string snapshot="Assets/Traigger/Scenes/HandSourceBeforeFingers.unity";
     if(!File.Exists(snapshot)){File.Copy("Validation/Hands/scene-before-fingers.unity.bak",snapshot);AssetDatabase.ImportAsset(snapshot);}
     var preview=EditorSceneManager.OpenPreviewScene(snapshot);
     try{
      SkinnedMeshRenderer original=null;
      foreach(var root in preview.GetRootGameObjects())foreach(var candidate in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(candidate.name==hand.name)original=candidate;
      if(!original||original.sharedMesh.vertexCount!=407)throw new Exception("No se encuentra la mano original del rig actual");
      hand.sharedMesh=original.sharedMesh;
      hand.bones=Array.ConvertAll(original.bones,b=>Array.Find(rig.bones,r=>r.name==b.name));
      if(Array.Exists(hand.bones,b=>!b))throw new Exception("Falta un hueso original de la mano");
     }finally{EditorSceneManager.ClosePreviewScene(preview);}

     for(int f=0;f<4;f++)if(previousDriver.joints[f*3])Undo.DestroyObjectImmediate(previousDriver.joints[f*3].gameObject);
     Undo.DestroyObjectImmediate(previousDriver);
    }
    var source=hand.sharedMesh;if(source.vertexCount!=407)throw new Exception("La mano original ya no tiene la cuadrícula esperada");
    var driver=hand.gameObject.AddComponent<FingerPoseRig>();driver.leftHuman=side==0;driver.sourceMesh=source;driver.sourceBones=hand.bones;
    var parent=Array.Find(rig.bones,b=>b.name==(side==0?"RightHand":"LeftHand"));
    var bones=new List<Transform>(hand.bones);var poses=new List<Matrix4x4>(source.bindposes);var digits=new List<Transform>();var rests=new List<Quaternion>();int first=bones.Count;
    var regions=JsonUtility.FromJson<Regions>(File.ReadAllText("Assets/Traigger/Art/BodyV12/finger-regions.json"));
    var anchors=side==0?regions.leftAnchors:regions.rightAnchors;var groups=side==0?regions.left:regions.right;
    var roots=Array.ConvertAll(anchors,a=>new Vector2(side==0?a.root[0]:1254-a.root[0],a.root[1]));
    var tips=Array.ConvertAll(anchors,a=>new Vector2(side==0?a.tip[0]:1254-a.tip[0],a.tip[1]));
    Vector2 Mirror(Vector2 p)=>side==0?p:new Vector2(1254-p.x,p.y);
    Vector2 UV(Vector2 p)=>new Vector2(p.x/1254,1-p.y/1254);
    for(int finger=0;finger<4;finger++){Transform previous=parent;for(int segment=0;segment<3;segment++){
      var joint=new GameObject("Finger_"+label+"_"+finger+"_"+segment).transform;joint.SetParent(previous,false);
      var at=Mirror(Vector2.Lerp(roots[finger],tips[finger],segment/3f));joint.position=hand.transform.TransformPoint(Point(source,UV(at)));joint.rotation=hand.transform.rotation;
      bones.Add(joint);poses.Add(joint.worldToLocalMatrix*hand.transform.localToWorldMatrix);digits.Add(joint);rests.Add(joint.localRotation);previous=joint;
    }}
    const int nx=60,ny=72;int stride=(nx+1)*(ny+1);var vertices=new Vector3[stride*5];var uv=new Vector2[vertices.Length];var colors=new Color[vertices.Length];var weights=new BoneWeight[vertices.Length];var triangles=new List<int>();var oldUV=source.uv;var oldWeights=source.boneWeights;var oldColors=source.colors;
    for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++){
      int id=y*(nx+1)+x;float u=x/(float)nx,t=y/(float)ny;uv[id]=oldUV[0]+u*(oldUV[10]-oldUV[0])+t*(oldUV[396]-oldUV[0]);vertices[id]=Point(source,uv[id]);
      float gx=u*10,gy=t*36;int ix=Mathf.Min(9,(int)gx),iy=Mathf.Min(35,(int)gy),a=iy*11+ix;float fx=gx-ix,fy=gy-iy;
      var values=new Dictionary<int,float>();void Add(int b,float w){if(w<=0)return;values.TryGetValue(b,out float old);values[b]=old+w;}
      Vector2 pixel=Mirror(new Vector2(uv[id].x*1254,(1-uv[id].y)*1254));int best=0;float distance=float.MaxValue,along=0;
      for(int f=0;f<4;f++){var axis=tips[f]-roots[f];float q=Mathf.Clamp01(Vector2.Dot(pixel-roots[f],axis)/axis.sqrMagnitude);float d=Vector2.Distance(pixel,roots[f]+axis*q);if(d<distance){distance=d;best=f;along=q;}}
      int[] cells={a,a+1,a+11,a+12};float[] blend={(1-fx)*(1-fy),fx*(1-fy),(1-fx)*fy,fx*fy};Color color=Color.clear;
      for(int q=0;q<4;q++)color+=(oldColors.Length>0?oldColors[cells[q]]:Color.white)*blend[q];
      for(int group=0;group<5;group++){
       values.Clear();int finger=Mathf.Max(0,group-1);var axis=tips[finger]-roots[finger];float projection=Vector2.Dot(pixel-roots[finger],axis.normalized);
       float amount=group==0?0:Mathf.SmoothStep(0,1,Mathf.InverseLerp(-5,30,projection));
       for(int q=0;q<4;q++){var w=oldWeights[cells[q]];float f=blend[q]*(1-amount);Add(w.boneIndex0,w.weight0*f);Add(w.boneIndex1,w.weight1*f);Add(w.boneIndex2,w.weight2*f);Add(w.boneIndex3,w.weight3*f);}
       float chain=Mathf.Clamp(projection/axis.magnitude*3,0,2);int segment=Mathf.Min(1,(int)chain);Add(first+finger*3+segment,amount*(1-(chain-segment)));Add(first+finger*3+segment+1,amount*(chain-segment));
       int vertex=id+group*stride;vertices[vertex]=vertices[id];uv[vertex]=uv[id];colors[vertex]=color;weights[vertex]=Pack(values);
      }
    }
    for(int y=0;y<ny;y++)for(int x=0;x<nx;x++){
      int a=y*(nx+1)+x,b=a+1,c=a+nx+1,d=c+1;
      void Triangle(int i,int j,int k){var tex=(uv[i]+uv[j]+uv[k])/3;var pixel=Mirror(new Vector2(tex.x*1254,(1-tex.y)*1254));int best=0;float distance=float.MaxValue,projection=0;
       for(int f=0;f<4;f++){var axis=tips[f]-roots[f];float q=Mathf.Clamp01(Vector2.Dot(pixel-roots[f],axis)/axis.sqrMagnitude);float d=Vector2.Distance(pixel,roots[f]+axis*q);if(d<distance){distance=d;best=f;projection=Vector2.Dot(pixel-roots[f],axis.normalized);}}
       int group=groups[y*nx+x];int offset=group*stride;triangles.AddRange(new[]{i+offset,j+offset,k+offset});
      }
      Triangle(a,c,b);Triangle(b,c,d);
    }
    var mesh=new Mesh{name="Hand"+label+"_fingers"};mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.boneWeights=weights;mesh.bindposes=poses.ToArray();mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
    foreach(var w in weights)if(w.boneIndex0>=bones.Count||w.boneIndex1>=bones.Count||w.boneIndex2>=bones.Count||w.boneIndex3>=bones.Count)throw new Exception("Índice de dedo fuera del rig");
    if(poses.Count!=bones.Count)throw new Exception("Bindposes y huesos no coinciden");
    hand.sharedMesh=null;hand.bones=bones.ToArray();hand.sharedMesh=mesh;hand.updateWhenOffscreen=true;driver.joints=digits.ToArray();driver.rest=rests.ToArray();AssetDatabase.AddObjectToAsset(mesh,asset);EditorUtility.SetDirty(hand);EditorUtility.SetDirty(driver);
   }
   var input=UnityEngine.Object.FindAnyObjectByType<VisionMotionInput>();if(!input)input=new GameObject("TraiggerVisionInput").AddComponent<VisionMotionInput>();
   var controller=UnityEngine.Object.FindAnyObjectByType<StudioController>();if(controller){controller.vision=input;EditorUtility.SetDirty(controller);}
   if(!rig.GetComponent<CallPresentation>())rig.gameObject.AddComponent<CallPresentation>();
   // Opt-in explícito desde el panel de llamadas; no abre puertos al preparar la escena.
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);EditorSceneManager.SaveScene(rig.gameObject.scene);
   HandCallCheck.Check();
   File.WriteAllText("Validation/Hands/setup.txt","Manos: 4 dedos kemono por mano, 3 huesos por dedo y cuadrícula 60x72. Cabeza, cuerpo y muñecas conservados. Arte de dedos al cerrar: prototipo que requiere retoque de superficies ocultas.\n");
  }
 }
}
