using System;using System.IO;using System.Linq;using System.Text;using UnityEditor;using UnityEngine;
namespace Traigger.Editor {
 // Prueba aislada del outfit real; no cambia la escena ni la selección del usuario.
 public static class PumpkinSleeveCheck {
  const string dir="Validation/PumpkinSleeves";
  [InitializeOnLoadMethod]static void Schedule(){EditorApplication.update+=Request;}
  static void Request(){if(File.Exists(dir+".request")&&!EditorApplication.isPlaying&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating){File.Delete(dir+".request");Run();}}
  [MenuItem("Traigger/Pruebas/Comprobar mangas del outfit Calabaza")]
  public static void Run(){
   Directory.CreateDirectory(dir);if(File.Exists(dir+"/error.txt"))File.Delete(dir+"/error.txt");GameObject root=null,camObject=null;var materials=new System.Collections.Generic.List<Material>();
   try{
    var original=UnityEngine.Object.FindAnyObjectByType<KemonoBodyRig>();if(!original)throw new Exception("Falta cuerpo preparado");
    root=UnityEngine.Object.Instantiate(original.gameObject);root.name="Prueba temporal Calabaza";root.transform.SetParent(null);root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.transform.localScale=Vector3.one;
    foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
    foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)){r.sharedMaterial=new Material(r.sharedMaterial);materials.Add(r.sharedMaterial);}
    var rig=root.GetComponent<AvatarRig>();rig.ownsResources=false;rig.CaptureRest();var body=root.GetComponent<KemonoBodyRig>();var seams=root.GetComponent<SleeveSeams>();seams.Sync();
    if(!Outfits.Apply(rig,"Traigger","Calabaza"))throw new Exception("No se carga outfit");
    var follower=root.GetComponent<OutfitFollower>();
    camObject=new GameObject("Cámara prueba Calabaza");var cam=camObject.AddComponent<Camera>();cam.orthographic=true;cam.orthographicSize=1.4f;cam.transform.position=new Vector3(0,2.8f,-10);cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.055f,.055f,.075f,1);cam.cullingMask=1<<31;
    float maxSeparation=0,cuffStretch=0;var report=new StringBuilder();
    foreach(var b in rig.bones.Where(b=>b.name.Contains("Arm")||b.name.Contains("Shoulder")))report.AppendLine(b.name+"="+b.position);
    foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name.Contains("Sleeve")||r.name=="Kemono_Torso"||r.name.Contains("UpperArm"))){var m=r.sharedMesh;var uv=m.uv;report.AppendLine(r.name+" bounds="+m.bounds+" uv="+uv.Min(v=>v.x)+","+uv.Min(v=>v.y)+".."+uv.Max(v=>v.x)+","+uv.Max(v=>v.y)+" order="+r.sortingOrder+" texture="+r.sharedMaterial.mainTexture.width+"x"+r.sharedMaterial.mainTexture.height);}
    foreach(float raise in new[]{0f,30f,60f,90f,120f,150f})foreach(float bend in new[]{0f,70f}){
     rig.ResetPose();rig.Rotate("RightUpperArm",-raise);rig.Rotate("LeftUpperArm",raise);rig.Rotate("RightLowerArm",-bend);rig.Rotate("LeftLowerArm",bend);body.ApplyShoulderFollow();body.ApplyConstraints();body.SyncHelpers();body.UpdateLayering();seams.Sync();follower.Sync();
     Validate(root,ref maxSeparation,ref cuffStretch);
     Save(cam,root,dir+"/pose-"+raise+"-"+bend+".png");
     if(bend==0&&(raise==0||raise==90||raise==150)){
      var pos=cam.transform.position;var size=cam.orthographicSize;
      foreach(var side in new[]{"Right","Left"}){var upper=Array.Find(rig.bones,b=>b.name==side+"UpperArm");var lower=Array.Find(rig.bones,b=>b.name==side+"LowerArm");var center=Vector3.Lerp(upper.position,lower.position,.64f);cam.transform.position=new Vector3(center.x,center.y,-10);cam.orthographicSize=.47f;Save(cam,root,dir+"/close-"+side+"-"+raise+".png");

      }
      cam.transform.position=pos;cam.orthographicSize=size;
     }
    }
    File.WriteAllText(dir+"/diagnostics.txt",report.ToString());
    File.WriteAllText(dir+"/results.txt","PASS Unity "+Application.unityVersion+"\n12 poses: elevación 0–150°, codo 0/70°.\n6 capas de manga: tela / borde delantero / forro.\nPesos normalizados, orden forro < piel < borde y geometría idéntica entre capas.\nSeparación máxima entre capas: "+maxSeparation.ToString("F8")+" unidades.\nCambio máximo de área del puño: "+cuffStretch.ToString("F6")+"\nRevisión visual: PNG de poses y primeros planos. Validación iOS/visionOS pendiente.\n");Debug.Log("TRAIGGER_PUMPKIN_SLEEVES_RENDERED");
   }catch(Exception e){File.WriteAllText(dir+"/error.txt",e.ToString());File.WriteAllText(dir+"/results.txt","FAIL: "+e.Message);Debug.LogException(e);}
   finally{if(root)UnityEngine.Object.DestroyImmediate(root);if(camObject)UnityEngine.Object.DestroyImmediate(camObject);foreach(var m in materials)if(m)UnityEngine.Object.DestroyImmediate(m);}
  }
  static void Validate(GameObject root,ref float separation,ref float stretch){
   var renderers=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
   foreach(string side in new[]{"Left","Right"}){
    SkinnedMeshRenderer Find(string name)=>Array.Find(renderers,r=>r.name==name);
    var cloth=Find("Outfit_PumpkinSleeve"+side+"Cloth");var cuff=Find("Outfit_PumpkinSleeve"+side+"Cuff");var rear=Find("Outfit_PumpkinSleeve"+side+"Lining");var arm=Find("Kemono_UpperArm"+side);var fore=Find("Kemono_Forearm"+side);
    if(!cloth||!cuff||!rear)throw new Exception("Faltan capas de manga "+side);
    if(rear.sortingOrder>=Mathf.Min(arm.sortingOrder,fore.sortingOrder)||cuff.sortingOrder<=Mathf.Max(arm.sortingOrder,fore.sortingOrder))throw new Exception("Orden incorrecto en "+side);
    foreach(var r in new[]{cloth,cuff,rear})foreach(var w in r.sharedMesh.boneWeights)if(Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1)>1e-5f)throw new Exception("Pesos sin normalizar");
    var baked=new Mesh();var other=new Mesh();
    try{
     cloth.BakeMesh(baked);var v=baked.vertices;
     foreach(var r in new[]{cuff,rear}){r.BakeMesh(other);var vv=other.vertices;if(v.Length!=vv.Length)throw new Exception("Mallas incompatibles");for(int i=0;i<v.Length;i++){separation=Mathf.Max(separation,Vector3.Distance(v[i],vv[i]));if(!float.IsFinite(v[i].x)||!float.IsFinite(v[i].y))throw new Exception("Malla no finita");}}
     if(separation>1e-5f)throw new Exception("Costura abierta entre capas");
     var source=cloth.sharedMesh.vertices;var uv=cloth.sharedMesh.uv;var tr=cloth.sharedMesh.triangles;
     var pigment=(Texture2D)cloth.sharedMaterial.GetTexture("_SourceTex");
     for(int i=0;i<tr.Length;i+=3){int a=tr[i],b=tr[i+1],c=tr[i+2];var center=(uv[a]+uv[b]+uv[c])/3;
      float x=(side=="Left"?center.x:1-center.x)*446,y=(1-center.y)*416;
      float along=(x-151)*.841f+(y-326)*.541f,across=-(x-151)*.541f+(y-326)*.841f;
      if(across< -54||Mathf.Abs(along)>140||pigment.GetPixelBilinear(center.x,center.y).a<.98f)continue;
      float rest=Vector3.Cross(source[b]-source[a],source[c]-source[a]).z,posed=Vector3.Cross(v[b]-v[a],v[c]-v[a]).z;
      if(Mathf.Abs(rest)<1e-8f)continue;if(rest*posed<=0)throw new Exception("Puño invertido");stretch=Mathf.Max(stretch,Mathf.Abs(posed/rest-1));
     }
    }finally{UnityEngine.Object.DestroyImmediate(baked);UnityEngine.Object.DestroyImmediate(other);}
   }
   if(stretch>.05f)throw new Exception("El puño se estira más del 5%: "+stretch);
  }
  public static void Save(Camera cam,GameObject root,string path){
   var rs=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.enabled&&!r.forceRenderingOff&&r.gameObject.activeInHierarchy).ToArray();var copies=new System.Collections.Generic.List<GameObject>();var rt=RenderTexture.GetTemporary(1000,850,24);var active=RenderTexture.active;var target=cam.targetTexture;
   try{foreach(var r in rs){var m=new Mesh();r.BakeMesh(m);var go=new GameObject("baked"){layer=31};go.transform.SetPositionAndRotation(r.transform.position,r.transform.rotation);go.transform.localScale=r.transform.lossyScale;go.AddComponent<MeshFilter>().sharedMesh=m;var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterial=r.sharedMaterial;mr.sortingOrder=r.sortingOrder;mr.sortingLayerID=r.sortingLayerID;r.enabled=false;copies.Add(go);}
    cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
   }finally{cam.targetTexture=target;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);foreach(var c in copies){UnityEngine.Object.DestroyImmediate(c.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(c);}foreach(var r in rs)r.enabled=true;}
  }
 }
}
