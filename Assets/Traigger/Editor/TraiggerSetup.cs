using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Traigger.Editor {
    public static class TraiggerSetup {
        [InitializeOnLoadMethod]
        static void ConfigureFirstImport(){
            const string marker="ProjectSettings/TraiggerInitialized.txt";
            if(System.IO.File.Exists(marker))return;
            EditorApplication.delayCall+=()=>{
                PlayerSettings.productName="Traigger VTuber Studio";
                PlayerSettings.companyName="Traigger";
                PlayerSettings.runInBackground=true;
                PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=900;
                PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
                System.IO.File.WriteAllText(marker,"Configuración inicial aplicada.");
            };
        }
        public static void BatchOpenAndValidate(){
            EditorSceneManager.OpenScene("Assets/Traigger/Scenes/TraiggerStudio.unity");
            StudioSetup.Validate();
        }
        [MenuItem("Traigger/Abrir escena de Traigger")]
        public static void OpenScene(){
            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene("Assets/Traigger/Scenes/TraiggerStudio.unity");
        }
        [MenuItem("Traigger/Crear rig de Traigger para editar")]
        public static void CreateRig(){
            var definition=AssetDatabase.LoadAssetAtPath<RigDefinition>("Assets/Traigger/Art/TraiggerRig.asset");
            if(!definition){Debug.LogError("No se encuentra TraiggerRig.asset");return;}
            var rig=AutoRig.Build(definition);Undo.RegisterCreatedObjectUndo(rig.gameObject,"Crear Traigger");
            Selection.activeGameObject=rig.gameObject;
            // No se guarda sobre la escena de inicio: ésta crea su propio rig al ejecutar.
            Debug.Log("Rig generado para edición. Guarda un prefab desde AutoRig o usa una escena separada para evitar duplicados en Play.");
        }
    }
}
