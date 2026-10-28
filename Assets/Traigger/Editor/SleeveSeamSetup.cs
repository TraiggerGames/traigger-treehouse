using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Traigger.Editor {
    public static class SleeveSeamSetup {
        [MenuItem("Traigger/Mangas/Corregir tubo opaco y borde trasero")]
        public static void CorrectTube(){
            if(EditorApplication.isPlaying)throw new Exception("Sal de Play para guardar la corrección");
            var seams=UnityEngine.Object.FindAnyObjectByType<SleeveSeams>();if(!seams)throw new Exception("Faltan mangas separadas");
            Directory.CreateDirectory("Validation/SleeveTube");
            if(!File.Exists("Validation/SleeveTube/scene-before-tube.unity.bak"))File.Copy(seams.gameObject.scene.path,"Validation/SleeveTube/scene-before-tube.unity.bak");
            StudioSetup.Render("Validation/SleeveTube/before.png");
            Undo.RegisterFullObjectHierarchyUndo(seams.gameObject,"Corregir tubo de mangas");
            var asset=ScriptableObject.CreateInstance<RigDefinition>();AssetDatabase.CreateAsset(asset,AssetDatabase.GenerateUniqueAssetPath("Assets/Traigger/Art/BodyV12/SleeveTube.asset"));
            seams.PrepareOpaqueSkin();foreach(var arm in seams.arms)AssetDatabase.AddObjectToAsset(arm.sharedMesh,asset);
            var renderers=new System.Collections.Generic.List<SkinnedMeshRenderer>();renderers.AddRange(seams.fronts);renderers.AddRange(seams.linings);renderers.AddRange(seams.arms);
            foreach(var renderer in renderers){renderer.sharedMaterial=new Material(renderer.sharedMaterial){name=renderer.name+"_tubeOpaque"};AssetDatabase.AddObjectToAsset(renderer.sharedMaterial,asset);EditorUtility.SetDirty(renderer);}
            seams.tubeCorrected=true;seams.joinsIntegrated=false;seams.opaqueSleeves=true;seams.Sync();EditorUtility.SetDirty(seams);
            Check();StudioSetup.Render("Validation/SleeveTube/after.png");
        }
        [MenuItem("Traigger/Mangas/Separar bajo trasero de camiseta")]
        public static void SplitHem(){
            if(EditorApplication.isPlaying)throw new Exception("Sal de Play para separar el bajo");
            var body=UnityEngine.Object.FindAnyObjectByType<KemonoBodyRig>();if(!body)throw new Exception("Falta el cuerpo kemono");
            Directory.CreateDirectory("Validation/Sleeves");
            File.Copy(body.gameObject.scene.path,"Validation/Sleeves/scene-before-rear-hem.unity.bak",true);
            Undo.RegisterFullObjectHierarchyUndo(body.gameObject,"Separar bajo trasero");
            var split=body.GetComponent<ShirtHemSplit>();if(!split)split=ShirtHemSplit.Install(body);
            var asset=ScriptableObject.CreateInstance<RigDefinition>();AssetDatabase.CreateAsset(asset,AssetDatabase.GenerateUniqueAssetPath("Assets/Traigger/Art/BodyV12/ShirtHem.asset"));
            if(!AssetDatabase.Contains(split.front.sharedMaterial))AssetDatabase.AddObjectToAsset(split.front.sharedMaterial,asset);
            if(!AssetDatabase.Contains(split.rear.sharedMaterial))AssetDatabase.AddObjectToAsset(split.rear.sharedMaterial,asset);
            EditorUtility.SetDirty(split);EditorUtility.SetDirty(split.front);EditorUtility.SetDirty(split.rear);
            Check();
        }
        [MenuItem("Traigger/Mangas/Integrar bordes internos de tela y piel")]
        public static void IntegrateJoins(){
            if(EditorApplication.isPlaying)throw new Exception("Sal de Play para integrar las uniones");
            var seams=UnityEngine.Object.FindAnyObjectByType<SleeveSeams>();if(!seams)throw new Exception("Faltan las mangas separadas");
            Directory.CreateDirectory("Validation/Sleeves");
            File.Copy(seams.gameObject.scene.path,"Validation/Sleeves/scene-before-soft-joins.unity.bak",true);
            Undo.RegisterFullObjectHierarchyUndo(seams.gameObject,"Integrar bordes internos");
            var asset=ScriptableObject.CreateInstance<RigDefinition>();AssetDatabase.CreateAsset(asset,AssetDatabase.GenerateUniqueAssetPath("Assets/Traigger/Art/BodyV12/SoftJoins.asset"));
            seams.RestoreSkinOverlap();
            foreach(var arm in seams.arms)AssetDatabase.AddObjectToAsset(arm.sharedMesh,asset);
            foreach(var r in seams.body.parts)if(r.name.Contains("Forearm")||r.name.Contains("UpperArm")||r.name.Contains("Sleeve")){
                r.sharedMaterial=new Material(r.sharedMaterial){name=r.sharedMaterial.name+"_softJoin"};AssetDatabase.AddObjectToAsset(r.sharedMaterial,asset);EditorUtility.SetDirty(r);
            }
            seams.joinsIntegrated=true;seams.Sync();EditorUtility.SetDirty(seams);Check();
        }
        [MenuItem("Traigger/Mangas/Alinear hombros con la camiseta de referencia")]
        public static void AlignShoulders(){
            if(EditorApplication.isPlaying)throw new Exception("Sal de Play para ajustar los hombros");
            var seams=UnityEngine.Object.FindAnyObjectByType<SleeveSeams>();if(!seams)throw new Exception("Falta separación de mangas");
            if(seams.shoulderSources.Length==0&&seams.shouldersAligned){
                string path="Assets/Traigger/Art/BodyV12/SleeveSeams.asset";
                var originals=AssetDatabase.LoadAllAssetsAtPath(path);
                seams.shoulderSources=Array.ConvertAll(seams.fronts,f=>Array.Find(originals,a=>a is Mesh&&a.name==f.sharedMesh.name.Replace("_shoulderFit","")) as Mesh);
                if(Array.Exists(seams.shoulderSources,m=>!m))throw new Exception("No se encuentra la copia original de las mangas");
            }
            Undo.RegisterFullObjectHierarchyUndo(seams.gameObject,"Alinear hombros y mangas");
            File.Copy(seams.gameObject.scene.path,"Validation/Sleeves/scene-before-shoulder-fit.unity.bak",true);
            seams.AlignShoulderCaps();
            var asset=ScriptableObject.CreateInstance<RigDefinition>();AssetDatabase.CreateAsset(asset,AssetDatabase.GenerateUniqueAssetPath("Assets/Traigger/Art/BodyV12/ShoulderFit.asset"));
            foreach(var front in seams.fronts){AssetDatabase.AddObjectToAsset(front.sharedMesh,asset);EditorUtility.SetDirty(front);}
            foreach(var lining in seams.linings)EditorUtility.SetDirty(lining);
            EditorUtility.SetDirty(seams);Check();
        }
        static void Pose(AvatarRig rig,KemonoBodyRig body,float raise,float bend=0){
            rig.ResetPose();rig.Rotate("RightUpperArm",-raise);rig.Rotate("LeftUpperArm",raise);rig.Rotate("RightLowerArm",-bend);rig.Rotate("LeftLowerArm",bend);
            body.ApplyShoulderFollow();body.ApplyConstraints();body.SyncHelpers();body.UpdateLayering();var seams=body.GetComponent<SleeveSeams>();if(seams)seams.Sync();
        }
        [MenuItem("Traigger/Mangas/Separar forro y corregir sisa sin reconstruir rig")]
        public static void Apply(){
            if(EditorApplication.isPlaying){Debug.LogError("Sal de Play para guardar la corrección de mangas");return;}
            var body=UnityEngine.Object.FindAnyObjectByType<KemonoBodyRig>();if(!body)throw new Exception("Abre el cuerpo kemono que has editado");
            if(body.GetComponent<SleeveSeams>())throw new Exception("Las mangas ya están separadas; usa la prueba de mangas");
            var rig=body.rig;rig.CaptureRest();Directory.CreateDirectory("Validation/Sleeves");
            // Copia del archivo guardado; Undo conserva además las referencias actuales de la escena abierta.
            if(File.Exists(rig.gameObject.scene.path))File.Copy(rig.gameObject.scene.path,"Validation/Sleeves/scene-before-sleeves.unity.bak",true);
            foreach(float angle in new[]{0f,90f,150f}){Pose(rig,body,angle,25);StudioSetup.Render("Validation/Sleeves/before-"+angle+".png");}
            rig.ResetPose();body.SyncHelpers();body.UpdateLayering();
            Undo.RegisterFullObjectHierarchyUndo(rig.gameObject,"Separar forro de mangas y corregir sisa");
            var seams=SleeveSeams.Install(body);
            string path=AssetDatabase.GenerateUniqueAssetPath("Assets/Traigger/Art/BodyV12/SleeveSeams.asset");
            var asset=ScriptableObject.CreateInstance<RigDefinition>();AssetDatabase.CreateAsset(asset,path);
            for(int i=0;i<seams.fronts.Length;i++){
                AssetDatabase.AddObjectToAsset(seams.fronts[i].sharedMesh,asset);AssetDatabase.AddObjectToAsset(seams.arms[i].sharedMesh,asset);
                AssetDatabase.AddObjectToAsset(seams.fronts[i].sharedMaterial,asset);AssetDatabase.AddObjectToAsset(seams.linings[i].sharedMaterial,asset);
                EditorUtility.SetDirty(seams.fronts[i]);EditorUtility.SetDirty(seams.arms[i]);EditorUtility.SetDirty(seams.linings[i]);
            }
            EditorUtility.SetDirty(seams);EditorUtility.SetDirty(body);EditorUtility.SetDirty(rig);AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);EditorSceneManager.SaveScene(rig.gameObject.scene);
            Check();Debug.Log("TRAIGGER_SLEEVE_SEAMS_SAVED: "+rig.gameObject.scene.path);
        }
        static Vector3[] Bake(SkinnedMeshRenderer r){var m=new Mesh();r.BakeMesh(m);var result=m.vertices;UnityEngine.Object.DestroyImmediate(m);return result;}
        [MenuItem("Traigger/Pruebas/Comprobar mangas al levantar brazos")]
        public static void Check(){
            if(EditorApplication.isPlaying){Debug.LogError("Sal de Play para comprobar las mangas");return;}
            var seams=UnityEngine.Object.FindAnyObjectByType<SleeveSeams>();if(!seams)throw new Exception("Aplica primero la corrección de mangas");var body=seams.body;var rig=body.rig;
            rig.CaptureRest();Directory.CreateDirectory("Validation/Sleeves");Directory.CreateDirectory("Validation/SleeveTube");float worstStretch=0,worstArea=1,rootArea=1;var report=new System.Text.StringBuilder();
            var positions=Array.ConvertAll(rig.bones,b=>b.localPosition);var restRotation=Array.ConvertAll(rig.bones,b=>b.localRotation);
            try{
                foreach(float angle in new[]{0f,30f,60f,90f,120f,150f,165f})foreach(float bend in new[]{0f,70f}){
                    Pose(rig,body,angle,bend);
                    for(int side=0;side<2;side++){
                        var front=seams.fronts[side];var back=seams.linings[side];var arm=seams.arms[side];
                        if(front.sharedMesh!=back.sharedMesh)throw new Exception("Forro y frente no comparten geometría");
                        if(back.sortingOrder>=arm.sortingOrder||front.sortingOrder<=arm.sortingOrder)throw new Exception("Orden incorrecto: forro < brazo < frente");
                                                if(front.sharedMaterial.GetFloat("_SleeveSplit")!=-1||back.sharedMaterial.GetFloat("_SleeveSplit")!=1)throw new Exception("Máscaras de manga incompletas");
                        if(seams.tubeCorrected){
                            if(front.sharedMaterial.GetFloat("_SleeveTube")!=1||back.sharedMaterial.GetFloat("_SleeveTube")!=1)throw new Exception("Falta recorte continuo del tubo");
                            if(front.sharedMaterial.GetFloat("_JoinStrength")!=0||front.sharedMaterial.GetFloat("_VertexAlpha")!=0||back.sharedMaterial.GetFloat("_VertexAlpha")!=0)throw new Exception("La manga todavía tiene fundidos de opacidad");
                            if(arm.sharedMaterial.GetFloat("_SkinOpaque")!=1)throw new Exception("La piel visible sigue sin opacidad forzada");
                        }
                        var source=front.sharedMesh.vertices;var posed=Bake(front);var triangles=front.sharedMesh.triangles;
                        int upper=Array.FindIndex(front.bones,b=>b.name==(side==0?"RightUpperArm":"LeftUpperArm"));Vector3 pivot=front.sharedMesh.bindposes[upper].inverse.MultiplyPoint3x4(Vector3.zero);
                        foreach(var w in front.sharedMesh.boneWeights)if(Mathf.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1)>.0001f)throw new Exception("Pesos sin normalizar");
                        for(int i=0;i<triangles.Length;i+=3){
                            int a=triangles[i],b=triangles[i+1],c=triangles[i+2];float rest=Vector3.Cross(source[b]-source[a],source[c]-source[a]).magnitude;if(rest<1e-8f)continue;
                            float ratio=Vector3.Cross(posed[b]-posed[a],posed[c]-posed[a]).magnitude/rest;
                            if(float.IsNaN(ratio)||float.IsInfinity(ratio))throw new Exception("Deformación no finita");
                            var weights=front.sharedMesh.boneWeights;
                            bool Rigid(int v)=>weights[v].boneIndex0==upper&&weights[v].weight0>.99999f;
                            bool bulk=Rigid(a)&&Rigid(b)&&Rigid(c);
                            if(bulk){worstStretch=Mathf.Max(worstStretch,Mathf.Abs(ratio-1));worstArea=Mathf.Min(worstArea,ratio);}else rootArea=Mathf.Min(rootArea,ratio);
                        }
                    }
                    var hem=body.GetComponent<ShirtHemSplit>();if(hem){hem.Sync();if(hem.front.sharedMesh!=hem.rear.sharedMesh)throw new Exception("El bajo no comparte la malla del torso");foreach(var part in body.parts)if(part&&(part.name.Contains("Pelvis")||part.name.Contains("Thigh"))&&hem.rear.sortingOrder>=part.sortingOrder)throw new Exception("El bajo trasero debe ir detrás de los pantalones");}
                    if(bend==0||angle==90)StudioSetup.Render("Validation/Sleeves/after-"+angle+"-elbow-"+bend+".png");
                    if(seams.tubeCorrected&&((angle==0&&bend==0)||(angle==90&&bend==70)||(angle==165&&bend==0))){
                        var camera=Array.Find(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None),c=>c.name=="Avatar Camera");
                        var oldPosition=camera.transform.position;float oldSize=camera.orthographicSize;
                        try{for(int side=0;side<2;side++){
                            string label=side==0?"Right":"Left";
                            var upper=Array.Find(rig.bones,b=>b.name==label+"UpperArm");var lower=Array.Find(rig.bones,b=>b.name==label+"LowerArm");
                            var center=Vector3.Lerp(upper.position,lower.position,.65f);camera.transform.position=new Vector3(center.x,center.y,oldPosition.z);camera.orthographicSize=.48f;
                            StudioSetup.Render("Validation/SleeveTube/"+label+"-"+angle+"-"+bend+".png");
                        }}finally{camera.transform.position=oldPosition;camera.orthographicSize=oldSize;}
                    }
                    if(angle==0&&hem){
                        var camera=Array.Find(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None),c=>c.name=="Avatar Camera");
                        var oldPosition=camera.transform.position;float oldSize=camera.orthographicSize;
                        try{camera.transform.position=new Vector3(0,1.95f,oldPosition.z);camera.orthographicSize=.75f;StudioSetup.Render("Validation/Sleeves/hem-close-elbow-"+bend+".png");}
                        finally{camera.transform.position=oldPosition;camera.orthographicSize=oldSize;}
                    }
                    if((angle==0||angle==90||angle==165)&&bend==0){
                        var camera=Array.Find(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None),c=>c.name=="Avatar Camera");
                        var oldPosition=camera.transform.position;float oldSize=camera.orthographicSize;
                        try{camera.transform.position=new Vector3(0,body.torsoRenderer.bounds.max.y-.25f,oldPosition.z);camera.orthographicSize=.85f;
                            StudioSetup.Render("Validation/Sleeves/close-"+angle+".png");}
                        finally{camera.transform.position=oldPosition;camera.orthographicSize=oldSize;}
                    }
                }
                if(worstStretch>.025f)throw new Exception("La tela fuera de la sisa sigue estirándose: "+worstStretch);
                report.AppendLine("PASS Unity "+Application.unityVersion).AppendLine("2 mangas divididas: forro oscuro detrás de piel, frente y borde delante").AppendLine("14 poses: elevación 0–165°, codo 0°/70°; pesos normalizados y mallas finitas").AppendLine("Cambio máximo de área del panel fuera de la sisa: "+worstStretch.ToString("F6")).AppendLine("Área mínima del panel: "+worstArea.ToString("F6")+"; sisa: "+rootArea.ToString("F6"));
                report.AppendLine("La zona cubierta de la sisa puede plegarse; el resultado visual se revisa en las capturas. No es un giro 3D.");
                File.WriteAllText("Validation/Sleeves/results.txt",report.ToString());Debug.Log("TRAIGGER_SLEEVES_PASS");
            }catch(Exception e){File.WriteAllText("Validation/Sleeves/results.txt","FAIL: "+e.Message+"\n"+report);throw;}
            finally{
                for(int i=0;i<rig.bones.Length;i++){rig.bones[i].localPosition=positions[i];rig.bones[i].localRotation=restRotation[i];}
                body.SyncHelpers();body.UpdateLayering();seams.Sync();
                if(!EditorApplication.isPlaying){AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);EditorSceneManager.SaveScene(rig.gameObject.scene);}
            }
        }
    }
}
