using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Traigger.Editor {
    public static class RepairScene {
        [Serializable] class BoneInput { public string name; public int parent; public float x,y; }
        [Serializable] class LayerInput { public string name; public string texturePath; public int[] bones; public float[] rect,uv; public int order,face; public float topCenterExtension,cheekSpread,falloff; public float[] bottomCenterCutout,sideCutout; }
        [Serializable] class InputData { public BoneInput[] bones; public LayerInput[] layers; }
        [MenuItem("Traigger/Reparar y guardar escena con avatar visible")]
        public static void Repair(){
            if(EditorApplication.isPlaying){Debug.LogError("Sal de Play antes de reparar");return;}
            const string folder="Assets/Traigger/Art/";
            var data=JsonUtility.FromJson<InputData>(File.ReadAllText(folder+"assembly.json"));
            var target=AssetDatabase.LoadAssetAtPath<RigDefinition>(folder+"TraiggerRig.asset");
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"TraiggerParts-clean.png");
            if(!target||!texture)throw new Exception("Faltan assets del personaje");
            target.layerShader=Shader.Find("Traigger/UnlitAlpha");
            target.bones=new BoneSpec[data.bones.Length];
            for(int i=0;i<data.bones.Length;i++){var b=data.bones[i];target.bones[i]=new BoneSpec(b.name,b.parent,b.x,b.y);}
            target.layers=new LayerSpec[data.layers.Length];
            for(int i=0;i<data.layers.Length;i++){var l=data.layers[i];target.layers[i]=new LayerSpec{name=l.name,texture=string.IsNullOrEmpty(l.texturePath)?texture:AssetDatabase.LoadAssetAtPath<Texture2D>(l.texturePath),rect=new Rect(l.rect[0],l.rect[1],l.rect[2],l.rect[3]),uvRect=new Rect(l.uv[0],l.uv[1],l.uv[2],l.uv[3]),bones=l.bones,order=l.order,face=(FaceDeform)l.face,columns=18,rows=22,falloff=l.falloff>0?l.falloff:2,topCenterExtension=l.topCenterExtension,cheekSpread=l.cheekSpread,bottomCenterCutout=l.bottomCenterCutout!=null?new Vector2(l.bottomCenterCutout[0],l.bottomCenterCutout[1]):Vector2.zero,sideCutout=l.sideCutout!=null&&l.sideCutout.Length==3?new Vector3(l.sideCutout[0],l.sideCutout[1],l.sideCutout[2]):Vector3.zero};}
            AutoRig.Validate(target);EditorUtility.SetDirty(target);AssetDatabase.SaveAssets();
            var bootstrap=UnityEngine.Object.FindFirstObjectByType<StudioBootstrap>();
            if(!bootstrap)throw new Exception("Abre primero TraiggerStudio");
            if(bootstrap.preparedRig)Undo.DestroyObjectImmediate(bootstrap.preparedRig.gameObject);
            if(bootstrap.preparedCamera)Undo.DestroyObjectImmediate(bootstrap.preparedCamera.gameObject);
            if(bootstrap.preparedControlCamera)Undo.DestroyObjectImmediate(bootstrap.preparedControlCamera.gameObject);
            var rig=AutoRig.Build(target,bootstrap.transform);rig.name="Traigger — rig editable";
            string path=AssetDatabase.GenerateUniqueAssetPath(folder+"TraiggerMeshData.asset");
            var container=ScriptableObject.CreateInstance<RigDefinition>();AssetDatabase.CreateAsset(container,path);
            if(rig.closedMouth)AssetDatabase.AddObjectToAsset(rig.closedMouth.sharedMaterial,container);
            foreach(var r in rig.layers){AssetDatabase.AddObjectToAsset(r.sharedMesh,container);AssetDatabase.AddObjectToAsset(r.sharedMaterial,container);}
            var camera=new GameObject("Avatar Camera").AddComponent<Camera>();camera.transform.SetParent(bootstrap.transform,false);
            camera.transform.position=new Vector3(0,2.45f,-10);camera.orthographic=true;camera.orthographicSize=2.8f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.08f,.12f,0);
            var controls=new GameObject("Control Camera").AddComponent<Camera>();controls.transform.SetParent(bootstrap.transform,false);controls.cullingMask=0;controls.depth=-1;controls.backgroundColor=camera.backgroundColor;
            // El cuerpo segmentado (BodyV12) sustituye al cuerpo del atlas antiguo; la cabeza no cambia.
            bool kemono=KemonoBodySetup.Available;if(kemono)KemonoBodySetup.Apply(rig);
            int shaded=ReapplyHeadShading(rig);
            bootstrap.definition=target;bootstrap.preparedRig=rig;bootstrap.preparedCamera=camera;bootstrap.preparedControlCamera=controls;bootstrap.enabled=true;
            EditorUtility.SetDirty(bootstrap);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);EditorSceneManager.SaveScene(bootstrap.gameObject.scene);
            Selection.activeGameObject=rig.gameObject;SceneView.lastActiveSceneView?.Frame(new Bounds(new Vector3(0,2.45f,0),new Vector3(4,5.4f,1)),false);
            Debug.Log("TRAIGGER_SCENE_REPAIRED: avatar, mallas y cámaras guardados"+(kemono?"; cuerpo kemono segmentado":"; cuerpo del atlas antiguo")+"; cabeza sombreada: "+shaded+" capas");
        }
        // «Traigger > Arte > Sombrear cabeza» guarda sus materiales (capa_celShaded) en Art/HeadShading. La reconstrucción
        // crea materiales nuevos, así que se vuelven a asignar los guardados más recientes: se conserva el estilo y
        // cualquier ajuste de intensidad hecho en ellos. Si no existen, la cabeza queda con el aspecto original.
        static int ReapplyHeadShading(AvatarRig rig){
            const string folder="Assets/Traigger/Art/HeadShading";
            if(!AssetDatabase.IsValidFolder(folder))return 0;
            string latest=null;DateTime newest=DateTime.MinValue;
            foreach(string guid in AssetDatabase.FindAssets("HeadShadingMaterials",new[]{folder})){
                string path=AssetDatabase.GUIDToAssetPath(guid);if(!path.EndsWith(".asset"))continue;
                var time=File.GetLastWriteTimeUtc(path);if(time>newest){newest=time;latest=path;}
            }
            if(latest==null)return 0;
            var materials=new System.Collections.Generic.Dictionary<string,Material>();
            foreach(var item in AssetDatabase.LoadAllAssetsAtPath(latest))if(item is Material material)materials[material.name]=material;
            int count=0;
            for(int i=0;i<rig.layers.Length;i++){
                if(!materials.TryGetValue(rig.definition.layers[i].name+"_celShaded",out var shaded))continue;
                rig.layers[i].sharedMaterial=shaded;EditorUtility.SetDirty(rig.layers[i]);count++;
            }
            return count;
        }
    }
}
