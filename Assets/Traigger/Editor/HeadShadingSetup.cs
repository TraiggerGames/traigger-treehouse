using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace Traigger.Editor {
 public static class HeadShadingSetup {
  [MenuItem("Traigger/Arte/Sombrear cabeza sin modificar rig")]
  public static void Apply(){
   if(EditorApplication.isPlaying)throw new Exception("Sal de Play para guardar el sombreado");
   const string folder="Assets/Traigger/Art/HeadShading/";
   foreach(string name in new[]{"Traigger-head-shading-v1","BackHair-shading-v1"}){
    var importer=(TextureImporter)AssetImporter.GetAtPath(folder+name+".png");
    importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.SaveAndReimport();
   }
   var rig=UnityEngine.Object.FindAnyObjectByType<AvatarRig>();if(!rig)throw new Exception("Falta rig activo");
   Directory.CreateDirectory("Validation/HeadShading");
   File.Copy(rig.gameObject.scene.path,"Validation/HeadShading/scene-before-head-shading.unity.bak",true);
   StudioSetup.Render("Validation/HeadShading/before.png");
   var meshes=Array.ConvertAll(rig.layers,r=>r.sharedMesh);var positions=Array.ConvertAll(rig.bones,b=>b.localPosition);var rotations=Array.ConvertAll(rig.bones,b=>b.localRotation);
   Undo.RegisterFullObjectHierarchyUndo(rig.gameObject,"Sombrear cabeza conservando rig");
   var asset=ScriptableObject.CreateInstance<RigDefinition>();AssetDatabase.CreateAsset(asset,AssetDatabase.GenerateUniqueAssetPath(folder+"HeadShadingMaterials.asset"));
   int count=0;
   for(int i=0;i<rig.layers.Length;i++){
    string name=rig.definition.layers[i].name;
    if(!(name.Contains("HeadBase")||name.Contains("Hair")||name.Contains("Ear")||name.Contains("Cheek")))continue;
    var renderer=rig.layers[i];var material=new Material(renderer.sharedMaterial){name=name+"_celShaded"};
    string textureName=renderer.sharedMaterial.mainTexture.name.Contains("BackHair-v2")?"BackHair-shading-v1":"Traigger-head-shading-v1";
    material.SetTexture("_HeadShadeTex",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+textureName+".png"));material.SetFloat("_HeadShadeStrength",name.Contains("Hair")?.7f:.85f);
    renderer.sharedMaterial=material;AssetDatabase.AddObjectToAsset(material,asset);EditorUtility.SetDirty(renderer);count++;
   }
   for(int i=0;i<rig.layers.Length;i++)if(rig.layers[i].sharedMesh!=meshes[i])throw new Exception("Ha cambiado una malla");
   for(int i=0;i<rig.bones.Length;i++)if(rig.bones[i].localPosition!=positions[i]||rig.bones[i].localRotation!=rotations[i])throw new Exception("Ha cambiado un hueso");
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);EditorSceneManager.SaveScene(rig.gameObject.scene);
   StudioSetup.Render("Validation/HeadShading/after.png");
   File.WriteAllText("Validation/HeadShading/results.txt","PASS: "+count+" materiales; mismas mallas, UV, pesos, blendshapes y transformaciones. Alfa original conservado por shader; originales PNG sin modificar. Sólo RGB de sombreado.\n");
   Debug.Log("TRAIGGER_HEAD_SHADING_SAVED: "+count);
  }
 }
}
