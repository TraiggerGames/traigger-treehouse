using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace Traigger.Editor {
 public static class CoatRepaintCheck {
  const string Folder="Validation/CoatRepaint";
  [InitializeOnLoadMethod] static void Schedule(){EditorApplication.delayCall+=Pending;}
  static void Pending(){
   if(!File.Exists(Folder+".request"))return;
   if(EditorApplication.isCompiling||EditorApplication.isUpdating){EditorApplication.delayCall+=Pending;return;}
   if(EditorApplication.isPlaying)return;
   Directory.CreateDirectory(Folder);
   try{Run();}catch(Exception e){File.WriteAllText(Folder+"/error.txt",e.ToString());Debug.LogException(e);}
   finally{File.Delete(Folder+".request");}
  }
  [MenuItem("Traigger/Arte/Comprobar cabeza y cola repintadas")]
  public static void Run(){
   if(EditorApplication.isPlaying)throw new Exception("Sal de Play antes de comprobar el acabado");
   var rig=UnityEngine.Object.FindAnyObjectByType<AvatarRig>();if(!rig)throw new Exception("Abre Traigger");
   Directory.CreateDirectory(Folder);
   var meshes=rig.layers.Select(l=>l.sharedMesh).ToArray();var materials=rig.layers.Select(l=>l.sharedMaterial).ToArray();
   var weights=rig.layers.Select(l=>Enumerable.Range(0,l.sharedMesh.blendShapeCount).Select(l.GetBlendShapeWeight).ToArray()).ToArray();
   var positions=rig.bones.Select(b=>b.localPosition).ToArray();var rotations=rig.bones.Select(b=>b.localRotation).ToArray();
   var camera=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c=>c.name=="Avatar Camera");
   var cp=camera.transform.position;float size=camera.orthographicSize;var viewport=camera.rect;
   var hair=rig.gameObject.AddComponent<HairDetailRig>();hair.rig=rig;
   var expressions=rig.gameObject.AddComponent<FacialExpressionRig>();expressions.rig=rig;expressions.manual=true;
   try{
    var texture=Resources.Load<Texture2D>("Coat/CoatAtlasHD");if(!texture||texture.width!=2508)throw new Exception("Falta atlas HD o se ha reducido al importar");
    foreach(string name in new[]{"HeadBase","CheekLeft","CheekRight","EarLeft","EarRight","Tail"}){
     int index=Array.FindIndex(rig.definition.layers,l=>l.name==name);if(index<0)throw new Exception("Falta capa "+name);
     if(rig.layers[index].sharedMaterial.mainTexture!=texture)throw new Exception("La capa no usa el repintado: "+name);
     if(rig.layers[index].sharedMaterial.GetFloat("_HeadShadeStrength")>.001f)throw new Exception("Sombreado antiguo duplicado en "+name);
    }
    hair.Initialize();hair.Step(0,0,0,0,1,1f/60);
    camera.rect=new Rect(0,0,1,1);camera.transform.position=new Vector3(rig.transform.position.x,rig.transform.position.y+2.45f,cp.z);camera.orthographicSize=2.8f;
    StudioSetup.Render(Folder+"/full.png");
    camera.transform.position=new Vector3(rig.transform.position.x,rig.transform.position.y+3.8f,cp.z);camera.orthographicSize=1.05f;
    StudioSetup.Render(Folder+"/head.png");
    expressions.Initialize();expressions.manualFrame.cheekPuff=1;for(int i=0;i<60;i++)expressions.Step(null,1f/30);
    int head=Array.FindIndex(rig.definition.layers,l=>l.name=="HeadBase");
    if(rig.layers[head].sharedMesh.GetBlendShapeIndex("Expression_cheekPuff")<0)throw new Exception("Se pierde el correctivo del moflete");
    StudioSetup.Render(Folder+"/cheek-puff.png");
    expressions.manualFrame.cheekPuff=0;for(int i=0;i<60;i++)expressions.Step(null,1f/30);
    int tail=Array.FindIndex(rig.definition.layers,l=>l.name=="Tail");var bounds=rig.layers[tail].bounds;
    camera.transform.position=new Vector3(bounds.center.x,bounds.center.y,cp.z);camera.orthographicSize=Mathf.Max(bounds.extents.y,bounds.extents.x*1.25f)*1.15f;
    StudioSetup.Render(Folder+"/tail.png");
    File.WriteAllText(Folder+"/results.txt","PASS Unity "+Application.unityVersion+"\nAtlas HD cargado en cabeza, dos carrillos, dos orejas y cola; sombreado antiguo desactivado.\nPelo HD conservado. Correctivo cheekPuff disponible; capturas neutra, mofletes y cola.\nSin reconstrucción de rig; originales se restauran tras la prueba.\nRevisión en dispositivos físicos pendiente.\n");
   }finally{
    expressions.Release();UnityEngine.Object.DestroyImmediate(expressions);hair.Release();UnityEngine.Object.DestroyImmediate(hair);
    for(int i=0;i<rig.bones.Length;i++){rig.bones[i].localPosition=positions[i];rig.bones[i].localRotation=rotations[i];}
    for(int i=0;i<rig.layers.Length;i++){
     if(meshes[i]!=rig.layers[i].sharedMesh||materials[i]!=rig.layers[i].sharedMaterial)throw new Exception("Se altera una malla o material durante la prueba");
     for(int j=0;j<weights[i].Length;j++)rig.layers[i].SetBlendShapeWeight(j,weights[i][j]);
    }
    camera.transform.position=cp;camera.orthographicSize=size;camera.rect=viewport;
   }
  }
 }
}
