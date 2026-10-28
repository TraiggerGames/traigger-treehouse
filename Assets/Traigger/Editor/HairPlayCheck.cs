using System;using System.IO;using UnityEditor;using UnityEngine;
namespace Traigger.Editor {
 // Comprueba el render normal sobre fotogramas reales de Play; no usa BakeMesh ni poses congeladas.
 [InitializeOnLoad] public static class HairPlayCheck {
  const string key="TraiggerHairPlayCheck";const string dir="Validation/Hair/Strands17/Play";
  static double began,next;static int frames,errors;static float peak;static StudioController studio;
  static HairPlayCheck(){EditorApplication.playModeStateChanged+=Changed;}
  [MenuItem("Traigger/Pruebas/Comprobar pelo en Play")]
  public static void Run(){if(EditorApplication.isPlaying)throw new Exception("Detén Play antes de esta prueba");SessionState.SetBool(key,true);EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){
   if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(key,false)){
    Directory.CreateDirectory(dir);began=EditorApplication.timeSinceStartup;next=began+1;frames=0;errors=0;peak=0;studio=null;
    Application.logMessageReceived+=Log;EditorApplication.update+=Tick;
   }
   if(state==PlayModeStateChange.ExitingPlayMode){EditorApplication.update-=Tick;Application.logMessageReceived-=Log;SessionState.SetBool(key,false);}
  }
  static void Log(string message,string trace,LogType type){if(message.Contains("Rendering stopped")||message.Contains("expected mesh data size")||message.Contains("Falta el sprite del mechón")||message.Contains("Falta el mapa legible"))errors++;}
  static void Tick(){
   double now=EditorApplication.timeSinceStartup;if(now<next)return;
   if(!studio)studio=UnityEngine.Object.FindAnyObjectByType<StudioController>();
   if(!studio||!studio.hairDetails||studio.hairDetails.SeparatedLockCount!=17){if(now-began>20)Finish("FAIL: no se cargan los 17 mechones en Play");return;}
   float t=(float)(now-began-1);studio.demo=false;studio.tracking=false;studio.head=Mathf.Sin(t*3.4f)*7;studio.turn=Mathf.Sin(t*3)*14;studio.nod=Mathf.Sin(t*2.2f)*3;
   peak=Mathf.Max(peak,studio.hairDetails.MaxStrandAngle);
   var head=Array.Find(studio.rig.bones,b=>b.name=="Head");float scale=Mathf.Max(.1f,Mathf.Abs(studio.rig.transform.lossyScale.y));
   var face=new Bounds((head?head.position:studio.rig.transform.position)+Vector3.up*(.55f*scale),new Vector3(1.7f,2.2f,0)*scale);
   Capture(studio.avatarCamera,face,dir+"/"+frames.ToString("D3")+".png");
   frames++;next=now+.13;
   if(frames==1)File.WriteAllText(dir+"/camera.txt","head="+(head?head.position.ToString():"missing")+"; scale="+scale+"; face="+face);
   if(frames>=22)Finish(errors==0&&peak>1?"PASS normal GPU Play rendering":"FAIL render errors="+errors+"; motion="+peak);
  }
  static void Finish(string result){
   File.WriteAllText(dir+"/results.txt",result+"\n17 independently loaded sprites.\nNormal engine frames: "+frames+"\nPeak spring angle: "+peak+" degrees\nMesh render errors: "+errors+"\n");
   EditorApplication.update-=Tick;Application.logMessageReceived-=Log;SessionState.SetBool(key,false);Debug.Log("TRAIGGER_HAIR_PLAY: "+result);EditorApplication.isPlaying=false;
  }
  static void Capture(Camera camera,Bounds face,string path){
   var position=camera.transform.position;var size=camera.orthographicSize;bool ortho=camera.orthographic;var rect=camera.rect;var target=camera.targetTexture;var active=RenderTexture.active;
   var rt=RenderTexture.GetTemporary(600,750,24);var image=new Texture2D(600,750,TextureFormat.RGBA32,false);
   try{camera.rect=new Rect(0,0,1,1);camera.transform.position=new Vector3(face.center.x,face.center.y,position.z);camera.orthographic=true;camera.orthographicSize=Mathf.Max(face.extents.y,face.extents.x/.8f)*1.05f;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,600,750),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
   finally{camera.transform.position=position;camera.orthographicSize=size;camera.orthographic=ortho;camera.rect=rect;camera.targetTexture=target;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.Destroy(image);}
  }
 }
}
