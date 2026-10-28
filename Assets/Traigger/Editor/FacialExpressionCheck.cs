using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Traigger.Editor {
    public static class FacialExpressionCheck {
        [MenuItem("Traigger/Pruebas/Comprobar muecas kemono")]
        public static void Check(){
            if(EditorApplication.isPlaying)throw new Exception("Sal de Play para capturar las pruebas");
            var avatar=UnityEngine.Object.FindAnyObjectByType<AvatarRig>();if(!avatar)throw new Exception("Abre la escena de Traigger");
            // Recupera sólo las referencias a mallas si una prueba anterior fue interrumpida.
            if(Array.Exists(avatar.layers,r=>r.sharedMesh.name.EndsWith(" · facial runtime"))){
                var preview=UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene(avatar.gameObject.scene.path);
                try{AvatarRig saved=null;foreach(var root in preview.GetRootGameObjects()){saved=root.GetComponentInChildren<AvatarRig>();if(saved)break;}
                    if(!saved)throw new Exception("No se encuentra el rig guardado");
                    if(saved.closedMouth&&avatar.closedMouth){var points=new Vector3[saved.closedMouth.positionCount];saved.closedMouth.GetPositions(points);avatar.closedMouth.SetPositions(points);}
                    foreach(var r in avatar.layers)if(r.sharedMesh.name.EndsWith(" · facial runtime")){var source=Array.Find(saved.layers,x=>x.name==r.name);if(source)r.sharedMesh=source.sharedMesh;}
                }finally{UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);}
            }
            var camera=Array.Find(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None),c=>c.name=="Avatar Camera");
            var position=camera.transform.position;float size=camera.orthographicSize;
            var materials=Array.ConvertAll(avatar.layers,r=>r.sharedMaterial);
            var meshes=Array.ConvertAll(avatar.layers,r=>r.sharedMesh);var report=new System.Text.StringBuilder();
            var visible=Array.ConvertAll(avatar.layers,r=>r.enabled);
            var weights=Array.ConvertAll(avatar.layers,r=>{var a=new float[r.sharedMesh.blendShapeCount];for(int i=0;i<a.Length;i++)a[i]=r.GetBlendShapeWeight(i);return a;});
            bool closedVisible=avatar.closedMouth&&avatar.closedMouth.enabled;Vector3 mouthScale=avatar.closedMouth?avatar.closedMouth.transform.localScale:Vector3.one;
            var expressions=avatar.gameObject.AddComponent<FacialExpressionRig>();expressions.rig=avatar;
            Directory.CreateDirectory("Validation/FaceExpressions");
            try {
                expressions.Initialize();int shapes=0;
                foreach(var renderer in avatar.layers){var mesh=renderer.sharedMesh;for(int i=0;i<mesh.blendShapeCount;i++)if(mesh.GetBlendShapeName(i).StartsWith("Expression_"))shapes++;}
                if(shapes<15)throw new Exception("Faltan correctivos faciales");
                camera.transform.position=new Vector3(avatar.transform.position.x,avatar.transform.position.y+3.8f,position.z);camera.orthographicSize=1.05f;
                for(int pose=0;pose<8;pose++){
                    expressions.Preset(pose);
                    avatar.Face(FaceDeform.Mouth,expressions.manualFrame.jawOpen);
                    avatar.MouthShape((expressions.manualFrame.mouthSmileLeft+expressions.manualFrame.mouthSmileRight)*.9f-expressions.manualFrame.mouthPucker*.9f);
                    avatar.Face(FaceDeform.BlinkLeft,expressions.manualFrame.eyeBlinkLeft);avatar.Face(FaceDeform.BlinkRight,expressions.manualFrame.eyeBlinkRight);
                    for(int frame=0;frame<50;frame++)expressions.Step(null,1f/30);
                    if(pose==7){var tongue=avatar.GetComponentInChildren<SpriteRenderer>();if(!tongue||!tongue.enabled||tongue.sprite.texture.name!="TraiggerTongue-v2")throw new Exception("Lengua no visible");}
                    foreach(var renderer in avatar.layers){var baked=new Mesh();renderer.BakeMesh(baked);foreach(var v in baked.vertices)if(float.IsNaN(v.x)||float.IsInfinity(v.x)||float.IsNaN(v.y)||float.IsInfinity(v.y))throw new Exception("Deformación no finita");UnityEngine.Object.DestroyImmediate(baked);}
                    StudioSetup.Render("Validation/FaceExpressions/pose-"+pose+".png");
                }
                var mouthRenderer=Array.Find(avatar.layers,r=>r.sharedMaterial.HasProperty("_TongueOut")&&r.sharedMaterial.GetFloat("_TongueOut")>.9f);
                if(!mouthRenderer)throw new Exception("No se oculta la lengua pintada");
                foreach(float amount in new[]{.5f,0f}){
                    expressions.manualFrame.tongueOut=amount;for(int frame=0;frame<50;frame++)expressions.Step(null,1f/30);
                    StudioSetup.Render("Validation/FaceExpressions/tongue-"+(amount==0?"retracted":"half")+".png");
                    if(amount==0&&mouthRenderer.sharedMaterial.GetFloat("_TongueOut")>.001f)throw new Exception("No restaura el interior original al recoger la lengua");
                }
                var source=avatar.gameObject.AddComponent<ARKitFaceSource>();try{
                    var data=new float[40];data[39]=.9f;data[19]=.7f;data[28]=.8f;var f=source.Convert(data);
                    if(f.browInnerUp!=.7f||f.cheekPuff!=.8f||f.tongueOut!=.9f)throw new Exception("Orden de canales nativos incorrecto");
                    if(source.Convert(new float[19]).cheekPuff!=0)throw new Exception("Emisor antiguo incompatible");
                    if(FaceReceiver.Finite(new FaceFrame{browInnerUp=float.NaN}))throw new Exception("NaN aceptado");
                }finally{UnityEngine.Object.DestroyImmediate(source);}
                report.AppendLine("PASS Unity "+Application.unityVersion+": "+shapes+" correctivos; 8 poses finitas, lengua visible, interior sin segunda lengua y recogida comprobados; canales nuevos y búfer antiguo comprobados.");
            }finally{
                expressions.Release();UnityEngine.Object.DestroyImmediate(expressions);camera.transform.position=position;camera.orthographicSize=size;
                for(int i=0;i<meshes.Length;i++){if(avatar.layers[i].sharedMesh!=meshes[i])throw new Exception("No restaura las mallas originales");if(avatar.layers[i].sharedMaterial!=materials[i])throw new Exception("No restaura el material original");avatar.layers[i].enabled=visible[i];for(int j=0;j<weights[i].Length;j++)avatar.layers[i].SetBlendShapeWeight(j,weights[i][j]);}
                if(avatar.closedMouth){avatar.closedMouth.enabled=closedVisible;avatar.closedMouth.transform.localScale=mouthScale;}
            }
            report.AppendLine("No sustituye prueba física de TrueDepth ni revisión artística de todas las combinaciones.");
            File.WriteAllText("Validation/FaceExpressions/results.txt",report.ToString());Debug.Log("TRAIGGER_FACE_EXPRESSIONS_PASS");
        }
    }
}
