using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Traigger.Editor {
    public static class EarCheck {
        [MenuItem("Traigger/Pruebas/Comprobar flexión de orejas")]
        public static void Run(){
            if(EditorApplication.isPlaying)throw new Exception("Sal de Play para esta prueba");
            var rig=UnityEngine.Object.FindAnyObjectByType<AvatarRig>();if(!rig)throw new Exception("Sin avatar");
            // El arco debe ser continuo, conservar el reposo y poder desactivarse.
            foreach(float side in new[]{-1f,1f}){
                var rest=new Vector3(side*.376f,.7908f,0);
                if(Vector3.Distance(StudioController.EarPlacement(rest,0,1),rest)>1e-6f)throw new Exception("El arco altera el reposo");
                if(Vector3.Distance(StudioController.EarPlacement(rest,25,0),rest)>1e-6f)throw new Exception("Arco cero desplaza la base");
                Vector3 previous=StudioController.EarPlacement(rest,-25,1);
                for(int angle=-24;angle<=25;angle++){
                    Vector3 next=StudioController.EarPlacement(rest,angle,1);
                    if(Vector3.Distance(next,previous)>.02f||Vector3.Distance(next,rest)>.15f)throw new Exception("Arco discontinuo o excesivo");
                    previous=next;
                }
            }
            int checkedCount=0;float peak=0;
            foreach(var layer in rig.layers){
                if(layer.name!="EarLeft"&&layer.name!="EarRight")continue;
                var original=layer.sharedMesh;var vertices=original.vertices;
                var flex=new EarFlex(layer);
                try{
                    var mesh=layer.sharedMesh;
                    if(mesh==original||mesh.vertexCount!=original.vertexCount)throw new Exception("Malla original sustituida incorrectamente");
                    var changed=mesh.vertices;
                    for(int i=0;i<vertices.Length;i++)if(vertices[i]!=changed[i])throw new Exception("Cambio en reposo");
                    var delta=new Vector3[vertices.Length];mesh.GetBlendShapeFrameVertices(mesh.GetBlendShapeIndex("CartilagePositive"),0,delta,null,null);
                    float baseY=original.bounds.min.y+original.bounds.size.y*.12f;int fixedCount=0;
                    for(int i=0;i<delta.Length;i++){
                        if(float.IsNaN(delta[i].sqrMagnitude)||float.IsInfinity(delta[i].sqrMagnitude))throw new Exception("Deformación no finita");
                        if(vertices[i].y<=baseY){if(delta[i].sqrMagnitude>1e-10f)throw new Exception("Se mueve la base");fixedCount++;}
                        peak=Mathf.Max(peak,delta[i].magnitude);
                    }
                    if(fixedCount==0)throw new Exception("Sin vértices de anclaje");
                    checkedCount++;
                }finally{flex.Dispose();}
                if(layer.sharedMesh!=original)throw new Exception("No se restauró la malla original");
            }
            if(checkedCount!=2||peak<.01f)throw new Exception("Faltan orejas o flexión");
            Directory.CreateDirectory("Validation");
            File.WriteAllText("Validation/ears-results.txt",$"PASS Unity {Application.unityVersion}\nArco continuo, reposo conservado, recorrido acotado.\nDos orejas, reposo intacto, bases fijas en el correctivo de cartílago, deltas finitos.\nMallas originales restauradas. Recorrido máximo: {peak}\n");
            Debug.Log("TRAIGGER_EARS_OK");
        }
    }
}
