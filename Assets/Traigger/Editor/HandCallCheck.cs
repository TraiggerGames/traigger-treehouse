using System;using System.IO;using UnityEngine;using UnityEditor;
namespace Traigger.Editor {
 public static class HandCallCheck {
  [MenuItem("Traigger/Pruebas/Comprobar dedos y datos de manos")]
  public static void Check(){
   if(EditorApplication.isPlaying)throw new Exception("Sal de Play para generar las pruebas de manos");
   Directory.CreateDirectory("Validation/Hands");var hands=UnityEngine.Object.FindObjectsByType<FingerPoseRig>(FindObjectsSortMode.None);if(hands.Length!=2)throw new Exception("Prepara las dos manos");
   var camera=Array.Find(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None),c=>c.name=="Avatar Camera");var oldPosition=camera.transform.position;float oldSize=camera.orthographicSize;var report=new System.Text.StringBuilder();
   try{foreach(var hand in hands){
    var renderer=hand.GetComponent<SkinnedMeshRenderer>();var mesh=renderer.sharedMesh;
    if(hand.joints.Length!=12||mesh.vertexCount!=22265)throw new Exception("Topología inesperada");
    foreach(var w in mesh.boneWeights)if(Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1)>.0001f)throw new Exception("Pesos de dedo sin normalizar");
    if(mesh.bindposes.Length!=renderer.bones.Length)throw new Exception("Bindposes incompatibles");
    foreach(var w in mesh.boneWeights)if(w.boneIndex0>=renderer.bones.Length||w.boneIndex1>=renderer.bones.Length||w.boneIndex2>=renderer.bones.Length||w.boneIndex3>=renderer.bones.Length)throw new Exception("Índice de hueso fuera de rango");
    bool experimental=hand.experimentalDeformation;hand.experimentalDeformation=true;
    bool manual=hand.manual;var controls=(float[])hand.controls.Clone();var jointPos=Array.ConvertAll(hand.joints,j=>j.localRotation);
    try{
     Vector3[] baseline=null;float moved=0;
     foreach(float curl in new[]{0f,.5f,1f}){
      hand.Pose(curl,curl,curl,curl);for(int k=0;k<30;k++)hand.Evaluate(1f/30);
      var baked=new Mesh();renderer.BakeMesh(baked);var vertices=baked.vertices;
      foreach(var v in vertices)if(float.IsNaN(v.x)||float.IsInfinity(v.x)||float.IsNaN(v.y)||float.IsInfinity(v.y))throw new Exception("Vértices no finitos");
      if(baseline==null)baseline=vertices;else for(int n=0;n<vertices.Length;n++)moved=Mathf.Max(moved,Vector3.Distance(baseline[n],vertices[n]));
      var center=renderer.bones[renderer.bones.Length-12].position;camera.transform.position=new Vector3(center.x,center.y-.06f,oldPosition.z);camera.orthographicSize=.32f;
      StudioSetup.Render("Validation/Hands/"+(hand.leftHuman?"left":"right")+"-"+curl+".png");UnityEngine.Object.DestroyImmediate(baked);
     }
     if(moved<.005f)throw new Exception("Los dedos no deforman la malla");report.AppendLine((hand.leftHuman?"Izquierda":"Derecha")+": pesos correctos; 12 huesos; desplazamiento al cerrar "+moved.ToString("F4"));
    }finally{hand.experimentalDeformation=experimental;hand.manual=manual;hand.controls=controls;for(int n=0;n<hand.joints.Length;n++)hand.joints[n].localRotation=jointPos[n];}
   }
   var go=new GameObject("Hand packet check");try{var input=go.AddComponent<VisionMotionInput>();
    input.OnNative("{\"left\":{\"tracked\":true,\"sampleAge\":0,\"curls\":[0,0.25,0.5,0.75,1]}}");if(input.leftCurls[2]!=.5f)throw new Exception("No lee cierres sin pose de cabeza");
    input.OnNative("{\"left\":{\"tracked\":true,\"sampleAge\":0.9,\"curls\":[1,1,1,1,1]}}");if(input.lastLeftHand!=-100)throw new Exception("No rechaza datos viejos");
    input.OnNative("{\"right\":{\"tracked\":true,\"curls\":[0,-1,0,0,0]}}");if(input.lastRightHand!=-100)throw new Exception("No rechaza articulaciones no detectadas");
    input.OnNative("malformed");report.AppendLine("Datos: cierres sin cabeza, caducidad, pérdida de articulaciones y JSON malformado comprobados.");
   }finally{UnityEngine.Object.DestroyImmediate(go);}
   var relayObject=new GameObject("Local UDP check");relayObject.SetActive(false);
   try{var target=relayObject.AddComponent<VisionMotionInput>();var receiver=relayObject.AddComponent<MotionRelayReceiver>();receiver.port=0;receiver.target=target;relayObject.SetActive(true);receiver.StartListening();if(receiver.BoundPort==0)throw new Exception(receiver.status);
    using(var sender=new System.Net.Sockets.UdpClient()){var bytes=System.Text.Encoding.UTF8.GetBytes("{\"left\":{\"tracked\":true,\"sampleAge\":0,\"curls\":[0,0.25,0.5,0.75,1]}}");sender.Send(bytes,bytes.Length,new System.Net.IPEndPoint(System.Net.IPAddress.Loopback,receiver.BoundPort));}
    var deadline=System.Diagnostics.Stopwatch.StartNew();bool received=false;while(deadline.ElapsedMilliseconds<1500&&!received){received=receiver.ConsumePending();if(!received)System.Threading.Thread.Sleep(5);}
    if(!received||target.leftCurls[2]!=.5f)throw new Exception("Fallo en el transporte UDP local");report.AppendLine("UDP: datagrama real recibido y aplicado en el hilo principal por loopback.");
   }finally{relayObject.GetComponent<MotionRelayReceiver>().StopListening();UnityEngine.Object.DestroyImmediate(relayObject);}
   File.WriteAllText("Validation/Hands/results.txt","PASS Unity "+Application.unityVersion+"\n"+report+"No sustituye prueba física con Vision Pro ni validación artística de un puño cerrado.\n");Debug.Log("TRAIGGER_HANDS_PASS");
   }finally{camera.transform.position=oldPosition;camera.orthographicSize=oldSize;}
  }
 }
}
