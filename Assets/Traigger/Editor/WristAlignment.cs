using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Traigger.Editor {
 public static class WristAlignment {
  // Interpolación en la cuadrícula original: no aproxima el anclaje al vértice más cercano.
  static Vector3 Point(Mesh mesh,Vector2 uv){
   var uvs=mesh.uv;var v=mesh.vertices;float u=(uv.x-uvs[0].x)/(uvs[10].x-uvs[0].x)*10;
   float y=(uv.y-uvs[0].y)/(uvs[396].y-uvs[0].y)*36;
   int x0=Mathf.Clamp(Mathf.FloorToInt(u),0,9),y0=Mathf.Clamp(Mathf.FloorToInt(y),0,35);int a=y0*11+x0;
   return Vector3.Lerp(Vector3.Lerp(v[a],v[a+1],u-x0),Vector3.Lerp(v[a+11],v[a+12],u-x0),y-y0);
  }
  [MenuItem("Traigger/Mangas/Alinear muñecas con el borde del guante")]
  public static void Apply(){
   if(EditorApplication.isPlaying)throw new Exception("Sal de Play para ajustar las muñecas");
   var body=UnityEngine.Object.FindAnyObjectByType<KemonoBodyRig>();if(body.GetComponentsInChildren<FingerPoseRig>().Length>0)throw new Exception("Las muñecas ya tienen dedos articulados: restaura la copia previa para recalcular el encaje; no se modifica el rig actual");var rig=body.rig;rig.CaptureRest();
   Directory.CreateDirectory("Validation/Wrists");File.Copy(rig.gameObject.scene.path,"Validation/Wrists/scene-before-wrists.unity.bak",true);
   Undo.RegisterFullObjectHierarchyUndo(rig.gameObject,"Alinear muñecas");
   var asset=ScriptableObject.CreateInstance<RigDefinition>();AssetDatabase.CreateAsset(asset,AssetDatabase.GenerateUniqueAssetPath("Assets/Traigger/Art/BodyV12/WristFit.asset"));
   var report=new System.Text.StringBuilder();
   for(int side=0;side<2;side++){
    string label=side==0?"Left":"Right",bone=side==0?"Right":"Left";
    var hand=Array.Find(body.parts,r=>r.name=="Kemono_Hand"+label);
    var wrist=Array.Find(rig.bones,b=>b.name==bone+"Hand");var elbow=Array.Find(rig.bones,b=>b.name==bone+"LowerArm");
    var camera=Array.Find(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None),c=>c.name=="Avatar Camera");
    var camPos=camera.transform.position;float camSize=camera.orthographicSize;
    void Capture(string name){camera.transform.position=new Vector3(wrist.position.x,wrist.position.y-.06f,camPos.z);camera.orthographicSize=.28f;StudioSetup.Render("Validation/Wrists/"+label+"-"+name+".png");}
    try{
     Capture("before");
     var mesh=UnityEngine.Object.Instantiate(hand.sharedMesh);mesh.name="Hand"+label+"_wristFit";
     // Centros de los extremos del borde proximal azul dibujado en el atlas, en píxeles.
     Vector2 UV(float x,float y)=>new Vector2(x/1254f,1-y/1254f);
     Vector3 a=Point(mesh,side==0?UV(263,198):UV(813,230));
     Vector3 b=Point(mesh,side==0?UV(445,230):UV(995,198));
     Vector3 center=(a+b)*.5f,edge=b-a;
     Vector3 axis=new Vector3(edge.y,-edge.x,0).normalized;
     Vector3 target=hand.transform.InverseTransformVector(wrist.position-elbow.position).normalized;
     if(Vector3.Dot(axis,target)<0)axis=-axis;
     float angle=Vector3.SignedAngle(axis,target,Vector3.forward);var rotation=Quaternion.Euler(0,0,angle);
     Vector3 anchor=hand.transform.InverseTransformPoint(wrist.position);var vertices=mesh.vertices;
     for(int i=0;i<vertices.Length;i++)vertices[i]=anchor+rotation*(vertices[i]-center);
     mesh.vertices=vertices;mesh.RecalculateBounds();hand.sharedMesh=mesh;AssetDatabase.AddObjectToAsset(mesh,asset);EditorUtility.SetDirty(hand);
     report.AppendLine(label+": corrección del eje "+angle.ToString("F2")+" grados; desplazamiento "+Vector3.Distance(center,anchor).ToString("F4"));
     foreach(float bend in new[]{0f,-45f,45f}){
      rig.ResetPose();rig.Rotate(bone+"Hand",bend);body.SyncHelpers();body.UpdateLayering();var seams=body.GetComponent<SleeveSeams>();if(seams)seams.Sync();Capture("after-"+bend);
     }
    }finally{rig.ResetPose();body.SyncHelpers();camera.transform.position=camPos;camera.orthographicSize=camSize;}
   }
   body.UpdateLayering();body.GetComponent<SleeveSeams>()?.Sync();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);EditorSceneManager.SaveScene(rig.gameObject.scene);
   File.WriteAllText("Validation/Wrists/results.txt",report.ToString());Debug.Log("TRAIGGER_WRISTS_ALIGNED");
  }
 }
}
