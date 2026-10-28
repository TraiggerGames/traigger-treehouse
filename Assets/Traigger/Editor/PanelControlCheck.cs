using System;using System.Collections.Generic;using System.IO;using System.Threading.Tasks;using UnityEngine;using UnityEditor;
namespace Traigger.Editor {
 public static class PanelControlCheck {
  [MenuItem("Traigger/Pruebas/Comprobar encuadre y bloqueo")]
  public static async void Check(){
   if(!EditorApplication.isPlaying){Debug.LogError("Pulsa Play antes de comprobar encuadres y gestos");return;}
   var studio=UnityEngine.Object.FindAnyObjectByType<StudioController>();if(!studio||!studio.manipulator)return;
   var m=studio.manipulator;var camera=studio.avatarCamera;var pos=camera.transform.position;float size=camera.orthographicSize;Rect viewport=camera.rect;
   Vector2 offset=m.offset;float scale=m.scale;bool locked=m.gesturesLocked,remember=m.remember,frameEnabled=studio.framing.enabled;
   var go=new GameObject("Framing test");var framing=go.AddComponent<AvatarFraming>();framing.studio=studio;
   var report=new System.Text.StringBuilder();
   try{
    m.remember=false;studio.framing.enabled=false;m.SetGesturesLocked(false);
    Transform head=Array.Find(studio.rig.bones,b=>b.name=="Head");Vector2 point=camera.WorldToScreenPoint(head.position);
    m.SimulateTouches(new Dictionary<int,Vector2>{{1,point}});m.SimulateTouches(new Dictionary<int,Vector2>{{1,point+Vector2.right*35}});
    if(m.offset==offset)throw new Exception("El gesto de arrastre no inicia");
    var frozen=m.offset;float frozenScale=m.scale;m.SetGesturesLocked(true);
    m.SimulateTouches(new Dictionary<int,Vector2>{{1,point-Vector2.right*150},{2,point+Vector2.right*150}});
    if(m.offset!=frozen||m.scale!=frozenScale||m.dragging)throw new Exception("El bloqueo no cancela el gesto en curso");
    m.SetGesturesLocked(false);m.SimulateTouches(new Dictionary<int,Vector2>());
    m.SimulateTouches(new Dictionary<int,Vector2>{{1,point},{2,point+Vector2.right*60}});
    m.SimulateTouches(new Dictionary<int,Vector2>{{1,point},{2,point+Vector2.right*100}});
    if(m.scale==frozenScale)throw new Exception("No se puede volver a escalar al desbloquear");m.SimulateTouches(new Dictionary<int,Vector2>());
    report.AppendLine("Gestos: arrastre, bloqueo durante gesto, pellizco bloqueado y desbloqueo comprobados.");
    Directory.CreateDirectory("Validation/Panel");
    foreach(bool face in new[]{false,true}){
     framing.Center(face);int frame=Time.frameCount;double deadline=EditorApplication.timeSinceStartup+10;
     while(EditorApplication.isPlaying&&Time.frameCount<frame+3&&EditorApplication.timeSinceStartup<deadline)await Task.Delay(15);
     if(!framing.Active||!EditorApplication.isPlaying)throw new Exception("Encuadre interrumpido");
     var bounds=framing.framedBounds;
     foreach(float x in new[]{bounds.min.x,bounds.max.x})foreach(float y in new[]{bounds.min.y,bounds.max.y}){var v=camera.WorldToViewportPoint(new Vector3(x,y,bounds.center.z));if(v.x<0||v.x>1||v.y<0||v.y>1)throw new Exception("El encuadre corta la geometría");}
     if(!face)foreach(var bone in studio.rig.GetComponentsInChildren<Transform>())if(bone.name=="RightFoot"||bone.name=="LeftFoot"||bone.name=="RightHand"||bone.name=="LeftHand"){
      var v=camera.WorldToViewportPoint(bone.position);if(v.x<0||v.x>1||v.y<0||v.y>1)throw new Exception("Queda fuera un pie o una mano");
     }
     Capture(framing,camera,"Validation/Panel/"+(face?"face":"body")+".png");report.AppendLine((face?"Cara / llamada":"Cuerpo entero")+": geometría dentro de cámara, salida 16:9 capturada.");
    }
    File.WriteAllText("Validation/Panel/results.txt","PASS Unity "+Application.unityVersion+"\n"+report+"Pendiente prueba física de gestos en Vision Pro.\n");Debug.Log("TRAIGGER_PANEL_CONTROLS_PASS");
   }catch(Exception e){Debug.LogException(e);}finally{
    if(go)UnityEngine.Object.DestroyImmediate(go);
    if(studio&&studio.framing)studio.framing.enabled=frameEnabled;
    if(m){m.SetGesturesLocked(locked);m.offset=offset;m.scale=scale;m.remember=remember;}
    if(camera){camera.transform.position=pos;camera.orthographicSize=size;camera.rect=viewport;}
   }
  }
  static void Capture(AvatarFraming framing,Camera source,string path){
   var go=new GameObject("Framing capture"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;camera.rect=new Rect(0,0,1,1);
   var rt=new RenderTexture(1280,720,24);var previous=RenderTexture.active;Texture2D image=null;
   try{camera.targetTexture=rt;framing.Apply(camera,16f/9);camera.Render();RenderTexture.active=rt;image=new Texture2D(1280,720,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
   finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(go);rt.Release();UnityEngine.Object.DestroyImmediate(rt);if(image)UnityEngine.Object.DestroyImmediate(image);}
  }
 }
}
