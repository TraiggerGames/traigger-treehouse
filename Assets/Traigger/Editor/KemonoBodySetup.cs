using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Traigger.Editor {
    public static class KemonoBodySetup {
        public const string artFolder="Assets/Traigger/Art/BodyV12/";
        public static bool Available=>File.Exists(artFolder+"layout-v12.json");
        // Monta el cuerpo segmentado sobre un rig y guarda sus mallas. Lo usan estos menús y
        // «Reparar y guardar escena», para que cualquier reconstrucción dé el cuerpo nuevo.
        public static KemonoBodyRig Apply(AvatarRig rig){
            var layout=JsonUtility.FromJson<KemonoBodyRig.Layout>(File.ReadAllText(artFolder+"layout-v12.json"));
            var imported=new System.Collections.Generic.HashSet<string>();
            foreach(var part in layout.parts){
                if(!imported.Add(part.texture))continue;
                var importer=(TextureImporter)AssetImporter.GetAtPath(artFolder+part.texture+".png");
                if(importer.npotScale==TextureImporterNPOTScale.None&&importer.maxTextureSize>=4096&&importer.textureCompression==TextureImporterCompression.Uncompressed&&!importer.mipmapEnabled&&importer.alphaIsTransparency)continue;
                importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.crunchedCompression=false;importer.filterMode=FilterMode.Bilinear;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.SaveAndReimport();
            }
            var body=KemonoBodyRig.Build(rig,layout,name=>AssetDatabase.LoadAssetAtPath<Texture2D>(artFolder+name+".png"));
            // La reconstrucción conserva también la separación del interior de las mangas.
            var previousSeams=rig.GetComponent<SleeveSeams>();
            if(previousSeams)Undo.DestroyObjectImmediate(previousSeams);
            var seams=SleeveSeams.Install(body);
            var previousHem=rig.GetComponent<ShirtHemSplit>();if(previousHem)Undo.DestroyObjectImmediate(previousHem);
            var hem=ShirtHemSplit.Install(body);
            var container=ScriptableObject.CreateInstance<RigDefinition>();AssetDatabase.CreateAsset(container,AssetDatabase.GenerateUniqueAssetPath(artFolder+"KemonoBodyMeshes-v12.asset"));
            foreach(var renderer in body.parts){AssetDatabase.AddObjectToAsset(renderer.sharedMesh,container);AssetDatabase.AddObjectToAsset(renderer.sharedMaterial,container);EditorUtility.SetDirty(renderer);}
            foreach(var lining in seams.linings){AssetDatabase.AddObjectToAsset(lining.sharedMaterial,container);EditorUtility.SetDirty(lining);}
            AssetDatabase.AddObjectToAsset(hem.rear.sharedMaterial,container);EditorUtility.SetDirty(hem.rear);EditorUtility.SetDirty(hem);
            EditorUtility.SetDirty(seams);
            EditorUtility.SetDirty(body);EditorUtility.SetDirty(rig);AssetDatabase.SaveAssets();
            return body;
        }
        [MenuItem("Traigger/Cuerpo kemono/Crear escena con cuerpo segmentado")]
        public static void Create(){
            if(EditorApplication.isPlaying){Debug.LogError("Sal de Play antes de preparar la escena de cuerpo nuevo");return;}
            string source="Assets/Traigger/Scenes/TraiggerStudio.unity";
            string destination=AssetDatabase.GenerateUniqueAssetPath("Assets/Traigger/Scenes/TraiggerStudio-KemonoBody.unity");
            if(!AssetDatabase.CopyAsset(source,destination))throw new System.Exception("No se pudo copiar la escena original");
            EditorSceneManager.OpenScene(destination);
            var rig=UnityEngine.Object.FindAnyObjectByType<AvatarRig>();if(!rig)throw new System.Exception("No hay rig en la escena");
            Undo.RegisterFullObjectHierarchyUndo(rig.gameObject,"Crear cuerpo kemono segmentado");
            Apply(rig);EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);EditorSceneManager.SaveScene(rig.gameObject.scene);
            StudioSetup.Render("Validation/kemono-v12-neutral-editor.png");
            Debug.Log("TRAIGGER_KEMONO_BODY_CREATED: "+destination);
        }
        [MenuItem("Traigger/Cuerpo kemono/Reconstruir sólo el cuerpo de prueba")]
        public static void Rebuild(){
            if(EditorApplication.isPlaying){Debug.LogError("Sal de Play antes de reconstruir");return;}
            var old=UnityEngine.Object.FindAnyObjectByType<KemonoBodyRig>();
            var rig=old?old.rig:UnityEngine.Object.FindAnyObjectByType<AvatarRig>();if(!rig)throw new System.Exception("Sin rig en la escena");
            Undo.RegisterFullObjectHierarchyUndo(rig.gameObject,"Ajustar cuerpo de prueba");
            if(old){foreach(var renderer in old.parts)if(renderer)Undo.DestroyObjectImmediate(renderer.gameObject);Undo.DestroyObjectImmediate(old);}
            var body=Apply(rig);EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);EditorSceneManager.SaveScene(rig.gameObject.scene);
            StudioSetup.Render("Validation/kemono-v12-neutral-editor.png");
            Debug.Log("TRAIGGER_KEMONO_REBUILT_SAVED: "+rig.gameObject.scene.path+"; piezas="+body.parts.Length);
        }
        [MenuItem("Traigger/Cuerpo kemono/Guardar cuerpo probado como escena principal")]
        public static void MakeMain(){
            if(EditorApplication.isPlaying){Debug.LogError("Sal de Play antes de guardar la escena principal");return;}
            var body=UnityEngine.Object.FindAnyObjectByType<KemonoBodyRig>();if(!body)throw new System.Exception("Abre la escena del cuerpo nuevo");
            string original="Assets/Traigger/Scenes/TraiggerStudio.unity";
            string backup=AssetDatabase.GenerateUniqueAssetPath("Assets/Traigger/Scenes/TraiggerStudio-antes-cuerpo-v12.unity");
            AssetDatabase.CopyAsset(original,backup);
            EditorSceneManager.SaveScene(body.gameObject.scene,original);Debug.Log("TRAIGGER_KEMONO_MAIN_SAVED; copia anterior: "+backup);
        }
    }
}
