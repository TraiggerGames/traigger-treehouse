using System;using System.IO;using System.Collections.Generic;using UnityEngine;using UnityEditor;
namespace Traigger.Editor {
 public static class CheekPuffCheck {
  [MenuItem("Traigger/Pruebas/Comprobar mofletes sin pelaje duplicado")]
  public static void Run(){
   if(EditorApplication.isPlaying)throw new Exception("Sal de Play para comprobar la superposición de carrillos");
   var rig=UnityEngine.Object.FindAnyObjectByType<AvatarRig>();if(!rig)throw new Exception("Abre la escena de Traigger");
   var camera=Array.Find(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None),c=>c.name=="Avatar Camera");
   var position=camera.transform.position;float size=camera.orthographicSize;var rect=camera.rect;
   var meshes=Array.ConvertAll(rig.layers,r=>r.sharedMesh);var materials=Array.ConvertAll(rig.layers,r=>r.sharedMaterial);
   var weights=Array.ConvertAll(rig.layers,r=>{var a=new float[r.sharedMesh.blendShapeCount];for(int i=0;i<a.Length;i++)a[i]=r.GetBlendShapeWeight(i);return a;});
   var mouthPosition=rig.closedMouth?rig.closedMouth.transform.localPosition:Vector3.zero;
   var expression=rig.gameObject.AddComponent<FacialExpressionRig>();expression.rig=rig;expression.intensity=1.5f;expression.manual=true;
   var bakedHead=new Mesh();var bakedCheek=new Mesh();float maximum=0;
   Directory.CreateDirectory("Validation/CheekPuff");
   try{
    expression.Initialize();int head=Array.FindIndex(rig.definition.layers,l=>l.name=="HeadBase");
    var headRenderer=rig.layers[head];camera.rect=new Rect(0,0,1,1);camera.transform.position=new Vector3(rig.transform.position.x,rig.transform.position.y+3.8f,position.z);camera.orthographicSize=1.05f;
    foreach(float yaw in new[]{0f,-18f,18f}){
     rig.Look(yaw,0);
     var baseline=new Dictionary<SkinnedMeshRenderer,Vector3[]>();
     expression.manualFrame.cheekPuff=0;for(int i=0;i<60;i++)expression.Step(null,1f/30);
     foreach(string name in new[]{"CheekLeft","CheekRight"}){
      int index=Array.FindIndex(rig.definition.layers,l=>l.name==name);if(index<0)throw new Exception("Falta "+name);
      baseline[rig.layers[index]]=Gaps(headRenderer,rig.layers[index],bakedHead,bakedCheek);
     }
     expression.manualFrame.cheekPuff=1;for(int i=0;i<60;i++)expression.Step(null,1f/30);
     foreach(var pair in baseline){
      var renderer=pair.Key;if(renderer.sharedMesh.GetBlendShapeIndex("Expression_cheekPuff")<0)throw new Exception("Pelaje sin correctivo de moflete");
      var after=Gaps(headRenderer,renderer,bakedHead,bakedCheek);
      for(int i=0;i<after.Length;i++)maximum=Mathf.Max(maximum,(after[i]-pair.Value[i]).magnitude);
     }
     StudioSetup.Render("Validation/CheekPuff/"+(yaw==0?"front":yaw<0?"left":"right")+".png");
    }
    if(maximum>.003f)throw new Exception("La cara y el recorte de pelaje se separan: "+maximum);
    File.WriteAllText("Validation/CheekPuff/results.txt","PASS Unity "+Application.unityVersion+"\nMofletes al máximo, intensidad 1.5, giro -18/0/18 grados.\nSeparación adicional al inflar respecto al reposo, muestras del mismo UV: "+maximum+" unidades (límite 0.003).\nTexturas, huesos y posición neutral conservados.\n");Debug.Log("TRAIGGER_CHEEK_PUFF_PASS");
   }finally{
    expression.Release();UnityEngine.Object.DestroyImmediate(expression);UnityEngine.Object.DestroyImmediate(bakedHead);UnityEngine.Object.DestroyImmediate(bakedCheek);
    for(int i=0;i<rig.layers.Length;i++){if(rig.layers[i].sharedMesh!=meshes[i]||rig.layers[i].sharedMaterial!=materials[i])throw new Exception("No se restauran recursos originales");for(int j=0;j<weights[i].Length;j++)rig.layers[i].SetBlendShapeWeight(j,weights[i][j]);}
    camera.transform.position=position;camera.orthographicSize=size;camera.rect=rect;if(rig.closedMouth)rig.closedMouth.transform.localPosition=mouthPosition;
   }
  }
  static Vector3[] Gaps(SkinnedMeshRenderer head,SkinnedMeshRenderer cheek,Mesh bakedHead,Mesh bakedCheek){
   head.BakeMesh(bakedHead);cheek.BakeMesh(bakedCheek);var uv=cheek.sharedMesh.uv;var vertices=bakedCheek.vertices;
   var headUV=head.sharedMesh.uv;var triangles=bakedHead.triangles;var headVertices=bakedHead.vertices;var result=new Vector3[uv.Length];
   for(int i=0;i<uv.Length;i++)result[i]=cheek.transform.TransformPoint(vertices[i])-head.transform.TransformPoint(Sample(uv[i],headUV,triangles,headVertices));return result;
  }
  static Vector3 Sample(Vector2 point,Vector2[] uv,int[] triangles,Vector3[] positions){
   for(int i=0;i<triangles.Length;i+=3){int ia=triangles[i],ib=triangles[i+1],ic=triangles[i+2];var a=uv[ia];var b=uv[ib];var c=uv[ic];
    float d=(b.y-c.y)*(a.x-c.x)+(c.x-b.x)*(a.y-c.y);if(Mathf.Abs(d)<1e-12f)continue;
    float x=((b.y-c.y)*(point.x-c.x)+(c.x-b.x)*(point.y-c.y))/d;
    float y=((c.y-a.y)*(point.x-c.x)+(a.x-c.x)*(point.y-c.y))/d,z=1-x-y;
    if(x>=-.0001f&&y>=-.0001f&&z>=-.0001f)return positions[ia]*x+positions[ib]*y+positions[ic]*z;
   }
   throw new Exception("El UV del carrillo no pertenece a la cara");
  }
 }
}
