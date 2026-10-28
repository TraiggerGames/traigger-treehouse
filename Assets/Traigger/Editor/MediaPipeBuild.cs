using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Traigger.Editor {
    // Los SDK estáticos permanecen fuera de Assets: no afectan a macOS ni visionOS.
    // Al exportar iOS se copian únicamente las variantes del destino elegido.
    public static class MediaPipeBuild {
        [PostProcessBuild(200)]
        public static void Install(BuildTarget target, string path) {
            if (target != BuildTarget.iOS) return;
            string root = Path.GetFullPath("ThirdParty/MediaPipe-1.1.1");
            // Opcional: sin el SDK (Tools/fetch_thirdparty.sh) se compila sin el modo híbrido MediaPipe (canImport en Swift).
            if (!Directory.Exists(Path.Combine(root, "MediaPipeTasksVision.xcframework"))) {
                UnityEngine.Debug.LogWarning("Traigger: sin MediaPipe en ThirdParty/; el modo HDMI híbrido no estará disponible. Ejecuta Tools/fetch_thirdparty.sh para añadirlo.");
                return;
            }
            string slice = PlayerSettings.iOS.sdkVersion == iOSSdkVersion.SimulatorSDK ? "ios-arm64-simulator" : "ios-arm64";
            string destination = Path.Combine(path, "MediaPipe");
            Directory.CreateDirectory(destination);
            var project = new PBXProject();
            string projectPath = PBXProject.GetPBXProjectPath(path);
            project.ReadFromFile(projectPath);
            string framework = project.GetUnityFrameworkTargetGuid();
            foreach (string name in new[] { "MediaPipeTasksCommon", "MediaPipeTasksVision" }) {
                string source = Path.Combine(root, name + ".xcframework", slice, name + ".framework");
                if (!Directory.Exists(source)) throw new Exception("Falta SDK MediaPipe: " + source);
                CopyDirectory(source, Path.Combine(destination, name + ".framework"));
                string relative = "MediaPipe/" + name + ".framework";
                project.AddFileToBuild(framework, project.AddFile(relative, relative));
            }
            const string graphs = "MediaPipeTaskGraphs_library.a";
            File.Copy(Path.Combine(root, "MediaPipeTaskGraphs_library.xcframework", slice, graphs), Path.Combine(destination, graphs), true);
            project.AddFile("MediaPipe/" + graphs, "MediaPipe/" + graphs);
            project.AddBuildProperty(framework, "FRAMEWORK_SEARCH_PATHS", "$(PROJECT_DIR)/MediaPipe");
            // Mantener los registros de calculadores C++ aunque no tengan referencias directas.
            // force_load sólo afecta a este archivo; all_load también arrastraría bibliotecas de Unity.
            project.AddBuildProperty(framework, "OTHER_LDFLAGS", "-ObjC");
            project.AddBuildProperty(framework, "OTHER_LDFLAGS", "-force_load \"$(PROJECT_DIR)/MediaPipe/" + graphs + "\"");
            foreach (string name in new[] { "Accelerate.framework", "AVFoundation.framework", "CoreMedia.framework", "AudioToolbox.framework", "CoreGraphics.framework", "CoreImage.framework", "CoreVideo.framework", "QuartzCore.framework", "Metal.framework" })
                project.AddFrameworkToProject(framework, name, false);
            foreach (string library in new[] { "libc++.tbd", "libsqlite3.tbd" })
                project.AddFileToBuild(framework, project.AddFile("usr/lib/" + library, "Frameworks/" + library, PBXSourceTree.Sdk));
            project.WriteToFile(projectPath);
            // Data/Raw es el StreamingAssets de Unity en iOS. También copiarlo aquí permite exportaciones incrementales.
            string modelDirectory = Path.Combine(path, "Data/Raw");
            Directory.CreateDirectory(modelDirectory);
            string model = Path.Combine(root, "face_landmarker.task");
            if (!File.Exists(model)) model = Path.GetFullPath("Assets/StreamingAssets/face_landmarker.task");
            if (File.Exists(model)) File.Copy(model, Path.Combine(modelDirectory, "face_landmarker.task"), true);
        }
        static void CopyDirectory(string source, string destination) {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
