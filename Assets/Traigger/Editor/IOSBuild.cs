using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
namespace Traigger.Editor {
    // Compilación para iPhone y iPad. Unity genera un proyecto de Xcode en Builds/iOS; se firma y se instala desde
    // Xcode con tu cuenta de Apple. El seguimiento facial usa ARKit con la cámara frontal (TrueDepth).
    public static class IOSBuild {
        // Escena con el cuerpo segmentado nuevo; si no existe, la principal.
        public static string Scene=>File.Exists("Assets/Traigger/Scenes/TraiggerStudio-KemonoBody.unity")?"Assets/Traigger/Scenes/TraiggerStudio-KemonoBody.unity":"Assets/Traigger/Scenes/TraiggerStudio.unity";
        public const string Output="Builds/iOS";
        public const string DefaultBundle="com.traigger.vtuberstudio";
        public static void Configure(){
            // Sólo se rellenan valores vacíos: si ya has puesto tu identificador o tu equipo, se respetan.
            if(string.IsNullOrEmpty(PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS)))
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS,DefaultBundle);
            PlayerSettings.iOS.targetDevice=iOSTargetDevice.iPhoneAndiPad;
            if(new Version(PlayerSettings.iOS.targetOSVersionString)<new Version(15,0))PlayerSettings.iOS.targetOSVersionString="15.0";
            PlayerSettings.iOS.cameraUsageDescription="La cámara sigue tu cara o tu cuerpo para mover al personaje. Las imágenes no se guardan ni se envían.";
            PlayerSettings.iOS.requiresFullScreen=true;
            PlayerSettings.iOS.microphoneUsageDescription="El micrófono mueve la boca del personaje cuando hablas. El audio no se graba ni se envía.";
            PlayerSettings.defaultInterfaceOrientation=UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait=PlayerSettings.allowedAutorotateToLandscapeLeft=PlayerSettings.allowedAutorotateToLandscapeRight=true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown=false;
        }
        [MenuItem("Traigger/4 · Compilar iPhone / iPad (proyecto de Xcode)")]
        public static void Build(){
            if(EditorApplication.isPlaying)throw new Exception("Sal de Play antes de compilar");
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS,BuildTarget.iOS))throw new Exception("Falta el módulo iOS Build Support en Unity Hub");
            Configure();
            // Antes de compilar se reconstruye el avatar con lo último (cuerpo nuevo, sombreado de cabeza) y se guarda,
            // para que el iPhone/iPad no muestre una versión antigua guardada en la escena.
            UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Scene);
            Signing.Apply();
            // Unity dibuja con transparencia: Traigger puede flotar encima de la interfaz nativa (navegador).
            PlayerSettings.preserveFramebufferAlpha=true;
            RepairScene.Repair();
            var options=new BuildPlayerOptions{scenes=new[]{Scene},locationPathName=Output,target=BuildTarget.iOS,targetGroup=BuildTargetGroup.iOS,options=BuildOptions.None};
            var report=BuildPipeline.BuildPlayer(options);
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Compilación iOS fallida: "+report.summary.totalErrors+" errores");
            Debug.Log("TRAIGGER_IOS_XCODE_PROJECT: "+Path.GetFullPath(Output)+" · ábrelo en Xcode (Unity-iPhone.xcodeproj), elige tu equipo en Signing & Capabilities y pulsa Run");
            if(!Application.isBatchMode)EditorUtility.RevealInFinder(Path.Combine(Output,"Unity-iPhone.xcodeproj"));
        }
        // El plugin de seguimiento facial necesita ARKit enlazado en UnityFramework.
        [PostProcessBuild(100)]
        // Sin #if UNITY_IOS: ese símbolo depende de la plataforma activa del editor, y si no era iOS este paso se
        // saltaba en silencio (sin permisos de red local ni de voz en el proyecto de Xcode).
        public static void AddArkit(BuildTarget target,string path){
            if(target!=BuildTarget.iOS)return;
            string projectPath=UnityEditor.iOS.Xcode.PBXProject.GetPBXProjectPath(path);
            var project=new UnityEditor.iOS.Xcode.PBXProject();project.ReadFromFile(projectPath);
            string framework=project.GetUnityFrameworkTargetGuid();
            project.AddFrameworkToProject(framework,"ARKit.framework",false);
            project.AddFrameworkToProject(framework,"AVFoundation.framework",false);project.AddFrameworkToProject(framework,"Vision.framework",false);project.AddFrameworkToProject(framework,"Network.framework",false);
            // Frases y emoción por la voz: reconocimiento de voz y, desde iOS 26, Apple Intelligence (enlace débil para iOS anteriores).
            project.AddFrameworkToProject(framework,"Speech.framework",false);project.AddFrameworkToProject(framework,"NaturalLanguage.framework",false);
            project.AddBuildProperty(framework,"OTHER_LDFLAGS","-weak_framework");project.AddBuildProperty(framework,"OTHER_LDFLAGS","FoundationModels");
            // Xcode 15+ aísla los scripts de compilación y eso impide que el paso de IL2CPP lea sus propias herramientas
            // ("file system sandbox blocked open()"). Se desactiva el aislamiento en el proyecto y en todos los targets.
            foreach(string guid in new[]{project.ProjectGuid(),project.GetUnityMainTargetGuid(),framework,project.TargetGuidByName("GameAssembly")}){
                if(string.IsNullOrEmpty(guid))continue;project.SetBuildProperty(guid,"ENABLE_USER_SCRIPT_SANDBOXING","NO");
                Signing.Apply(project,guid);
                // El verificador de módulos de Xcode rechaza las cabeceras que genera Unity para UnityFramework.
                project.SetBuildProperty(guid,"ENABLE_MODULE_VERIFIER","NO");
            }
            // Perfil de voz, escenas y carteles en iCloud (opcional, ver CloudSyncSetting).
            if(CloudSyncSetting.Enabled){
                string main=project.GetUnityMainTargetGuid();
                string entitlements=project.GetBuildPropertyForAnyConfig(main,"CODE_SIGN_ENTITLEMENTS");
                if(string.IsNullOrEmpty(entitlements)){entitlements="Traigger.entitlements";project.AddFile(entitlements,entitlements);project.SetBuildProperty(main,"CODE_SIGN_ENTITLEMENTS",entitlements);}
                var rights=new UnityEditor.iOS.Xcode.PlistDocument();string rightsPath=Path.Combine(path,entitlements);
                if(File.Exists(rightsPath))rights.ReadFromFile(rightsPath);
                rights.root.SetString("com.apple.developer.ubiquity-kvstore-identifier",CloudSyncSetting.KeyValueStore);
                // iCloud Drive: imágenes y vídeos de escenas y carteles.
                rights.root.CreateArray("com.apple.developer.icloud-container-identifiers").AddString(CloudSyncSetting.Container);
                rights.root.CreateArray("com.apple.developer.ubiquity-container-identifiers").AddString(CloudSyncSetting.Container);
                rights.root.CreateArray("com.apple.developer.icloud-services").AddString("CloudDocuments");
                rights.WriteToFile(rightsPath);
            }
            project.WriteToFile(projectPath);
            // Modo sensor: búsqueda de las Vision Pro en la red local (Bonjour) y texto del permiso.
            string plistPath=Path.Combine(path,"Info.plist");var plist=new UnityEditor.iOS.Xcode.PlistDocument();plist.ReadFromFile(plistPath);
            plist.root.SetString("NSLocalNetworkUsageDescription","Envía tu cuerpo y tus manos a las Vision Pro y el vídeo de Traigger a la cámara «Traigger» de tu Mac.");
            var services=plist.root.CreateArray("NSBonjourServices");services.AddString("_traigger-sensor._udp");services.AddString("_traigger-cam._tcp");services.AddString("_traigger-hdmi._tcp");services.AddString("_traigger-hdmiu._udp");services.AddString("_traigger-audio._tcp");services.AddString("_traigger-face._udp");services.AddString("_smb._tcp"); // capturadora → Vision Pro; sonido → Mac
            plist.root.SetString("NSSpeechRecognitionUsageDescription","Traigger entiende tus frases en el propio dispositivo para poner la expresión que les va. No se graba ni se envía nada.");
            plist.WriteToFile(plistPath);
        }
    }
}
