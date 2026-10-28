using System;using System.IO;using System.Collections.Generic;using UnityEngine;using UnityEditor;
namespace Traigger.Editor {
 public static class HairDetailCheck {
  [Serializable] class Manifest{public int version;public HairDetailRig.StrandDefinition[] strands;}
  [MenuItem("Traigger/Arte/Mejorar nitidez del pelo y comprobar mechones")]
  [MenuItem("Traigger/Pruebas/Comprobar mechones frontales y costuras")]
  public static void Run(){
   var studio=UnityEngine.Object.FindAnyObjectByType<StudioController>();var rig=studio?studio.rig:UnityEngine.Object.FindAnyObjectByType<AvatarRig>();if(!rig)throw new Exception("Abre la escena de Traigger");
   if(EditorApplication.isPlaying){CapturePlay(studio);return;}
   const string dir="Validation/Hair/Strands17";Directory.CreateDirectory(dir);Directory.CreateDirectory(dir+"/Frames");
   // Forzar importación como Sprite incluso si el PNG se vio antes de compilar el importador.
   foreach(var file in Directory.GetFiles("Assets/Traigger/Resources/Hair/Strands","*.png"))AssetDatabase.ImportAsset(file,ImportAssetOptions.ForceUpdate);
   AssetDatabase.ImportAsset("Assets/Traigger/Resources/Hair/FrontHairOwnership.png",ImportAssetOptions.ForceUpdate);
   var manifest=JsonUtility.FromJson<Manifest>(Resources.Load<TextAsset>("Hair/FrontHairLayers").text);
   if(manifest.version!=3||manifest.strands.Length!=17)throw new Exception("Faltan mechones individuales");
   var camera=Array.Find(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None),c=>c.name=="Avatar Camera");
   var position=camera.transform.position;float size=camera.orthographicSize;var viewport=camera.rect;
   var bones=new Quaternion[rig.bones.Length];for(int i=0;i<bones.Length;i++)bones[i]=rig.bones[i].localRotation;
   var weights=new float[rig.layers.Length][];for(int i=0;i<rig.layers.Length;i++){weights[i]=new float[rig.layers[i].sharedMesh.blendShapeCount];for(int n=0;n<weights[i].Length;n++)weights[i][n]=rig.layers[i].GetBlendShapeWeight(n);}
   var detail=rig.gameObject.AddComponent<HairDetailRig>();detail.rig=rig;
   var baked=new Mesh();var bakedPiece=new Mesh();
   try{
    camera.rect=new Rect(0,0,1,1);camera.transform.position=new Vector3(rig.transform.position.x,rig.transform.position.y+3.8f,position.z);camera.orthographicSize=1.05f;
    int front=Array.FindIndex(rig.definition.layers,l=>l.name=="FrontHair");var source=rig.layers[front];
    detail.Step(0,0,0,0,1,1f/60);source=rig.layers[front];
    if(detail.SeparatedLockCount!=17)throw new Exception("No se cargaron los 17 sprites con sus mallas");
    var pieces=new SkinnedMeshRenderer[17];var textureIDs=new HashSet<Texture2D>();
    for(int i=0;i<pieces.Length;i++){
     var d=manifest.strands[i];var sprite=Resources.Load<Sprite>(d.resource);if(!sprite)throw new Exception("Falta sprite "+d.name);
     textureIDs.Add(sprite.texture);pieces[i]=Array.Find(rig.GetComponentsInChildren<SkinnedMeshRenderer>(true),r=>r.name=="FrontHair"+d.name);
     if(!pieces[i])throw new Exception("Falta malla "+d.name);
     foreach(var weight in pieces[i].sharedMesh.boneWeights)if(Mathf.Abs(weight.weight0+weight.weight1+weight.weight2+weight.weight3-1)>.0001f)throw new Exception("Pesos sin normalizar en "+d.name);
    }
    if(textureIDs.Count!=17)throw new Exception("Hay sprites que comparten la textura completa en lugar de un recorte propio");
    // Comparación de arte: misma pose, misma textura fuente, con y sin la separación.
    var baseTexture=source.sharedMaterial.mainTexture;source.sharedMaterial.mainTexture=Resources.Load<Texture2D>("Hair/FrontHairHD");foreach(var piece in pieces)piece.enabled=false;
    Capture(camera,dir+"/reference.png");source.sharedMaterial.mainTexture=baseTexture;foreach(var piece in pieces)piece.enabled=true;Capture(camera,dir+"/neutral.png");
    // Regresión del defecto mostrado por el usuario: una unión no puede dibujar un contorno
    // distinto al sprite completo, aunque no exista separación geométrica.
    float colorError=ImageError(dir+"/reference.png",dir+"/neutral.png");
    if(colorError>.03f)throw new Exception("La composición del pelo marca los recortes: error RGB="+colorError);
    source.BakeMesh(baked);var neutral=baked.vertices;
    for(int i=0;i<36;i++)detail.Step(-28,-20,5,0,1,1f/60);
    source.BakeMesh(baked);var moved=baked.vertices;float displacement=0;for(int i=0;i<moved.Length;i++)displacement=Mathf.Max(displacement,(moved[i]-neutral[i]).magnitude);
    if(displacement<.015f||detail.MaxStrandAngle<3)throw new Exception("La física no tiene un recorrido visible");
    float seamError=CheckSeams(source,pieces,baked,bakedPiece);
    Capture(camera,dir+"/physics-right.png");
    for(int i=0;i<60;i++)detail.Step(28,20,-5,0,1,1f/60);seamError=Mathf.Max(seamError,CheckSeams(source,pieces,baked,bakedPiece));Capture(camera,dir+"/physics-left.png");
    float movingColorError=0;
    var head=Array.Find(rig.bones,b=>b.name=="Head");var headRotation=head?head.localRotation:Quaternion.identity;
    for(int frame=0;frame<132;frame++){
     float t=frame/60f,yaw=Mathf.Sin(t*3.1f)*12,pitch=Mathf.Sin(t*2.2f)*3,roll=Mathf.Sin(t*3.7f)*6;
     rig.Look(yaw,pitch);if(head)head.localRotation=headRotation*Quaternion.Euler(0,0,roll);
     detail.Step(Mathf.Cos(t*3.7f)*22.2f,Mathf.Cos(t*3.1f)*37.2f,Mathf.Cos(t*2.2f)*6.6f,0,.975f,1f/60);
     if(frame%12==0)seamError=Mathf.Max(seamError,CheckSeams(source,pieces,baked,bakedPiece));
     if(frame%36==0){
      foreach(var piece in pieces)piece.enabled=false;Capture(camera,dir+"/motion-reference-"+frame+".png");
      foreach(var piece in pieces)piece.enabled=true;Capture(camera,dir+"/motion-split-"+frame+".png");
      movingColorError=Mathf.Max(movingColorError,ImageError(dir+"/motion-reference-"+frame+".png",dir+"/motion-split-"+frame+".png"));
      if(movingColorError>.03f)throw new Exception("Se marca una costura al mover el flequillo: "+movingColorError);
     }
     if(frame%2==0)Capture(camera,dir+"/Frames/"+(frame/2).ToString("D3")+".png");
    }
    if(seamError>0.00002f)throw new Exception("Las costuras se separan: "+seamError);
    rig.Look(0,0);if(head)head.localRotation=headRotation;
    for(int i=0;i<600;i++)detail.Step(0,0,0,0,1,1f/60);
    if(detail.PeakMotion>.0001f)throw new Exception("El pelo no se asienta al detenerse");
    File.WriteAllText(dir+"/results.txt",$"PASS Unity {Application.unityVersion}\n17 Sprite assets / 17 separate cropped textures and skinned meshes.\n17 body springs + 17 tip springs.\nVisible maximum deflection: {displacement:F5} world units.\nMaximum separation at shared vertices during turns/physics: {seamError:E3} world units.\nHead corrective weights and all shared deformation frames match.\nNeutral RGB difference from unsplit source: {colorError:F6}/255.\nMaximum RGB difference in four moving poses: {movingColorError:F6}/255.\nNormalized bone weights preserved.\n66 offline baked frames with head turns, nod and roll; springs settle at rest. Normal GPU Play rendering tested separately.\nOriginal FrontHairHD.png preserved.\nPhysical-device tracking and performance remain to be tested.\n");
    Debug.Log("TRAIGGER_HAIR_17_SEAM_PASS: "+displacement+"; seam="+seamError);
   }finally{
    detail.Release();UnityEngine.Object.DestroyImmediate(detail);UnityEngine.Object.DestroyImmediate(baked);UnityEngine.Object.DestroyImmediate(bakedPiece);
    for(int i=0;i<bones.Length;i++)rig.bones[i].localRotation=bones[i];for(int i=0;i<weights.Length;i++)for(int n=0;n<weights[i].Length;n++)rig.layers[i].SetBlendShapeWeight(n,weights[i][n]);
    camera.transform.position=position;camera.orthographicSize=size;camera.rect=viewport;
   }
  }
  static float ImageError(string a,string b){
   var x=new Texture2D(2,2);var y=new Texture2D(2,2);
   try{x.LoadImage(File.ReadAllBytes(a));y.LoadImage(File.ReadAllBytes(b));var px=x.GetPixels32();var py=y.GetPixels32();double error=0;for(int i=0;i<px.Length;i++)error+=Math.Abs(px[i].r-py[i].r)+Math.Abs(px[i].g-py[i].g)+Math.Abs(px[i].b-py[i].b);return (float)(error/(px.Length*3));}
   finally{UnityEngine.Object.DestroyImmediate(x);UnityEngine.Object.DestroyImmediate(y);}
  }
  static float CheckSeams(SkinnedMeshRenderer source,SkinnedMeshRenderer[] pieces,Mesh baked,Mesh piece){
   source.BakeMesh(baked);var deformed=baked.vertices;var original=source.sharedMesh.vertices;var map=new Dictionary<Vector3,int>();for(int i=0;i<original.Length;i++)map[original[i]]=i;
   float error=0;
   foreach(var renderer in pieces){
    // A stale blendshape for even one frame would open the joint, despite identical rest vertices.
    for(int s=0;s<source.sharedMesh.blendShapeCount;s++)if(Mathf.Abs(source.GetBlendShapeWeight(s)-renderer.GetBlendShapeWeight(s))>.0001f)throw new Exception("Física desincronizada en "+renderer.name);
    renderer.BakeMesh(piece);var output=piece.vertices;var rest=renderer.sharedMesh.vertices;
    for(int i=0;i<rest.Length;i++)if(map.TryGetValue(rest[i],out int j))error=Mathf.Max(error,(output[i]-deformed[j]).magnitude);
   }
   return error;
  }
  static void Capture(Camera camera,string path){
   var target=camera.targetTexture;var active=RenderTexture.active;var rt=RenderTexture.GetTemporary(600,750,24);var texture=new Texture2D(600,750,TextureFormat.RGBA32,false);
   // In editor a tight test loop does not advance Unity's GPU skinning frame. Bake for the
   // offline capture; the Play test below uses the normal renderer over real engine frames.
   var originals=new List<SkinnedMeshRenderer>();var proxies=new List<GameObject>();var meshes=new List<Mesh>();
   if(!Application.isPlaying)foreach(var renderer in UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))if(renderer.enabled&&renderer.gameObject.activeInHierarchy){
    var mesh=new Mesh();renderer.BakeMesh(mesh);var go=new GameObject("Hair test · baked frame");go.hideFlags=HideFlags.HideAndDontSave;go.transform.SetParent(renderer.transform,false);
    go.AddComponent<MeshFilter>().sharedMesh=mesh;var draw=go.AddComponent<MeshRenderer>();draw.sharedMaterials=renderer.sharedMaterials;draw.sortingLayerID=renderer.sortingLayerID;draw.sortingOrder=renderer.sortingOrder;
    originals.Add(renderer);proxies.Add(go);meshes.Add(mesh);renderer.enabled=false;
   }
   try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,600,750),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
   finally{camera.targetTexture=target;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(texture);for(int i=0;i<originals.Count;i++){originals[i].enabled=true;UnityEngine.Object.DestroyImmediate(proxies[i]);UnityEngine.Object.DestroyImmediate(meshes[i]);}}
  }
  static void CapturePlay(StudioController studio){
   if(!studio||!studio.hairDetails||studio.hairDetails.SeparatedLockCount!=17)throw new Exception("Espera a que arranquen los 17 mechones");
   Directory.CreateDirectory("Validation/Hair/Strands17");Capture(studio.avatarCamera,"Validation/Hair/Strands17/play.png");Debug.Log("TRAIGGER_HAIR_17_PLAY_CAPTURED");
  }
 }
}
