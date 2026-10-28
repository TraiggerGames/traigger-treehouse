using System;using System.IO;using UnityEditor;using UnityEngine;
namespace Traigger.Editor {
 // Sólo los nuevos PNG de mechones: sprites editables, sin compresión ni pérdida de resolución.
 public class HairStrandImporter:AssetPostprocessor {
  void OnPreprocessTexture(){
   if(assetPath=="Assets/Traigger/Resources/Hair/FrontHairOwnership.png"){
    var map=(TextureImporter)assetImporter;map.textureType=TextureImporterType.Default;map.isReadable=true;map.sRGBTexture=false;map.npotScale=TextureImporterNPOTScale.None;
    map.maxTextureSize=4096;map.mipmapEnabled=false;map.textureCompression=TextureImporterCompression.Uncompressed;map.alphaIsTransparency=false;map.filterMode=FilterMode.Point;map.wrapMode=TextureWrapMode.Clamp;
    foreach(string platform in new[]{"iPhone","Standalone","VisionOS"})map.ClearPlatformTextureSettings(platform);return;
   }
   if(!assetPath.StartsWith("Assets/Traigger/Resources/Hair/Strands/",StringComparison.Ordinal)||!assetPath.EndsWith(".png",StringComparison.OrdinalIgnoreCase))return;
   var importer=(TextureImporter)assetImporter;importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
   importer.spritePixelsPerUnit=2508/.0045f/1254;importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=4096;
   importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.alphaIsTransparency=true;
   importer.filterMode=FilterMode.Bilinear;importer.wrapMode=TextureWrapMode.Clamp;importer.isReadable=false;
   foreach(string platform in new[]{"iPhone","Standalone","VisionOS"})importer.ClearPlatformTextureSettings(platform);
   var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);settings.spriteMeshType=SpriteMeshType.FullRect;settings.spriteExtrude=0;importer.SetTextureSettings(settings);
  }
 }
}
