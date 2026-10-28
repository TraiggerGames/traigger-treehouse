using UnityEditor;
using UnityEngine;
namespace Traigger.Editor {
    // Mantener resolución y alfa del nuevo sprite; no altera importadores de los atlas del usuario.
    public class TongueImport:AssetPostprocessor {
        void OnPreprocessTexture(){
            if(!assetPath.Contains("/Resources/TraiggerTongue-v")||!assetPath.EndsWith(".png"))return;
            var importer=(TextureImporter)assetImporter;
            importer.textureType=TextureImporterType.Default;importer.npotScale=TextureImporterNPOTScale.None;
            importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.filterMode=FilterMode.Bilinear;
        }
    }
}
