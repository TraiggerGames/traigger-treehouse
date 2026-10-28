using UnityEngine;using UnityEditor;
namespace Traigger.Editor {
 public class AvatarCallWindow:EditorWindow {
  Camera output;RenderTexture texture;
  [MenuItem("Traigger/Llamadas/Ventana de avatar para OBS")]
  static void Open(){var w=GetWindow<AvatarCallWindow>(true,"Traigger · Cámara");w.minSize=new Vector2(640,360);w.Show();}
  void OnGUI(){
   var controller=Object.FindAnyObjectByType<StudioController>();if(!controller||!controller.avatarCamera){EditorGUILayout.HelpBox("Abre TraiggerStudio-KemonoBody y pulsa Play.",MessageType.Info);return;}
   if(!output){output=new GameObject("Avatar call preview"){hideFlags=HideFlags.HideAndDontSave}.AddComponent<Camera>();output.enabled=false;texture=new RenderTexture(1280,720,24);texture.Create();}
   output.CopyFrom(controller.avatarCamera);output.enabled=false;output.targetTexture=texture;output.aspect=16f/9;output.rect=new Rect(0,0,1,1);output.transform.position=new Vector3(controller.rig.transform.position.x,controller.rig.transform.position.y+3f,controller.avatarCamera.transform.position.z);output.orthographicSize=2.3f;output.backgroundColor=new Color(.065f,.08f,.12f,1);output.clearFlags=CameraClearFlags.SolidColor;
   if(controller.framing&&controller.framing.Active)controller.framing.Apply(output,16f/9);
   output.Render();GUI.DrawTexture(new Rect(0,0,position.width,position.height),texture,ScaleMode.ScaleToFit);Repaint();
  }
  void OnDisable(){if(output)DestroyImmediate(output.gameObject);if(texture){texture.Release();DestroyImmediate(texture);}}
 }
 public class HandCallPanel:EditorWindow {
  [MenuItem("Traigger/Llamadas/Controles y conexión")]
  static void Open()=>GetWindow<HandCallPanel>(false,"Traigger · Llamadas");
  void OnGUI(){
   EditorGUILayout.LabelField("Vision Pro → Mac → cámara virtual",EditorStyles.boldLabel);
   var input=Object.FindAnyObjectByType<VisionMotionInput>();
   if(input){EditorGUILayout.LabelField("Manos",$"Izq: {(Time.unscaledTime-input.lastLeftHand<.3f?"recibida":"sin señal")} · Der: {(Time.unscaledTime-input.lastRightHand<.3f?"recibida":"sin señal")}");
    if(Application.isPlaying&&GUILayout.Button("Recibir Vision Pro por la red local · UDP 49984")){var receiver=input.GetComponent<MotionRelayReceiver>();if(!receiver)receiver=input.gameObject.AddComponent<MotionRelayReceiver>();receiver.target=input;}
    var r=input.GetComponent<MotionRelayReceiver>();if(r)EditorGUILayout.LabelField(r.status);
   }
   foreach(var hand in Object.FindObjectsByType<FingerPoseRig>(FindObjectsSortMode.None)){
    EditorGUILayout.LabelField(hand.leftHuman?"Mano izquierda":"Mano derecha",EditorStyles.boldLabel);hand.experimentalDeformation=EditorGUILayout.Toggle("Dedos experimentales",hand.experimentalDeformation);hand.manual=EditorGUILayout.Toggle("Control manual",hand.manual);
    EditorGUILayout.HelpBox("El cierre todavía puede abrir cortes en el dibujo. Desactivado conserva la mano original. No es un puño terminado.",MessageType.Warning);
    string[] names={"Pulgar","Índice","Medio","Exterior"};for(int f=0;f<4;f++)hand.controls[f]=EditorGUILayout.Slider(names[f],hand.controls[f],0,1);
    EditorGUILayout.BeginHorizontal();if(GUILayout.Button("Abierta"))hand.Pose(0,0,0,0);if(GUILayout.Button("Cerrar"))hand.Pose(1,1,1,1);if(GUILayout.Button("Señalar"))hand.Pose(.7f,0,1,1);if(GUILayout.Button("V"))hand.Pose(1,0,0,1);EditorGUILayout.EndHorizontal();
   }
   EditorGUILayout.HelpBox("OBS: captura la ventana «Traigger · Cámara», inicia Cámara virtual y selecciónala en FaceTime del Mac. No inicia llamadas ni transmite vídeo automáticamente. F8 oculta el panel en una aplicación compilada; Escape lo restaura.",MessageType.Info);
  }
 }
}
