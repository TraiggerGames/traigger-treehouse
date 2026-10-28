using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Traigger.Editor {
    public static class KemonoBodyCheck {
        static Vector3[] Bake(SkinnedMeshRenderer r){var m=new Mesh();r.BakeMesh(m);var v=m.vertices;for(int i=0;i<v.Length;i++)v[i]=r.transform.TransformPoint(v[i]);UnityEngine.Object.DestroyImmediate(m);return v;}
        static float Difference(Vector3[] a,Vector3[] b){float result=0;for(int i=0;i<a.Length;i++)result=Mathf.Max(result,Vector3.Distance(a[i],b[i]));return result;}
        static Transform Bone(AvatarRig r,string name)=>Array.Find(r.bones,b=>b.name==name);
        [MenuItem("Traigger/Pruebas/Comprobar cuerpo kemono segmentado")]
        public static void Run(){
            if(EditorApplication.isPlaying){Debug.LogError("Sal de Play para esta comprobación de geometría");return;}
            var body=UnityEngine.Object.FindAnyObjectByType<KemonoBodyRig>();if(!body)throw new Exception("Abre el cuerpo nuevo");var rig=body.rig;
            rig.CaptureRest();Directory.CreateDirectory("Validation");var messages=new List<string>();
            try{
                int expected=16+(System.Array.Exists(body.parts,x=>x&&x.name=="Kemono_NeckFill")?1:0);if(body.parts.Length!=expected)throw new Exception("Se esperaban "+expected+" piezas independientes");
                foreach(var r in body.parts)foreach(var w in r.sharedMesh.boneWeights){
                    float sum=w.weight0+w.weight1+w.weight2+w.weight3;if(Mathf.Abs(sum-1)>.0001f)throw new Exception("Pesos no normalizados: "+r.name);
                }
                // La camiseta se deforma con cuello, pecho, columna, cadera y hombros, pero nunca con los brazos.
                bool ArmBone(int i)=>i>=0&&i<rig.bones.Length&&(rig.bones[i].name.Contains("UpperArm")||rig.bones[i].name.Contains("LowerArm")||rig.bones[i].name.Contains("Hand")||rig.bones[i].name.Contains("Blend")||rig.bones[i].name.Contains("SleeveSway"));
                foreach(var w in body.torsoRenderer.sharedMesh.boneWeights)
                    if((w.weight0>0&&ArmBone(w.boneIndex0))||(w.weight1>0&&ArmBone(w.boneIndex1))||(w.weight2>0&&ArmBone(w.boneIndex2))||(w.weight3>0&&ArmBone(w.boneIndex3)))throw new Exception("La camiseta aún depende de un brazo");
                messages.Add(body.parts.Length+" piezas; pesos normalizados. Torso independiente de los brazos.");
                var head=Array.Find(rig.layers,r=>r.name=="HeadBase");var originalHead=Bake(head);var originalTorso=Bake(body.torsoRenderer);
                var shoulders=new[]{Bone(rig,"RightUpperArm").position,Bone(rig,"LeftUpperArm").position};
                rig.Look(0,0);rig.HairMotion(0,0,0,0);rig.Face(FaceDeform.Mouth,0);rig.Face(FaceDeform.BlinkLeft,0);rig.Face(FaceDeform.BlinkRight,0);
                StudioSetup.Render("Validation/kemono-v12-neutral.png");
                float torsoMovement=0,headMovement=0;float minimumRatio=1;
                // En el alcance extremo (brazo vertical y codo a ~120°) se acepta el pliegue del interior del codo:
                // sólo triángulos del brazo y antebrazo a menos de 0,15 del codo, y nunca por debajo de 0,03.
                float elbowCrease=1;const float creaseRadius=.15f,creaseMinimum=.03f;
                foreach(var pose in new[]{"arms","wave","wrists","legs","reach"}){
                    rig.ResetPose();
                    if(pose=="arms"){rig.Rotate("RightUpperArm",-50);rig.Rotate("LeftUpperArm",50);}
                    if(pose=="wave"){rig.Rotate("RightUpperArm",-55);rig.Rotate("RightLowerArm",-70);rig.Rotate("RightHand",15);}
                    if(pose=="wrists"){rig.Rotate("RightHand",-50);rig.Rotate("LeftHand",50);}
                    if(pose=="legs"){rig.Rotate("RightUpperLeg",-16);rig.Rotate("RightLowerLeg",30);rig.Rotate("RightFoot",-12);}
                    if(pose=="reach"){
                        var a=Bone(rig,"RightUpperArm");var b=Bone(rig,"RightLowerArm");var c=Bone(rig,"RightHand");
                        float upper=Vector3.Distance(a.position,b.position),lower=Vector3.Distance(b.position,c.position);
                        Vector3 target=rig.transform.TransformPoint(new Vector3(-.85f,3.35f,0));TwoBoneIK.Solve(a,b,c,target,-1);
                        if(Mathf.Abs(Vector3.Distance(a.position,b.position)-upper)>1e-5f||Mathf.Abs(Vector3.Distance(b.position,c.position)-lower)>1e-5f)throw new Exception("IK estira el brazo");
                        float error=Vector3.Distance(c.position,target);if(error>.005f)throw new Exception("IK no alcanza la mano: "+error);
                        messages.Add("IK de mano: longitudes conservadas; error "+error.ToString("F6"));
                    }
                    body.ApplyShoulderFollow();body.ApplyConstraints();body.SyncHelpers();
                    Vector3 elbow=Bone(rig,"RightLowerArm").position;
                    if(pose!="legs"){
                        // El hombro acompaña al brazo y deforma la camiseta sólo alrededor de los hombros.
                        var bakedTorso=Bake(body.torsoRenderer);
                        for(int i=0;i<bakedTorso.Length;i++){
                            if(Vector3.Distance(originalTorso[i],shoulders[0])<.5f||Vector3.Distance(originalTorso[i],shoulders[1])<.5f)continue;
                            torsoMovement=Mathf.Max(torsoMovement,Vector3.Distance(originalTorso[i],bakedTorso[i]));
                        }
                        headMovement=Mathf.Max(headMovement,Difference(originalHead,Bake(head)));
                    }
                    foreach(var renderer in body.parts){
                        var vertices=Bake(renderer);var rest=renderer.sharedMesh.vertices;var tri=renderer.sharedMesh.triangles;
                        for(int i=0;i<vertices.Length;i++)if(float.IsNaN(vertices[i].sqrMagnitude)||float.IsInfinity(vertices[i].sqrMagnitude))throw new Exception("Malla no finita");
                        bool arm=renderer.name=="Kemono_UpperArmLeft"||renderer.name=="Kemono_ForearmLeft";
                        for(int i=0;i<tri.Length;i+=3){int a=tri[i],b=tri[i+1],c=tri[i+2];float original=Vector3.Cross(rest[b]-rest[a],rest[c]-rest[a]).magnitude;if(original<=1e-8f)continue;
                            float ratio=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).magnitude/original;
                            if(pose=="reach"&&arm&&Vector3.Distance((vertices[a]+vertices[b]+vertices[c])/3,elbow)<creaseRadius)elbowCrease=Mathf.Min(elbowCrease,ratio);
                            else minimumRatio=Mathf.Min(minimumRatio,ratio);}
                    }
                    StudioSetup.Render("Validation/kemono-v12-"+pose+".png");
                }
                if(torsoMovement>1e-5f||headMovement>1e-5f)throw new Exception("Brazos arrastran torso/cabeza fuera de los hombros");
                if(minimumRatio<.10f)throw new Exception("Colapso de triángulos en una pose: "+minimumRatio);
                if(elbowCrease<creaseMinimum)throw new Exception("El pliegue del codo en el alcance colapsa: "+elbowCrease);
                // Constraints: un codo forzado más allá de su tope debe quedarse en el límite.
                rig.ResetPose();rig.Rotate("RightLowerArm",170);body.ApplyConstraints();
                float limited=Mathf.DeltaAngle(0,Bone(rig,"RightLowerArm").localEulerAngles.z);
                int limitIndex=Array.IndexOf(body.limitBones,Bone(rig,"RightLowerArm"));
                if(limitIndex<0||Mathf.Abs(limited-body.limitMax[limitIndex])>.01f)throw new Exception("El codo no respeta su límite: "+limited);
                // Con el forro separado, el exterior de la manga cubre siempre la unión del hombro.
                if(body.armLayers.Length>0){
                    var seams=body.GetComponent<SleeveSeams>();
                    if(seams){foreach(float angle in new[]{0f,30f,-90f}){
                        rig.ResetPose();rig.Rotate("RightUpperArm",angle);body.UpdateLayering();seams.Sync();
                        for(int i=0;i<seams.fronts.Length;i++)if(seams.fronts[i].sortingOrder<=body.torsoRenderer.sortingOrder||seams.fronts[i].sortingOrder<=seams.arms[i].sortingOrder||seams.linings[i].sortingOrder>=seams.arms[i].sortingOrder)throw new Exception("Orden de manga, torso y brazo incorrecto");
                    }messages.Add("Mangas: exterior sobre la unión del hombro, piel dentro y forro detrás");}
                }
                messages.Add("Constraints: "+body.limitBones.Length+" articulaciones con límite; codo forzado a 170° queda en "+limited.ToString("F1")+"°");
                messages.Add("Al mover brazos: camiseta fuera de los hombros "+torsoMovement+"; cabeza "+headMovement+" (los hombros sí se deforman)");
                messages.Add("Área mínima relativa de triángulos en poses: "+minimumRatio.ToString("F3"));
                messages.Add("Alcance extremo: pliegue del interior del codo aceptado, área mínima "+elbowCrease.ToString("F3")+" (mínimo exigido "+creaseMinimum+")");
                File.WriteAllText("Validation/kemono-v12-results.txt","PASS Unity "+Application.unityVersion+"\n"+string.Join("\n",messages)+"\nPoses acotadas verificadas; no certifica todo el recorrido extremo ni un giro 3D.\n");Debug.Log("TRAIGGER_KEMONO_V12_PASS");
            }catch(Exception e){File.WriteAllText("Validation/kemono-v12-results.txt","FAIL: "+e.Message+"\n"+string.Join("\n",messages));throw;}
            finally{rig.ResetPose();rig.Look(0,0);rig.HairMotion(0,0,0,0);}
        }
    }
}
