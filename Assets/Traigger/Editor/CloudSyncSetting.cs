using UnityEditor;
using UnityEditor.Build;
namespace Traigger.Editor {
    // Opción de compilación: sincronizar con iCloud el perfil de voz, las escenas y los carteles (con sus imágenes y
    // vídeos), para que iPhone, iPad y Vision Pro compartan lo mismo.
    // Añade el permiso de almacén clave-valor de iCloud, que exige una cuenta de desarrollador de Apple de pago:
    // con el equipo personal gratuito Xcode no podría firmar la app, por eso viene desactivada.
    public static class CloudSyncSetting {
        const string MenuPath="Traigger/Sincronizar con iCloud: voz, escenas y carteles (cuenta de desarrollador de pago)";
        static string Key=>"traigger.icloud."+PlayerSettings.productGUID;
        public static bool Enabled=>EditorPrefs.GetBool(Key,false);
        // Mismo identificador en las dos apps, para que compartan el almacén.
        public static string KeyValueStore=>"$(TeamIdentifierPrefix)"+PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
        // Contenedor de iCloud Drive para las imágenes y vídeos de escenas y carteles (StudioCloud.cs).
        public static string Container=>"iCloud."+PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
        [MenuItem(MenuPath,false,40)]static void Toggle()=>EditorPrefs.SetBool(Key,!Enabled);
        [MenuItem(MenuPath,true)]static bool Validate(){Menu.SetChecked(MenuPath,Enabled);return true;}
    }
}
