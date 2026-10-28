using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace Traigger.Editor {
 public static class HairRepaintCheck {
  const string Folder="Validation/HairRepaint";
  [InitializeOnLoadMethod] static void Schedule(){EditorApplication.delayCall+=Pending;}
  static void Pending(){
   if(!File.Exists(Folder+".request"))return;
   if(EditorApplication.isCompiling||EditorApplication.isUpdating){EditorApplication.delayCall+=Pending;return;}
   if(EditorApplication.isPlaying)return;
   Directory.CreateDirectory(Folder);
   try{Run();}catch(Exception e){File.WriteAllText(Folder+"/error.txt",e.ToString());Debug.LogException(e);}
   finally{File.Delete(Folder+".request");}
  }
  [MenuItem("Traigger/Arte/Comprobar sprite repintado y flequillo")]
  public static void Run(){
   if(EditorApplication.isPlaying)throw new Exception("Sal de Play para comprobar sin alterar el rig");
   var rig=UnityEngine.Object.FindAnyObjectByType<AvatarRig>();if(!rig)throw new Exception("Abre la escena de Traigger");
   Directory.CreateDirectory(Folder);
   var meshes=rig.layers.Select(l=>l.sharedMesh).ToArray();var materials=rig.layers.Select(l=>l.sharedMaterial).ToArray();
   var positions=rig.bones.Select(b=>b.localPosition).ToArray();var rotations=rig.bones.Select(b=>b.localRotation).ToArray();
   var camera=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c=>c.name=="Avatar Camera");
   var cp=camera.transform.position;float size=camera.orthographicSize;var viewport=camera.rect;
   var detail=rig.gameObject.AddComponent<HairDetailRig>();detail.rig=rig;
   try{
    detail.Initialize();
    int front=Array.FindIndex(rig.definition.layers,l=>l.name=="FrontHair"),back=Array.FindIndex(rig.definition.layers,l=>l.name=="BackHair");
    if(front<0||back<0)throw new Exception("Faltan capas de pelo");
    if(rig.layers[front].sharedMaterial.mainTexture.name!="FrontHairHD")throw new Exception("No se usa el sprite frontal nuevo");
    if(!rig.layers[back].sharedMaterial.mainTexture.name.EndsWith("HD"))throw new Exception("No se usa el sprite trasero nuevo");
    if(rig.layers[front].sharedMaterial.mainTexture.width!=2508)throw new Exception("La textura frontal se ha reducido al importar");
    var copy=rig.layers[front].sharedMesh;var original=meshes[front];
    var uv=original.uv;var vertices=original.vertices;var copyUV=copy.uv;var copyVertices=copy.vertices;
    for(int i=0;i<original.vertexCount;i++)if(uv[i]!=copyUV[i]||vertices[i]!=copyVertices[i])throw new Exception("Se altera el rig original");
    for(int i=0;i<original.blendShapeCount;i++)if(original.GetBlendShapeName(i)!=copy.GetBlendShapeName(i))throw new Exception("Se pierde un correctivo existente");
    int central=copy.GetBlendShapeIndex("Fringe1Positive");if(central<0)throw new Exception("Falta flequillo central");
    var delta=new Vector3[copy.vertexCount];copy.GetBlendShapeFrameVertices(central,0,delta,new Vector3[delta.Length],new Vector3[delta.Length]);
    float peak=0;for(int i=0;i<delta.Length;i++){
     peak=Mathf.Max(peak,delta[i].magnitude);
     float y=(1-copyUV[i].y)*1254;
     if(y<110&&delta[i].sqrMagnitude>1e-10f)throw new Exception("Se mueve la raíz del flequillo");
     if(float.IsNaN(delta[i].x)||float.IsInfinity(delta[i].x))throw new Exception("Deformación inválida");
    }
    if(peak<.001f||peak>.09f)throw new Exception("Desplazamiento central fuera de rango: "+peak);
    camera.rect=new Rect(0,0,1,1);camera.transform.position=new Vector3(rig.transform.position.x,rig.transform.position.y+3.8f,cp.z);camera.orthographicSize=1.05f;
    detail.Step(0,0,0,0,1,1f/60);StudioSetup.Render(Folder+"/neutral.png");
    for(int i=0;i<40;i++)detail.Step(-65,-25,12,0,1,1f/60);StudioSetup.Render(Folder+"/right.png");
    for(int i=0;i<40;i++)detail.Step(65,25,-12,0,1,1f/60);StudioSetup.Render(Folder+"/left.png");
    for(int i=0;i<500;i++)detail.Step(0,0,0,0,1,1f/60);
    if(rig.layers[front].GetBlendShapeWeight(central)>.01f||rig.layers[front].GetBlendShapeWeight(copy.GetBlendShapeIndex("Fringe1Negative"))>.01f)throw new Exception("El flequillo no vuelve a reposo");
    for(int i=0;i<rig.bones.Length;i++)if(positions[i]!=rig.bones[i].localPosition||rotations[i]!=rig.bones[i].localRotation)throw new Exception("Se altera un hueso");
    File.WriteAllText(Folder+"/results.txt","PASS Unity "+Application.unityVersion+"\nSprites HD cargados en las capas reales; resolución frontal 2508.\nUV, vértices originales, correctivos y huesos conservados.\nRaíz central fija, desplazamiento máximo "+peak+", muelle vuelve a reposo.\nPendiente evaluación del movimiento en dispositivo físico.\n");
   }finally{
    detail.Release();UnityEngine.Object.DestroyImmediate(detail);
    camera.transform.position=cp;camera.orthographicSize=size;camera.rect=viewport;
    for(int i=0;i<rig.layers.Length;i++)if(meshes[i]!=rig.layers[i].sharedMesh||materials[i]!=rig.layers[i].sharedMaterial)Debug.LogError("No se restauró una capa tras validar el pelo");
   }
  }
 }
}
