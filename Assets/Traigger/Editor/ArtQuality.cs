using System.IO;
using UnityEditor;
using UnityEngine;
namespace Traigger.Editor {
    public static class ArtQuality {
        [MenuItem("Traigger/Arte/Conservar resolución original del atlas")]
        public static void Apply(){
            const string path="Assets/Traigger/Art/TraiggerParts-clean.png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            if(!importer)throw new System.Exception("No se encuentra el atlas original");
            // Cambia sólo cómo Unity lee el PNG; no toca ningún píxel, color ni contorno del archivo.
            importer.npotScale=TextureImporterNPOTScale.None;
            importer.maxTextureSize=4096;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.crunchedCompression=false;importer.filterMode=FilterMode.Bilinear;
            importer.mipmapEnabled=false;importer.alphaIsTransparency=true;
            // Evitar que una configuración de macOS reduzca o comprima el atlas por separado.
            var platform=importer.GetPlatformTextureSettings("Standalone");platform.overridden=false;importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);importer.GetSourceTextureWidthAndHeight(out int width,out int height);
            if(texture.width!=width||texture.height!=height)throw new System.Exception("Unity ha reducido el atlas");
            Directory.CreateDirectory("Validation");
            File.WriteAllText("Validation/art-quality.txt",$"PASS Unity {Application.unityVersion}\nPNG original: {width} x {height}; textura importada: {texture.width} x {texture.height}.\nSin compresión, sin reducción NPOT, filtro bilineal. No se ha aumentado artificialmente la resolución ni modificado el PNG.\n");
            Debug.Log("TRAIGGER_ART_QUALITY_OK");
        }
    }
}
