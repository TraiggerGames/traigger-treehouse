using System;using System.IO;using UnityEditor;using UnityEngine;
namespace Traigger.Editor {
 // Movimiento sobre fotogramas reales: controlador, huesos auxiliares, física y render GPU.
 [InitializeOnLoad] public static class PumpkinSleevePlayCheck {
  const string key="TraiggerPumpkinSleevePlay",dir="Validation/PumpkinSleeves/Play";
  static double began,next;static int frames,errors;static float springPeak;static StudioController studio;static KemonoBodyRig body;
  static PumpkinSleevePlayCheck(){EditorApplication.playModeStateChanged+=Changed;EditorApplication.update+=Request;}
  static void Request(){if(File.Exists("Validation/PumpkinSleeves.playrequest")&&!EditorApplication.isPlaying&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating){File.Delete("Validation/PumpkinSleeves.playrequest");Run();}}
  [MenuItem("Traigger/Pruebas/Comprobar outfit Calabaza en Play")]
  public static void Run(){if(EditorApplication.isPlaying)throw new Exception("Detén Play antes de la prueba");SessionState.SetBool(key,true);EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){
   if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(key,false)){Directory.CreateDirectory(dir);began=EditorApplication.timeSinceStartup;next=began+1.5;frames=0;errors=0;springPeak=0;studio=null;body=null;Application.logMessageReceived+=Log;EditorApplication.update+=Tick;}
   if(state==PlayModeStateChange.ExitingPlayMode){EditorApplication.update-=Tick;Application.logMessageReceived-=Log;SessionState.SetBool(key,false);}
  }
  static void Log(string message,string trace,LogType type){if((type==LogType.Error||type==LogType.Exception)&& (message.Contains("Outfit")||message.Contains("Rendering stopped")||message.Contains("expected mesh data size")||message.Contains("OutfitSleeve")||message.Contains("manga")))errors++;}
  static void Tick(){
   double now=EditorApplication.timeSinceStartup;if(now<next)return;
   try{
    if(!studio){studio=UnityEngine.Object.FindAnyObjectByType<StudioController>();if(!studio||!studio.rig){if(now-began>20)Finish("FAIL: no se inicia estudio");return;}
     var old=studio.rig.GetComponent<OutfitFollower>();if(old)UnityEngine.Object.DestroyImmediate(old);
     if(!Outfits.Apply(studio.rig,"Traigger","Calabaza"))throw new Exception("Falta outfit");body=studio.rig.GetComponent<KemonoBodyRig>();if(!body)throw new Exception("Falta rig del cuerpo");
    }
    float t=(float)(now-began-1.5);float raise=55-55*Mathf.Cos(t*1.25f);
    studio.demo=false;studio.tracking=false;studio.naturalSpeech=false;studio.upperArmLeft=-raise;studio.upperArmRight=raise;
    studio.armLeft=-40*(1+Mathf.Sin(t*1.4f));studio.armRight=40*(1+Mathf.Sin(t*1.4f));studio.torso=Mathf.Sin(t*1.1f)*6;studio.turn=Mathf.Sin(t)*10;
    foreach(var bone in body.springBones)if(bone&&bone.name.Contains("Sleeve"))springPeak=Mathf.Max(springPeak,Mathf.Abs(Mathf.DeltaAngle(0,bone.localEulerAngles.z)));
    Capture(studio.avatarCamera,studio.rig,dir+"/"+frames.ToString("D3")+".png");frames++;next=now+.10;
    if(frames>=50)Finish(errors==0&&springPeak>.05f?"PASS: render GPU y física de mangas":"FAIL: errores="+errors+"; muelle="+springPeak);
   }catch(Exception e){Finish("FAIL: "+e);}
  }
  static void Finish(string result){File.WriteAllText(dir+"/results.txt",result+"\nFotogramas reales: "+frames+"\nPico de inercia de manga: "+springPeak.ToString("F3")+"°\nErrores de render del outfit: "+errors+"\nNo usa Face ID ni hardware iOS: prueba de controlador y render en Unity.\n");EditorApplication.update-=Tick;Application.logMessageReceived-=Log;SessionState.SetBool(key,false);Debug.Log("TRAIGGER_PUMPKIN_PLAY: "+result);EditorApplication.isPlaying=false;}
  static void Capture(Camera camera,AvatarRig rig,string path){
   var pos=camera.transform.position;var size=camera.orthographicSize;var rect=camera.rect;var target=camera.targetTexture;var active=RenderTexture.active;
   var rt=RenderTexture.GetTemporary(900,765,24);var image=new Texture2D(900,765,TextureFormat.RGBA32,false);
   try{var center=rig.transform.TransformPoint(new Vector3(0,2.8f,0));camera.rect=new Rect(0,0,1,1);camera.transform.position=new Vector3(center.x,center.y,pos.z);camera.orthographicSize=1.55f*Mathf.Abs(rig.transform.lossyScale.y);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
   finally{camera.transform.position=pos;camera.orthographicSize=size;camera.rect=rect;camera.targetTexture=target;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.Destroy(image);}
  }
 }
}
