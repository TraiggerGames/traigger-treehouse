using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Traigger.Editor {
    public static class BodyWeights {
        [MenuItem("Traigger/Rig/Aplicar pesos del cuerpo sin reconstruir huesos")]
        public static void Apply(){
            if(EditorApplication.isPlaying)throw new System.Exception("Sal de Play antes de guardar pesos");
            var rig=UnityEngine.Object.FindAnyObjectByType<AvatarRig>();if(!rig)throw new System.Exception("Sin rig abierto");
            Undo.RegisterFullObjectHierarchyUndo(rig.gameObject,"Mejorar pesos de cuerpo");
            // Nuevos assets: los anteriores siguen disponibles para recuperar la versión previa.
            var upgraded=new BodyRigUpgrade(rig);
            var container=ScriptableObject.CreateInstance<RigDefinition>();
            string path=AssetDatabase.GenerateUniqueAssetPath("Assets/Traigger/Art/TraiggerBodyWeights.asset");AssetDatabase.CreateAsset(container,path);
            foreach(var r in rig.layers){AssetDatabase.AddObjectToAsset(r.sharedMesh,container);AssetDatabase.AddObjectToAsset(r.sharedMaterial,container);EditorUtility.SetDirty(r);}
            EditorUtility.SetDirty(rig);AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);EditorSceneManager.SaveScene(rig.gameObject.scene);
            Debug.Log("TRAIGGER_BODY_WEIGHTS_SAVED: huesos, jerarquía, texturas y posiciones conservados; nuevos pesos guardados en "+path);
        }
    }
}
