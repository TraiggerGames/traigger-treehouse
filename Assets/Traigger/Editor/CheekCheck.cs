using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace Traigger.Editor {
    public static class CheekCheck {
        [MenuItem("Traigger/Pruebas/Comprobar carrillos separados del pelo")]
        public static void Run(){
            if(EditorApplication.isPlaying){Debug.LogError("Sal de Play para comprobar los carrillos");return;}
            var rig=UnityEngine.Object.FindAnyObjectByType<AvatarRig>();if(!rig)throw new Exception("Sin avatar");
            int headIndex=Array.FindIndex(rig.definition.layers,l=>l.name=="HeadBase");
            var spec=rig.definition.layers[headIndex];var face=rig.layers[headIndex];
            if(spec.bones.Any(i=>rig.definition.bones[i].name!="Head"))throw new Exception("El carrillo tiene influencias ajenas a la cabeza");
            for(int i=0;i<face.sharedMesh.blendShapeCount;i++)if(face.sharedMesh.GetBlendShapeName(i).StartsWith("Hair"))throw new Exception("La cara tiene deformación de pelo");
            rig.CaptureRest();rig.ResetPose();rig.Look(0,0);rig.HairMotion(0,0,0,0);rig.Face(FaceDeform.Mouth,0);rig.Face(FaceDeform.BlinkLeft,0);rig.Face(FaceDeform.BlinkRight,0);
            var first=new Mesh();var after=new Mesh();face.BakeMesh(first);Directory.CreateDirectory("Validation");
            StudioSetup.Render("Validation/cheeks-neutral.png");
            float maximum=0;
            foreach(float direction in new[]{-1f,1f}){
                rig.HairMotion(direction,direction,direction*.65f,direction*.8f);face.BakeMesh(after);
                var a=first.vertices;var b=after.vertices;for(int i=0;i<a.Length;i++)maximum=Mathf.Max(maximum,Vector3.Distance(a[i],b[i]));
                StudioSetup.Render(direction<0?"Validation/cheeks-hair-left.png":"Validation/cheeks-hair-right.png");
            }
            rig.HairMotion(0,0,0,0);UnityEngine.Object.DestroyImmediate(first);UnityEngine.Object.DestroyImmediate(after);
            if(maximum>1e-5f)throw new Exception("El pelo arrastra la malla de la cara");
            File.WriteAllText("Validation/cheeks-results.txt",$"PASS Unity {Application.unityVersion}\nCarrillos: capa HeadBase, influencia exclusiva Head\nCorrectivos Hair en la cara: ninguno\nDesplazamiento de cara al mover sólo el pelo: {maximum}\n");
            Debug.Log("TRAIGGER_CHEEKS_OK");
        }
    }
}
