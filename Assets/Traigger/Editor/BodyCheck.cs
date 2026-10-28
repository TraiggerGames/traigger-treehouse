using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Traigger.Editor {
    [InitializeOnLoad]
    public static class BodyCheck {
        const string pendingKey="Traigger.BodyCheck.Pending";
        static BodyCheck(){if(SessionState.GetBool(pendingKey,false))EditorApplication.update+=StartWhenReady;}
        static void StartWhenReady(){
            if(!EditorApplication.isPlaying||!UnityEngine.Object.FindAnyObjectByType<StudioController>())return;
            EditorApplication.update-=StartWhenReady;SessionState.SetBool(pendingKey,false);Run();
        }
        static StudioController c;static int phase;static double until;static Dictionary<string,Vector3[]> baseline;static readonly List<string> results=new List<string>();
        static bool demo,tracking,blink;static float physics;
        static readonly string[] labels={"neutral","shoulders","upper-arms","elbows","wrists","thighs","knees","ankles-toes","hands","wave"};
        [MenuItem("Traigger/Pruebas/Comprobar cuerpo articulado en Play")]
        public static void Run(){
            if(!EditorApplication.isPlaying){
                SessionState.SetBool(pendingKey,true);EditorApplication.update-=StartWhenReady;EditorApplication.update+=StartWhenReady;
                EditorApplication.isPlaying=true;return;
            }
            if(c)throw new Exception("Prueba ya en marcha");
            c=UnityEngine.Object.FindAnyObjectByType<StudioController>();if(!c)throw new Exception("Sin controlador");
            demo=c.demo;tracking=c.tracking;blink=c.autoBlink;physics=c.physics;c.demo=c.tracking=c.autoBlink=false;c.physics=0;
            c.head=c.turn=c.nod=c.lateral=c.torso=c.hips=c.neck=c.earLeft=c.earRight=c.earMood=c.smile=0;c.mouth=c.blinkLeft=c.blinkRight=0;
            results.Clear();Clear();phase=0;until=EditorApplication.timeSinceStartup+1.8;
            EditorApplication.update+=Tick;
        }
        static void Clear(){c.shoulderLeft=c.shoulderRight=c.upperArmLeft=c.upperArmRight=c.armLeft=c.armRight=c.wristLeft=c.wristRight=c.thighLeft=c.thighRight=c.legLeft=c.legRight=c.ankleLeft=c.ankleRight=c.toesLeft=c.toesRight=0;}
        static Dictionary<string,Vector3[]> Sample(){
            var data=new Dictionary<string,Vector3[]>();
            foreach(var r in c.rig.GetComponentsInChildren<SkinnedMeshRenderer>()){
                if(!r.enabled)continue;var weights=r.sharedMesh.boneWeights;
                foreach(var w in weights){float sum=w.weight0+w.weight1+w.weight2+w.weight3;if(Mathf.Abs(sum-1)>.001f)throw new Exception("Pesos sin normalizar: "+r.name);}
                var mesh=new Mesh();r.BakeMesh(mesh);var vertices=mesh.vertices;
                for(int i=0;i<vertices.Length;i++){vertices[i]=r.transform.TransformPoint(vertices[i]);if(float.IsNaN(vertices[i].sqrMagnitude)||float.IsInfinity(vertices[i].sqrMagnitude))throw new Exception("Vértice no finito: "+r.name);}
                data[r.name]=vertices;UnityEngine.Object.DestroyImmediate(mesh);
            }
            return data;
        }
        static void Tick(){
            if(!c||!EditorApplication.isPlaying){Finish("CANCELADA");return;}if(EditorApplication.timeSinceStartup<until)return;
            try{
                var current=Sample();Directory.CreateDirectory("Validation");
                if(phase==0){baseline=current;results.Add("Reposo: pesos normalizados y mallas finitas");}
                else if(phase<=7){
                    string[] names=phase==1?new[]{"RightShoulder","LeftShoulder"}:phase==2?new[]{"RightUpperArm","LeftUpperArm"}:phase==3?new[]{"RightLowerArm","LeftLowerArm"}:phase==4?new[]{"RightHand","LeftHand"}:phase==5?new[]{"RightUpperLeg","LeftUpperLeg"}:phase==6?new[]{"RightLowerLeg","LeftLowerLeg"}:new[]{"RightFoot","LeftFoot","RightToes","LeftToes"};
                    foreach(string name in names){
                        int index=Array.FindIndex(c.rig.bones,b=>b.name==name);if(index<0)throw new Exception("Falta hueso: "+name);
                        bool weighted=false;
                        foreach(var r in c.rig.GetComponentsInChildren<SkinnedMeshRenderer>())if(r.enabled)foreach(var w in r.sharedMesh.boneWeights)if((w.boneIndex0==index&&w.weight0>.01f)||(w.boneIndex1==index&&w.weight1>.01f)||(w.boneIndex2==index&&w.weight2>.01f)||(w.boneIndex3==index&&w.weight3>.01f))weighted=true;
                        if(!weighted)throw new Exception("Articulación sin influencia visible: "+name);
                    }
                    float max=0;foreach(var pair in current)if(baseline.TryGetValue(pair.Key,out var before)&&before.Length==pair.Value.Length)for(int i=0;i<before.Length;i++)max=Mathf.Max(max,Vector3.Distance(before[i],pair.Value[i]));
                    if(max<.005f)throw new Exception("Pose sin deformación: "+labels[phase]);results.Add(labels[phase]+": articulaciones con pesos visibles, recorrido "+max.ToString("F4"));
                }
                StudioSetup.Render("Validation/body-"+labels[phase]+".png");
                phase++;if(phase>=labels.Length){Finish("PASS");return;}
                Clear();
                switch(phase){
                    case 1:c.shoulderLeft=-15;c.shoulderRight=15;break;
                    case 2:c.upperArmLeft=-45;c.upperArmRight=45;break;
                    case 3:c.armLeft=-70;c.armRight=70;break;
                    case 4:c.wristLeft=-40;c.wristRight=40;break;
                    case 5:c.thighLeft=-22;c.thighRight=22;break;
                    case 6:c.legLeft=35;c.legRight=-35;break;
                    case 7:c.ankleLeft=-25;c.ankleRight=25;c.toesLeft=20;c.toesRight=-20;break;
                    case 8:c.wristRight=-25;c.upperArmLeft=-35;c.armLeft=-30;break;
                    case 9:c.upperArmLeft=-60;c.armLeft=-75;c.wristLeft=15;break;
                }
                until=EditorApplication.timeSinceStartup+1.8;
            }catch(Exception e){Finish("FAIL: "+e.Message);Debug.LogException(e);}
        }
        static void Finish(string status){
            EditorApplication.update-=Tick;
            Directory.CreateDirectory("Validation");File.WriteAllText("Validation/body-results.txt",status+" Unity "+Application.unityVersion+"\n"+string.Join("\n",results)+"\nLa prueba numérica no certifica el acabado artístico ni la ausencia de huecos en todos los ángulos.\n");
            if(c){Clear();c.demo=demo;c.tracking=tracking;c.autoBlink=blink;c.physics=physics;}c=null;Debug.Log("TRAIGGER_BODY_"+status);
        }
    }
}
