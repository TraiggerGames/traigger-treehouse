using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace Traigger.Editor {
    public static class MotionCheck {
        static StudioController studio;static double start;static int phase;static bool transition;static Vector3[] neutral;
        static SkinnedMeshRenderer face,hair;static Vector3 leftGoal,rightGoal;static Transform footL,footR;static float minSide,maxSide,footError,hairPeak;static bool oldDemo,oldTracking,oldBlink;static float maxRight,maxLeft,maxDistance;
        [MenuItem("Traigger/Pruebas/Comprobar transiciones y giro en Play")]
        static void Run(){
            if(!EditorApplication.isPlaying){Debug.LogError("Pulsa Play antes de probar el movimiento");return;}
            if(studio)return;
            studio=UnityEngine.Object.FindAnyObjectByType<StudioController>();if(!studio)throw new Exception("Sin controlador");
            face=studio.rig.layers.First(r=>r.name=="HeadBase");
            if(face.sharedMesh.GetBlendShapeIndex("TurnRight")<0){studio=null;throw new Exception("Regenera primero el rig con las deformaciones de giro");}
            hair=studio.rig.layers.First(r=>r.name=="FrontHair");footL=studio.rig.bones.First(b=>b.name=="RightFoot");footR=studio.rig.bones.First(b=>b.name=="LeftFoot");
            minSide=float.MaxValue;maxSide=float.MinValue;footError=hairPeak=0;
            oldDemo=studio.demo;oldTracking=studio.tracking;oldBlink=studio.autoBlink;
            studio.demo=studio.tracking=studio.autoBlink=false;studio.head=studio.turn=studio.nod=studio.lateral=studio.armLeft=studio.armRight=studio.mouth=studio.blinkLeft=studio.blinkRight=0;
            phase=0;transition=false;maxRight=maxLeft=maxDistance=0;start=EditorApplication.timeSinceStartup;
            EditorApplication.update+=Tick;
        }
        static Vector3[] Bake(){var mesh=new Mesh();face.BakeMesh(mesh);var v=mesh.vertices;UnityEngine.Object.DestroyImmediate(mesh);return v;}
        static void Tick(){
            if(!studio||!EditorApplication.isPlaying){Finish();return;}
            try{
                double age=EditorApplication.timeSinceStartup-start;
                if(phase>=3){
                    float x=studio.rig.transform.localPosition.x;minSide=Mathf.Min(minSide,x);maxSide=Mathf.Max(maxSide,x);
                    footError=Mathf.Max(footError,Vector3.Distance(footL.position,leftGoal),Vector3.Distance(footR.position,rightGoal));
                    foreach(string channel in new[]{"HairLeftPositive","HairLeftNegative","HairRightPositive","HairRightNegative"}){int index=hair.sharedMesh.GetBlendShapeIndex(channel);if(index>=0)hairPeak=Mathf.Max(hairPeak,hair.GetBlendShapeWeight(index));}
                }
                if(phase==0&&age>1.2){neutral=Bake();leftGoal=footL.position;rightGoal=footR.position;CheckHairRoot();Save("neutral");studio.turn=18;studio.armLeft=18;start=EditorApplication.timeSinceStartup;phase=1;return;}
                if(phase==1){
                    float w=face.GetBlendShapeWeight(face.sharedMesh.GetBlendShapeIndex("TurnRight"));maxRight=Mathf.Max(maxRight,w);
                    if(w>.1f&&w<95)transition=true;
                    if(age>1.8){var v=Bake();for(int i=0;i<v.Length;i++){float d=Vector3.Distance(v[i],neutral[i]);if(float.IsNaN(d)||float.IsInfinity(d))throw new Exception("Vértice no finito");maxDistance=Mathf.Max(maxDistance,d);}
                        Save("derecha");studio.turn=-18;studio.armLeft=-18;start=EditorApplication.timeSinceStartup;phase=2;return;}
                }
                if(phase==2){float w=face.GetBlendShapeWeight(face.sharedMesh.GetBlendShapeIndex("TurnLeft"));maxLeft=Mathf.Max(maxLeft,w);
                    if(age>2){Save("izquierda");if(!transition||maxRight<97||maxLeft<97||maxDistance<.02f)throw new Exception("Transición o giro insuficiente");
                        Directory.CreateDirectory("Validation");File.WriteAllText("Validation/motion-results.txt",$"PASS Unity {Application.unityVersion}\nTransición intermedia detectada: {transition}\nGiro derecha (%): {maxRight}\nGiro izquierda (%): {maxLeft}\nDesplazamiento malla cabeza: {maxDistance}\nRevisión artística completa: PENDIENTE\n");
                        studio.turn=studio.armLeft=0;studio.lateral=1;start=EditorApplication.timeSinceStartup;phase=3;return;}
                }
                if(phase==3&&age>1.8){Save("lateral-derecha");studio.lateral=-1;start=EditorApplication.timeSinceStartup;phase=4;return;}
                if(phase==4&&age>2){Save("lateral-izquierda");studio.lateral=0;start=EditorApplication.timeSinceStartup;phase=5;return;}
                if(phase==5&&age>1.5){
                    if(maxSide-minSide<.25f||footError>.025f||hairPeak<.5f)throw new Exception($"Lateral/apoyo/inercia insuficiente: rango={maxSide-minSide}, pies={footError}, pelo={hairPeak}");
                    File.AppendAllText("Validation/motion-results.txt",$"Recorrido lateral: {maxSide-minSide} unidades\nError máximo de apoyo: {footError} unidades\nRespuesta máxima del pelo: {hairPeak} %\nRaíces del pelo: fijas en sus correctivos\nLateral y pelo: PASS\n");
                    Debug.Log("TRAIGGER_MOTION_OK");Finish();
                }
            }catch(Exception e){Directory.CreateDirectory("Validation");File.AppendAllText("Validation/motion-results.txt","FAIL: "+e.Message+"\n");Debug.LogException(e);Finish();}
        }
        static void CheckHairRoot(){
            var mesh=hair.sharedMesh;var v=mesh.vertices;var delta=new Vector3[v.Length];
            foreach(string name in new[]{"HairLeftPositive","HairRightPositive","HairFringePositive"}){
                int index=mesh.GetBlendShapeIndex(name);if(index<0)throw new Exception("Falta deformación de pelo");
                mesh.GetBlendShapeFrameVertices(index,0,delta,new Vector3[v.Length],new Vector3[v.Length]);
                for(int i=0;i<v.Length;i++)if(v[i].y>4.35f&&delta[i].sqrMagnitude>1e-10f)throw new Exception("Se mueve la raíz del pelo");
            }
        }
        static void Save(string name){
            var cam=studio.avatarCamera;var previousTarget=cam.targetTexture;var previousActive=RenderTexture.active;
            var rt=new RenderTexture(800,1000,24);cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
            var tex=new Texture2D(800,1000,TextureFormat.RGBA32,false);tex.ReadPixels(new Rect(0,0,800,1000),0,0);tex.Apply();
            Directory.CreateDirectory("Validation");File.WriteAllBytes("Validation/motion-"+name+".png",tex.EncodeToPNG());
            cam.targetTexture=previousTarget;RenderTexture.active=previousActive;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);
        }
        static void Finish(){EditorApplication.update-=Tick;if(studio){studio.turn=studio.armLeft=studio.lateral=0;studio.demo=oldDemo;studio.tracking=oldTracking;studio.autoBlink=oldBlink;}studio=null;}
    }
}
