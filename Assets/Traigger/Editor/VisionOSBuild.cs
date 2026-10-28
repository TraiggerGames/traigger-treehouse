using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace Traigger.Editor {
    // Fase 1 de Vision Pro: Traigger como app de ventana (Unity Personal, sin PolySpatial). Genera un proyecto de
    // Xcode en Builds/visionOS. Los sensores (cabeza y manos) y SharePlay llegan en las fases siguientes con un host
    // nativo basado en UnityRealityKitBridge.
    public static class VisionOSBuild {
        // Host nativo (carpeta oculta para Unity): espacio inmersivo mixto para cabeza y manos.
        static void IntegrateHost(string destination){
            string folder=Path.Combine(Application.dataPath,"Traigger/Editor/VisionHost~");
            var start=new System.Diagnostics.ProcessStartInfo("/usr/bin/ruby"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
            foreach(var a in new[]{Path.Combine(folder,"integrate_host.rb"),destination,Path.Combine(folder,"Sources"),"production",PlayerSettings.bundleVersion,string.IsNullOrEmpty(PlayerSettings.VisionOS.buildNumber)?"1":PlayerSettings.VisionOS.buildNumber})start.ArgumentList.Add(a);
            start.Environment["TRAIGGER_TEAM"]=Signing.Team;
            start.Environment["TRAIGGER_KVSTORE"]=CloudSyncSetting.Enabled?CloudSyncSetting.KeyValueStore:"";
            start.Environment["TRAIGGER_ICLOUD_CONTAINER"]=CloudSyncSetting.Enabled?CloudSyncSetting.Container:"";
            using var process=System.Diagnostics.Process.Start(start);
            string output=process.StandardOutput.ReadToEnd(),error=process.StandardError.ReadToEnd();process.WaitForExit();
            if(process.ExitCode!=0)throw new Exception("No se pudo añadir el host nativo (requiere la gema Ruby xcodeproj): "+error+output);
            Debug.Log(output);
        }
        public const string Output="Builds/visionOS";
        [MenuItem("Traigger/5 · Compilar Vision Pro (proyecto de Xcode)")]
        public static void Build()=>Build(false);
        [MenuItem("Traigger/5b · Compilar para el simulador de Vision Pro")]
        public static void BuildSimulator()=>Build(true);
        public static void Build(bool simulator){
            if(EditorApplication.isPlaying)throw new Exception("Sal de Play antes de compilar");
            if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.VisionOS,BuildTarget.VisionOS))throw new Exception("Falta el módulo visionOS Build Support en Unity Hub");
            if(string.IsNullOrEmpty(PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.VisionOS)))
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.VisionOS,IOSBuild.DefaultBundle);
            PlayerSettings.iOS.microphoneUsageDescription="El micrófono mueve la boca del personaje cuando hablas. El audio no se graba ni se envía.";
            PlayerSettings.VisionOS.sdkVersion=simulator?VisionOSSdkVersion.Simulator:VisionOSSdkVersion.Device;
            PlayerSettings.preserveFramebufferAlpha=true; // ventana de Traigger transparente
            // Mismo avatar que en iPhone/iPad: se reconstruye y guarda la escena antes de compilar.
            UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(IOSBuild.Scene);
            Signing.Apply();
            RepairScene.Repair();
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{IOSBuild.Scene},locationPathName=Output,target=BuildTarget.VisionOS,targetGroup=BuildTargetGroup.VisionOS,options=BuildOptions.None});
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Compilación visionOS fallida: "+report.summary.totalErrors+" errores");
            IntegrateHost(Path.GetFullPath(Output));
            Debug.Log("TRAIGGER_VISIONOS_XCODE_PROJECT: "+Path.GetFullPath(Output)+" · esquema NativeHost (Traigger)");
            if(!Application.isBatchMode)EditorUtility.RevealInFinder(Output);
        }
    }
}
