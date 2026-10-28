using System.IO;
using UnityEditor;
using UnityEngine;
namespace Traigger.EditorTools {
    // Exporta una receta de personaje (RigDefinition) como paquete de personaje (carpeta con character.json y PNG),
    // para compartirlo o usarlo de plantilla. Selecciona la receta en el proyecto y usa el menú.
    public static class CharacterPackExporter {
        [MenuItem("Traigger/Exportar personaje como paquete…")]
        static void Export(){
            var definition=Selection.activeObject as RigDefinition??AssetDatabase.LoadAssetAtPath<RigDefinition>("Assets/Traigger/Art/TraiggerRig.asset");
            if(!definition){EditorUtility.DisplayDialog("Traigger","Selecciona una receta de personaje (RigDefinition).","Vale");return;}
            string folder=EditorUtility.SaveFolderPanel("Carpeta del paquete","",definition.name);
            if(string.IsNullOrEmpty(folder))return;
            CharacterPack.Save(definition,folder,new CharacterPack.Info{name=definition.name,author=PlayerSettings.companyName,version="1.0"},
                texture=>File.ReadAllBytes(AssetDatabase.GetAssetPath(texture)));
            EditorUtility.RevealInFinder(Path.Combine(folder,"character.json"));
        }
    }
}
