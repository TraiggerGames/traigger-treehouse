using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
namespace Traigger.Editor {
    public static class StudioSetup {
        const string Folder="Assets/Traigger/Generated";
        [MenuItem("Traigger/1 · Crear escena de demostración")]
        public static void CreateDemo(){
            if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            Directory.CreateDirectory(Folder);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var def=DemoDefinition(); AssetDatabase.CreateAsset(def,AssetDatabase.GenerateUniqueAssetPath(Folder+"/DemoRig.asset"));
            var rig=AutoRig.Build(def);
            if(rig.closedMouth)AssetDatabase.AddObjectToAsset(rig.closedMouth.sharedMaterial,def);
            foreach(var r in rig.layers){AssetDatabase.AddObjectToAsset(r.sharedMesh,def);AssetDatabase.AddObjectToAsset(r.sharedMaterial,def);}
            var cam=new GameObject("Avatar Camera").AddComponent<Camera>();
            cam.transform.position=new Vector3(0,2.3f,-10);cam.orthographic=true;cam.orthographicSize=2.8f;
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.07f,.08f,.12f,0);
            var studio=new GameObject("Studio Control");var controller=studio.AddComponent<StudioController>();
            controller.rig=rig;controller.avatarCamera=cam;controller.receiver=studio.AddComponent<FaceReceiver>();
            // Cámara secundaria sólo para mantener activa la superficie de control en Display 1.
            var controlCam=new GameObject("Control Camera").AddComponent<Camera>();
            controlCam.cullingMask=0;controlCam.depth=-1;controlCam.backgroundColor=new Color(.07f,.08f,.12f);
            PlayerSettings.productName="Traigger VTuber Studio";PlayerSettings.companyName="Traigger";
            PlayerSettings.runInBackground=true;PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=900;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),Folder+"/Studio.unity");
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(Folder+"/Studio.unity",true)};
            AssetDatabase.SaveAssets();Selection.activeGameObject=rig.gameObject;
        }
        static RigDefinition DemoDefinition(){
            var d=ScriptableObject.CreateInstance<RigDefinition>();d.name="Demo técnica — NO es Traigger";
            d.bones=new[]{
                new BoneSpec("Hip",-1,0,1.95f),new BoneSpec("Chest",0,0,2.65f),new BoneSpec("Head",1,0,3.6f),
                new BoneSpec("EarLeft",2,-.43f,4.04f),new BoneSpec("EarRight",2,.43f,4.04f),new BoneSpec("Hair",2,0,4.12f),
                new BoneSpec("UpperArmLeft",1,-.52f,2.92f),new BoneSpec("ForearmLeft",6,-.83f,2.27f),new BoneSpec("HandLeft",7,-.94f,1.65f),
                new BoneSpec("UpperArmRight",1,.52f,2.92f),new BoneSpec("ForearmRight",9,.83f,2.27f),new BoneSpec("HandRight",10,.94f,1.65f),
                new BoneSpec("ThighLeft",0,-.26f,1.95f),new BoneSpec("ShinLeft",12,-.3f,1.05f),new BoneSpec("FootLeft",13,-.34f,.22f),
                new BoneSpec("ThighRight",0,.26f,1.95f),new BoneSpec("ShinRight",15,.3f,1.05f),new BoneSpec("FootRight",16,.34f,.22f),
                new BoneSpec("Tail1",0,.35f,1.95f),new BoneSpec("Tail2",18,1.03f,1.5f),new BoneSpec("Tail3",19,1.5f,1.85f),
                new BoneSpec("EyeLeft",2,-.23f,3.66f),new BoneSpec("EyeRight",2,.23f,3.66f)};
            var ellipse=Texture("Ellipse",false);var ear=Texture("Ear",true);
            Color fur=new Color(.7f,.76f,.82f),dark=new Color(.12f,.16f,.24f),shirt=new Color(.13f,.48f,.62f);
            LayerSpec L(string n,Rect r,int[] b,int order,Color color,FaceDeform f=FaceDeform.None,Texture2D tex=null)=>new LayerSpec{name=n,rect=r,bones=b,order=order,tint=color,texture=tex?tex:ellipse,face=f};
            d.layers=new[]{
                L("Cola",new Rect(.25f,1.25f,1.65f,.75f),new[]{18,19,20},0,fur),
                L("Pierna izquierda",new Rect(-.56f,.12f,.48f,1.9f),new[]{12,13,14},1,dark),
                L("Pierna derecha",new Rect(.08f,.12f,.48f,1.9f),new[]{15,16,17},1,dark),
                L("Brazo izquierdo",new Rect(-1.1f,1.5f,.58f,1.5f),new[]{6,7,8},2,fur),
                L("Brazo derecho",new Rect(.52f,1.5f,.58f,1.5f),new[]{9,10,11},2,fur),
                L("Camiseta torso",new Rect(-.63f,1.8f,1.26f,1.36f),new[]{0,1},3,shirt),
                L("Oreja izquierda",new Rect(-.72f,3.9f,.48f,.75f),new[]{2,3},4,fur,FaceDeform.None,ear),
                L("Oreja derecha",new Rect(.24f,3.9f,.48f,.75f),new[]{2,4},4,fur,FaceDeform.None,ear),
                L("Cabeza",new Rect(-.65f,3.1f,1.3f,1.1f),new[]{1,2},5,fur),
                L("Hocico",new Rect(-.42f,3.23f,.84f,.44f),new[]{2},6,Color.white),
                L("Pelo",new Rect(-.48f,3.92f,.96f,.35f),new[]{2,5},7,dark),
                L("Ojo izquierdo",new Rect(-.36f,3.58f,.23f,.22f),new[]{21},8,dark,FaceDeform.BlinkLeft),
                L("Ojo derecho",new Rect(.13f,3.58f,.23f,.22f),new[]{22},8,dark,FaceDeform.BlinkRight),
                L("Boca",new Rect(-.17f,3.29f,.34f,.065f),new[]{2},9,new Color(.28f,.06f,.12f),FaceDeform.Mouth)};
            return d;
        }
        static Texture2D Texture(string name,bool triangle){
            var t=new Texture2D(128,128,TextureFormat.RGBA32,false);t.name=name;
            var pixels=new Color[128*128];
            for(int y=0;y<128;y++)for(int x=0;x<128;x++){
                float u=(x+.5f)/128f,v=(y+.5f)/128f;
                float edge=triangle?(1-v)*.49f-Mathf.Abs(u-.5f):1-(new Vector2((u-.5f)*2,(v-.5f)*2)).magnitude;
                pixels[y*128+x]=new Color(1,1,1,Mathf.Clamp01(edge*64));
            }
            t.SetPixels(pixels);t.Apply();string path=Folder+"/"+name+".png";
            File.WriteAllBytes(path,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);AssetDatabase.ImportAsset(path);
            var imp=(TextureImporter)AssetImporter.GetAtPath(path);imp.alphaIsTransparency=true;imp.mipmapEnabled=false;imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        [MenuItem("Traigger/2 · Comprobar deformación y guardar prueba")]
        public static void Validate(){
            var rig=UnityEngine.Object.FindFirstObjectByType<AvatarRig>();if(!rig){
                var bootstrap=UnityEngine.Object.FindFirstObjectByType<StudioBootstrap>();
                if(bootstrap&&bootstrap.definition)rig=AutoRig.Build(bootstrap.definition);
                else throw new Exception("Abre la escena de Traigger o crea la demo");
            }
            AutoRig.Validate(rig.definition);rig.CaptureRest();rig.ResetPose();
            foreach(var r in rig.layers){
                foreach(var w in r.sharedMesh.boneWeights){float sum=w.weight0+w.weight1+w.weight2+w.weight3;if(Mathf.Abs(sum-1)>.0001f)throw new Exception("Pesos no normalizados");}
            }
            var arm=rig.layers.First(r=>System.Array.Exists(rig.definition.layers[System.Array.IndexOf(rig.layers,r)].bones,b=>rig.definition.bones[b].name=="ForearmLeft"));
            var before=new Mesh();arm.BakeMesh(before);rig.Rotate("ForearmLeft",35);var after=new Mesh();arm.BakeMesh(after);
            float max=0;int moved=0;for(int i=0;i<before.vertexCount;i++){float dist=Vector3.Distance(before.vertices[i],after.vertices[i]);max=Mathf.Max(max,dist);if(dist>.001f)moved++;}
            if(max<.02f||moved<10)throw new Exception("No se detectó deformación");
            // Un movimiento rígido mantiene todas las distancias entre vértices; skinning suave no.
            int a=0,b=before.vertexCount-1;
            float lengthChange=Mathf.Abs(Vector3.Distance(before.vertices[a],before.vertices[b])-Vector3.Distance(after.vertices[a],after.vertices[b]));
            if(lengthChange<.001f)throw new Exception("La malla parece rígida");
            var face=rig.layers.First(r=>rig.definition.layers[System.Array.IndexOf(rig.layers,r)].face==FaceDeform.Mouth);var neutral=new Mesh();var open=new Mesh();face.BakeMesh(neutral);rig.Face(FaceDeform.Mouth,1);face.BakeMesh(open);
            if((neutral.vertices[0]-open.vertices[0]).sqrMagnitude<.00001f)throw new Exception("Blendshape no aplicado");
            Directory.CreateDirectory("Validation");
            File.WriteAllText("Validation/results.txt",$"Unity {Application.unityVersion}\nHuesos: {rig.bones.Length}\nCapas: {rig.layers.Length}\nPesos normalizados: OK\nVértices desplazados brazo: {moved}\nDesplazamiento máximo: {max}\nCambio distancia extremos: {lengthChange}\nBlendshape boca: OK\nReferencias de Traigger: ver Art/References\nPrueba artística: PENDIENTE\n");
            rig.ResetPose();rig.Face(FaceDeform.Mouth,0);Render("Validation/demo-neutral.png");
            rig.Rotate("ForearmLeft",35);rig.Rotate("Head",12);rig.Face(FaceDeform.Mouth,.8f);rig.Face(FaceDeform.BlinkLeft,1);Render("Validation/demo-deformed.png");
            rig.ResetPose();rig.Face(FaceDeform.Mouth,0);rig.Face(FaceDeform.BlinkLeft,0);
            foreach(var m in new[]{before,after,neutral,open})UnityEngine.Object.DestroyImmediate(m);
            Debug.Log("TRAIGGER_VALIDATION_OK");
        }
        public static void Render(string path){
            var camera=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c=>c.name=="Avatar Camera");
            // En edición Unity puede reutilizar la pose GPU del último fotograma. Hornear copias
            // temporales evita entregar capturas de reposo como si fueran poses distintas.
            var source=UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            var copies=new System.Collections.Generic.List<GameObject>();var baked=new System.Collections.Generic.List<Mesh>();
            var rt=new RenderTexture(800,1000,24){antiAliasing=4};var previous=RenderTexture.active;var previousTarget=camera.targetTexture;Texture2D tex=null;
            try{
                foreach(var renderer in source){
                    var mesh=new Mesh();renderer.BakeMesh(mesh);baked.Add(mesh);
                    var go=new GameObject("Capture pose — "+renderer.name){hideFlags=HideFlags.HideAndDontSave};go.layer=renderer.gameObject.layer;go.transform.SetParent(renderer.transform,false);copies.Add(go);
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;var staticRenderer=go.AddComponent<MeshRenderer>();staticRenderer.sharedMaterials=renderer.sharedMaterials;staticRenderer.sortingLayerID=renderer.sortingLayerID;staticRenderer.sortingOrder=renderer.sortingOrder;renderer.enabled=false;
                }
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;tex=new Texture2D(800,1000,TextureFormat.RGBA32,false);
                tex.ReadPixels(new Rect(0,0,800,1000),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());
            }finally{
                foreach(var renderer in source)if(renderer)renderer.enabled=true;
                foreach(var go in copies)UnityEngine.Object.DestroyImmediate(go);foreach(var mesh in baked)UnityEngine.Object.DestroyImmediate(mesh);
                RenderTexture.active=previous;camera.targetTexture=previousTarget;rt.Release();UnityEngine.Object.DestroyImmediate(rt);if(tex)UnityEngine.Object.DestroyImmediate(tex);
            }
        }
        public static void BatchSetupAndValidate(){CreateDemo();Validate();}
        [MenuItem("Traigger/3 · Compilar macOS")]
        public static void BuildMac(){
            if(EditorApplication.isPlaying)throw new Exception("Sal de Play antes de compilar");
            // La misma escena que iPhone/iPad (el Traigger de verdad), no la escena de prueba de la configuración.
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            EditorSceneManager.OpenScene(IOSBuild.Scene);
            RepairScene.Repair();
            const string app="Builds/Traigger VTuber Studio.app";
            var result=BuildPipeline.BuildPlayer(new[]{IOSBuild.Scene},app,BuildTarget.StandaloneOSX,BuildOptions.None);
            if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Compilación macOS fallida");
            // El menú del Mac es Traigger Control (NativeUI/Control): se mete dentro de la app para que Esc lo abra.
            const string control="NativeUI/Control/build/Build/Products/Release/Traigger Control.app";
            if(Directory.Exists(control)){
                string helpers=Path.Combine(app,"Contents/Helpers");Directory.CreateDirectory(helpers);
                string target=Path.Combine(helpers,"Traigger Control.app");if(Directory.Exists(target))Directory.Delete(target,true);
                FileUtil.CopyFileOrDirectory(control,target);
            }else Debug.LogWarning("Traigger: compila NativeUI/Control para tener el menú dentro de la app de Mac");
            // iCloud (perfil de voz, escenas y carteles): la app se firma con tu equipo y un perfil con esos permisos.
            if(CloudSyncSetting.Enabled){
                string Q(string v)=>"\""+v.Replace("\"","\\\"")+"\"";
                var start=new System.Diagnostics.ProcessStartInfo("/bin/sh",string.Join(" ",new[]{"MacCamera/sign_player.sh",Path.GetFullPath(app),
                    PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone),CloudSyncSetting.KeyValueStore,CloudSyncSetting.Container}.Select(Q)))
                    {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
                using var process=System.Diagnostics.Process.Start(start);
                string output=process.StandardOutput.ReadToEnd()+process.StandardError.ReadToEnd();process.WaitForExit();
                if(process.ExitCode!=0)throw new Exception("No se pudo firmar la app de Mac con iCloud:\n"+output);
                Debug.Log("Traigger: app de Mac firmada con iCloud");
            }
        }
    }
}
