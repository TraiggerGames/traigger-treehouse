using UnityEditor;
namespace Traigger.Editor {
    // Skins 3D (Resources/Skins3D): el modelo se importa con su piel pero sin animaciones ni materiales (Traigger mueve los huesos
    // con el seguimiento y pone su propio material, Skin3D.shader). Texturas a 2048 en iPhone/iPad/Vision Pro.
    public class Skin3DImport : AssetPostprocessor {
        public override uint GetVersion()=>4; // subirlo vuelve a importar los modelos con los ajustes nuevos
        bool Mine=>assetPath.Replace('\\','/').Contains("/Resources/Skins3D/");
        void OnPreprocessModel(){
            if(!Mine)return;var model=(ModelImporter)assetImporter;
            model.animationType=ModelImporterAnimationType.Generic;model.importAnimation=false;model.optimizeGameObjects=false; // Generic conserva la piel (con None la malla queda estática)
            model.materialImportMode=ModelImporterMaterialImportMode.None;model.importBlendShapes=true;model.importBlendShapeNormals=ModelImporterNormals.None;
            model.importCameras=false;model.importLights=false;model.isReadable=true; // para corregir pesos (cuello de la camiseta) al cargar
            model.optimizeMeshPolygons=true;model.optimizeMeshVertices=true;model.meshCompression=ModelImporterMeshCompression.Medium;
        }
        void OnPreprocessTexture(){
            if(!Mine)return;var texture=(TextureImporter)assetImporter;
            bool normal=assetPath.ToLowerInvariant().Contains("normal"),mask=assetPath.ToLowerInvariant().Contains("mask");
            texture.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            texture.sRGBTexture=!normal&&!mask;texture.mipmapEnabled=true;texture.maxTextureSize=mask?1024:4096;
            foreach(string platform in new[]{"iPhone","VisionOS"}){
                var settings=texture.GetPlatformTextureSettings(platform);settings.overridden=true;settings.maxTextureSize=mask?1024:2048;
                settings.format=TextureImporterFormat.ASTC_6x6;texture.SetPlatformTextureSettings(settings);
            }
        }
    }
}
