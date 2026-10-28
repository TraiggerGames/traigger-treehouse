using System;using System.IO;using UnityEngine;using UnityEditor;
namespace Traigger.Editor {
 public static class LayeredEyeCheck {
  [MenuItem("Traigger/Pruebas/Comprobar iris y pupilas separados")]
  public static void Run(){
   if(EditorApplication.isPlaying){CheckPlay();return;}
   var rig=UnityEngine.Object.FindAnyObjectByType<AvatarRig>();if(!rig)throw new Exception("Abre la escena de Traigger");
   var camera=Array.Find(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None),c=>c.name=="Avatar Camera");
   var position=camera.transform.position;float size=camera.orthographicSize;Rect viewport=camera.rect;
   var materials=Array.ConvertAll(rig.layers,r=>r.sharedMaterial);var orders=Array.ConvertAll(rig.layers,r=>r.sortingOrder);
   var weights=Array.ConvertAll(rig.layers,r=>{var a=new float[r.sharedMesh.blendShapeCount];for(int i=0;i<a.Length;i++)a[i]=r.GetBlendShapeWeight(i);return a;});
   var visible=Array.ConvertAll(rig.layers,r=>r.enabled);var mouthPosition=rig.closedMouth?rig.closedMouth.transform.localPosition:Vector3.zero;
   var eyes=rig.gameObject.AddComponent<LayeredEyeRig>();eyes.rig=rig;eyes.manual=true;
   Directory.CreateDirectory("Validation/Eyes");
   try{
    if(ShaderUtil.ShaderHasError(Resources.Load<Shader>("LayeredEye")))throw new Exception("El shader del ojo no compila");
    eyes.Initialize();camera.rect=new Rect(0,0,1,1);camera.transform.position=new Vector3(rig.transform.position.x,rig.transform.position.y+3.8f,position.z);camera.orthographicSize=1.05f;
    rig.Look(0,0);rig.Face(FaceDeform.BlinkLeft,0);rig.Face(FaceDeform.BlinkRight,0);
    foreach(var pose in new[]{("neutral",Vector2.zero),("left",Vector2.left),("right",Vector2.right),("up",Vector2.up),("down",Vector2.down)}){
     eyes.manualLeft=eyes.manualRight=pose.Item2;for(int frame=0;frame<60;frame++)eyes.Step(null,Vector2.zero,1f/30);
     StudioSetup.Render("Validation/Eyes/"+pose.Item1+".png");
    }
    eyes.manualLeft=eyes.manualRight=new Vector2(.6f,.15f);
    foreach(float yaw in new[]{-18f,18f}){rig.Look(yaw,0);for(int frame=0;frame<60;frame++)eyes.Step(null,Vector2.zero,1f/30);StudioSetup.Render("Validation/Eyes/turn-"+(yaw<0?"left":"right")+".png");}
    rig.Look(0,0);rig.Face(FaceDeform.BlinkRight,1);for(int frame=0;frame<60;frame++)eyes.Step(null,Vector2.zero,1f/30);StudioSetup.Render("Validation/Eyes/wink.png");rig.Face(FaceDeform.BlinkRight,0);
    var source=rig.layers[Array.FindIndex(rig.definition.layers,l=>l.name=="EyeLeft")];var baked=new Mesh();source.BakeMesh(baked);var before=baked.vertices;
    eyes.manualLeft=Vector2.left;for(int frame=0;frame<60;frame++)eyes.Step(null,Vector2.zero,1f/30);source.BakeMesh(baked);var after=baked.vertices;
    for(int i=0;i<before.Length;i++)if((before[i]-after[i]).sqrMagnitude>1e-10f)throw new Exception("La mirada arrastra el blanco del ojo");UnityEngine.Object.DestroyImmediate(baked);
    eyes.manual=false;var packet=new FaceFrame{independentEyes=true,eyeLookLeftX=.8f,eyeLookLeftY=.3f,eyeLookRightX=-.5f,eyeLookRightY=-.2f};
    for(int frame=0;frame<60;frame++)eyes.Step(packet,Vector2.one,1f/30);
    if((eyes.leftGaze-new Vector2(.8f,.3f)).magnitude>.001f||(eyes.rightGaze-new Vector2(-.5f,-.2f)).magnitude>.001f)throw new Exception("Se promedia la mirada independiente");
    var input=rig.gameObject.AddComponent<ARKitFaceSource>();try{var buffer=new float[40];buffer[9]=.7f;buffer[11]=.3f;buffer[12]=.4f;buffer[15]=.2f;var f=input.Convert(buffer);if(!f.independentEyes||f.eyeLookLeftX>=0||f.eyeLookRightX<=0||f.eyeLookLeftY<=0||f.eyeLookRightY>=0)throw new Exception("Conversión ARKit independiente incorrecta");}finally{UnityEngine.Object.DestroyImmediate(input);}
    File.WriteAllText("Validation/Eyes/results.txt","PASS Unity "+Application.unityVersion+"\nTres capas por ojo: blanco/contorno, iris y pupila.\nMirada no desplaza la geometría del blanco; datos independientes y conversión ARKit comprobados.\nCapturas: centro, cuatro extremos, giros y parpadeo.\nPendiente calibración en iPhone físico y revisión de combinaciones extremas.\n");Debug.Log("TRAIGGER_LAYERED_EYES_PASS");
   }finally{
    eyes.Release();UnityEngine.Object.DestroyImmediate(eyes);camera.transform.position=position;camera.orthographicSize=size;camera.rect=viewport;
    for(int i=0;i<rig.layers.Length;i++){if(rig.layers[i].sharedMaterial!=materials[i]||rig.layers[i].sortingOrder!=orders[i])throw new Exception("No restaura el material/orden del ojo");rig.layers[i].enabled=visible[i];for(int j=0;j<weights[i].Length;j++)rig.layers[i].SetBlendShapeWeight(j,weights[i][j]);}
    if(rig.closedMouth)rig.closedMouth.transform.localPosition=mouthPosition;
   }
  }
  // Captura el renderer real en Play, sin BakeMesh ni alterar la escena guardada.
  static void CheckPlay(){
   var studio=UnityEngine.Object.FindAnyObjectByType<StudioController>();if(!studio||!studio.layeredEyes)throw new Exception("Espera a que arranque el avatar");
   var eyes=studio.layeredEyes;var cam=studio.avatarCamera;var position=cam.transform.position;var size=cam.orthographicSize;var viewport=cam.rect;
   bool manual=eyes.manual;var left=eyes.manualLeft;var right=eyes.manualRight;var target=cam.targetTexture;var active=RenderTexture.active;
   var rt=new RenderTexture(800,1000,24);var tex=new Texture2D(800,1000,TextureFormat.RGBA32,false);
   try{
    eyes.manual=true;eyes.manualLeft=eyes.manualRight=new Vector2(-.8f,.2f);for(int i=0;i<60;i++)eyes.Step(null,Vector2.zero,1f/30);
    cam.rect=new Rect(0,0,1,1);cam.transform.position=new Vector3(studio.rig.transform.position.x,studio.rig.transform.position.y+3.8f,position.z);cam.orthographicSize=1.05f;
    cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,800,1000),0,0);tex.Apply();Directory.CreateDirectory("Validation/Eyes");File.WriteAllBytes("Validation/Eyes/play.png",tex.EncodeToPNG());
    File.AppendAllText("Validation/Eyes/results.txt","Renderer real en Play: captura guardada para revisión visual.\n");Debug.Log("TRAIGGER_LAYERED_EYES_PLAY_CAPTURED");
   }finally{cam.targetTexture=target;RenderTexture.active=active;cam.transform.position=position;cam.orthographicSize=size;cam.rect=viewport;eyes.manual=manual;eyes.manualLeft=left;eyes.manualRight=right;eyes.Step(null,Vector2.zero,.1f);rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);}
  }

 }
}
