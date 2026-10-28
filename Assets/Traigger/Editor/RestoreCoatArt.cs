using System;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
namespace Traigger.Editor {
 public static class RestoreCoatArt {
  const string Archive="Rollback/Traigger-originales-pelo-cabeza-cola-2026-10-25.zip";
  // Restaurar sólo bloques Material. El archivo de la cola también contiene mallas editadas
  // por el usuario: nunca se debe sustituir ese archivo entero para cambiar su sombreado.
  [MenuItem("Traigger/Arte/Rollback cabeza y cola (conservar pelo y rig)")]
  public static void Restore(){
   if(EditorApplication.isPlaying)throw new Exception("Sal de Play antes de restaurar materiales");
   const string head="Assets/Traigger/Art/HeadShading/HeadShadingMaterials 3.asset";
   const string tail="Assets/Traigger/Art/TraiggerMeshData 127.asset";
   Directory.CreateDirectory("Rollback/Antes-de-restaurar");
   using(var zip=ZipFile.OpenRead(Archive)){
    RestoreFile(zip,head,new[]{"4708873233336597939","745965484739843735","1267483130128339480","-6888682536033100524","4167981977151980658"},true);
    RestoreFile(zip,tail,new[]{"6583116383412475987"},false);
   }
   AssetDatabase.Refresh();Debug.Log("TRAIGGER_COAT_ROLLBACK: cabeza y cola restauradas; pelo HD, mallas y huesos conservados");
  }
  static void RestoreFile(ZipArchive zip,string path,string[] ids,bool removeTailCopy){
   var entry=zip.GetEntry("02-estado-antes-cabeza-cola/"+path);if(entry==null)throw new Exception("Falta copia original de "+path);
   string before;using(var reader=new StreamReader(entry.Open()))before=reader.ReadToEnd();
   string current=File.ReadAllText(path);
   File.Copy(path,"Rollback/Antes-de-restaurar/"+Path.GetFileNameWithoutExtension(path)+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".asset",false);
   foreach(string id in ids){
    string pattern=@"(?ms)^--- !u!21 &"+Regex.Escape(id)+@"\r?\n.*?(?=^--- !u!|\z)";
    var original=Regex.Match(before,pattern);if(!original.Success||!Regex.IsMatch(current,pattern))throw new Exception("No se encuentra material "+id);
    current=Regex.Replace(current,pattern,m=>original.Value);
   }
   if(removeTailCopy)current=Regex.Replace(current,@"(?ms)^--- !u!21 &1350981111111111111\r?\n.*?(?=^--- !u!|\z)","");
   File.WriteAllText(path,current);
  }
 }
}
