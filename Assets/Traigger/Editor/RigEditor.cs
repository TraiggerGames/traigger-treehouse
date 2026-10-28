using UnityEngine;
using UnityEditor;
namespace Traigger.Editor {
    public class RigEditor : EditorWindow {
        RigDefinition definition; GameObject preview; int selectedBone; bool showMesh=true; Vector2 scroll;
        [MenuItem("Traigger/AutoRig · Ajustar huesos y capas")]
        static void Open(){GetWindow<RigEditor>("Traigger AutoRig");}
        void OnEnable(){SceneView.duringSceneGui+=SceneGUI;}
        void OnDisable(){SceneView.duringSceneGui-=SceneGUI;}
        void OnGUI(){
            scroll=EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("Asigna capas PNG transparentes en Rig Definition. Ajusta sus rectángulos y huesos permitidos. Los pesos son una estimación inicial; comprueba codos, hombros y zonas ocultas.",MessageType.Info);
            definition=(RigDefinition)EditorGUILayout.ObjectField("Definición",definition,typeof(RigDefinition),false);
            if(!definition){EditorGUILayout.EndScrollView();return;}
            if(GUILayout.Button("Editar capas y resolución en Inspector"))Selection.activeObject=definition;
            if(GUILayout.Button("Reconstruir avatar (usa pose de reposo)")){
                try{AutoRig.Validate(definition);
                    var fresh=AutoRig.Build(definition);
                    if(preview)Undo.DestroyObjectImmediate(preview);
                    preview=fresh.gameObject;Undo.RegisterCreatedObjectUndo(preview,"Crear rig");Selection.activeGameObject=preview;
                }catch(System.Exception e){EditorUtility.DisplayDialog("AutoRig",e.Message,"Cerrar");}
            }
            showMesh=EditorGUILayout.Toggle("Ver triangulación",showMesh);
            selectedBone=EditorGUILayout.IntSlider("Hueso resaltado",selectedBone,0,Mathf.Max(0,definition.bones.Length-1));
            if(definition.bones.Length>0)EditorGUILayout.LabelField(definition.bones[selectedBone].name);
            EditorGUILayout.HelpBox("Arrastra las esferas en Scene para cambiar posiciones de reposo. Reconstruye después. En la figura seleccionada puedes doblar huesos con la herramienta de rotación. Los pesos se editan por cadena y falloff en cada capa; no incluye pincel de pesos.",MessageType.None);
            if(GUILayout.Button("Guardar avatar seleccionado como prefab con mallas"))SavePrefab();
            EditorGUILayout.EndScrollView();
        }
        void SavePrefab(){
            var rig=Selection.activeGameObject?Selection.activeGameObject.GetComponent<AvatarRig>():null;
            if(!rig)return;
            string path=EditorUtility.SaveFilePanelInProject("Guardar avatar","Avatar","prefab","Elige ruta");if(string.IsNullOrEmpty(path))return;
            string meshPath=AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.ChangeExtension(path,null)+"_Meshes.asset");
            var container=ScriptableObject.CreateInstance<RigDefinition>();EditorUtility.CopySerialized(rig.definition,container);AssetDatabase.CreateAsset(container,meshPath);rig.definition=container;
            foreach(var r in rig.layers){var mesh=Instantiate(r.sharedMesh);var mat=Instantiate(r.sharedMaterial);AssetDatabase.AddObjectToAsset(mesh,container);AssetDatabase.AddObjectToAsset(mat,container);r.sharedMesh=mesh;r.sharedMaterial=mat;}
            if(rig.closedMouth){var mouthMaterial=Instantiate(rig.closedMouth.sharedMaterial);AssetDatabase.AddObjectToAsset(mouthMaterial,container);rig.closedMouth.sharedMaterial=mouthMaterial;}
            PrefabUtility.SaveAsPrefabAsset(rig.gameObject,path);AssetDatabase.SaveAssets();
        }
        void SceneGUI(SceneView view){
            if(!definition)return;
            for(int i=0;i<definition.bones.Length;i++){
                var b=definition.bones[i];Handles.color=i==selectedBone?Color.yellow:Color.cyan;
                if(b.parent>=0)Handles.DrawLine(b.position,definition.bones[b.parent].position);
                Handles.Label(b.position,b.name);EditorGUI.BeginChangeCheck();
                var point=Handles.FreeMoveHandle(b.position,.035f,Vector3.zero,Handles.SphereHandleCap);
                if(EditorGUI.EndChangeCheck()){Undo.RecordObject(definition,"Mover hueso");b.position=point;EditorUtility.SetDirty(definition);}
            }
            if(showMesh&&preview){Handles.color=new Color(0,1,0,.25f);foreach(var r in preview.GetComponentsInChildren<SkinnedMeshRenderer>()){
                var m=new Mesh();r.BakeMesh(m);var v=m.vertices;var t=m.triangles;
                for(int i=0;i<t.Length;i+=3){Handles.DrawLine(r.transform.TransformPoint(v[t[i]]),r.transform.TransformPoint(v[t[i+1]]));Handles.DrawLine(r.transform.TransformPoint(v[t[i+1]]),r.transform.TransformPoint(v[t[i+2]]));}
                DestroyImmediate(m);
            }}
        }
    }
}
