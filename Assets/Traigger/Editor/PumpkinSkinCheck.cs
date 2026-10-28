using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Traigger.Editor {
    // Renderiza el paquete alternativo sin tocar ni guardar la escena abierta.
    public static class PumpkinSkinCheck {
        const string Package = "Examples/Traigger-Calabaza";
        const string Output = "Validation/PumpkinSkin";

        [InitializeOnLoadMethod]
        static void ScheduleRequest() { EditorApplication.delayCall += RunRequest; }

        static void RunRequest() {
            string request = Output + ".request";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying) return;
            try { Render(); }
            catch (Exception e) { Directory.CreateDirectory(Output); File.WriteAllText(Output + "/error.txt", e.ToString()); Debug.LogException(e); }
            finally { File.Delete(request); }
        }

        [MenuItem("Traigger/Personaje/Previsualizar Traigger Calabaza")]
        public static void Render() {
            Directory.CreateDirectory(Output);
            var rigDefinition = CharacterPack.Load(Path.GetFullPath(Package), out var metadata);
            GameObject root = null, cameraObject = null;
            RenderTexture target = null;
            var createdTextures = rigDefinition.layers.Select(l => l.texture).Where(t => t).Distinct().ToArray();
            AvatarRig rig = null;
            try {
                root = new GameObject("Preview aislada — " + metadata.name);
                rig = AutoRig.Build(rigDefinition, root.transform);
                rig.transform.localPosition = Vector3.zero;
                const int previewLayer = 31;
                foreach (var transform in root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = previewLayer;
                cameraObject = new GameObject("Preview camera — no guarda escena");
                var cam = cameraObject.AddComponent<Camera>();
                cam.orthographic = true; cam.orthographicSize = 2.7f;
                cam.transform.position = new Vector3(metadata.cameraX, metadata.cameraHeight, -10);
                cam.transform.rotation = Quaternion.identity;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.055f, .055f, .075f, 1);
                cam.nearClipPlane = .1f; cam.farClipPlane = 50; cam.allowHDR = false; cam.allowMSAA = false;
                cam.cullingMask = 1 << previewLayer;
                const int width = 1200, height = 1200;
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                target.antiAliasing = 1; cam.targetTexture = target;
                var armRenderer = rig.layers[Array.FindIndex(rigDefinition.layers, l => l.name == "ArmLeft")];
                var neutralBounds = armRenderer.bounds;
                Save(cam, target, Output + "/neutral.png");

                rig.ResetPose();
                rig.Rotate("RightUpperArm", -48); rig.Rotate("RightLowerArm", -24);
                rig.Rotate("LeftUpperArm", 48); rig.Rotate("LeftLowerArm", 24);
                rig.Rotate("RightShoulder", -8); rig.Rotate("LeftShoulder", 8);
                rig.Rotate("Chest", -3); rig.Rotate("Head", 3);
                Save(cam, target, Output + "/arms-raised.png");
                var raisedBounds = armRenderer.bounds;
                File.WriteAllText(Output + "/results.txt",
                    "PASS: CharacterPack.Load + AutoRig.Build\n" +
                    "Nombre: " + metadata.name + "\n" +
                    "Huesos: " + rigDefinition.bones.Length + " · capas: " + rigDefinition.layers.Length + "\n" +
                    "Todos los PNG referenciados se cargaron; mallas deformables generadas.\n" +
                    "Capturas: neutral.png y arms-raised.png.\n" +
                    "Brazo izquierdo: rotación local " + rig.bones[9].localEulerAngles.z.ToString("F1") + "°; cambio de bounds " + (raisedBounds.center - neutralBounds.center).magnitude.ToString("F4") + " unidades.\n" +
                    "Prueba automatizada de pose; la validación artística final requiere revisar la captura en la app/dispositivo.\n");
            }
            finally {
                if (rig) {
                    foreach (var layer in rig.layers) {
                        if (layer.sharedMesh) UnityEngine.Object.DestroyImmediate(layer.sharedMesh);
                        if (layer.sharedMaterial) UnityEngine.Object.DestroyImmediate(layer.sharedMaterial);
                    }
                    if (rig.closedMouth && rig.closedMouth.sharedMaterial) UnityEngine.Object.DestroyImmediate(rig.closedMouth.sharedMaterial);
                }
                if (cameraObject) UnityEngine.Object.DestroyImmediate(cameraObject);
                if (root) UnityEngine.Object.DestroyImmediate(root);
                if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                foreach (var texture in createdTextures) if (texture) UnityEngine.Object.DestroyImmediate(texture);
                if (rigDefinition) UnityEngine.Object.DestroyImmediate(rigDefinition);
            }
            AssetDatabase.Refresh();
        }

        static void Save(Camera cam, RenderTexture target, string path) {
            var bakedObjects = new System.Collections.Generic.List<GameObject>();
            var originals = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)
                .Where(r => r.gameObject.layer == 31).ToArray();
            try {
                foreach (var skinned in originals) {
                    var mesh = new Mesh { name = skinned.name + "_preview_baked" };
                    skinned.BakeMesh(mesh, true);
                    var go = new GameObject(skinned.name + "_preview_baked") { layer = 31 };
                    go.transform.SetParent(skinned.transform.parent, false);
                    go.transform.localPosition = skinned.transform.localPosition;
                    go.transform.localRotation = skinned.transform.localRotation;
                    go.transform.localScale = skinned.transform.localScale;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = skinned.sharedMaterial;
                    renderer.sortingLayerID = skinned.sortingLayerID;
                    renderer.sortingOrder = skinned.sortingOrder;
                    skinned.enabled = false;
                    bakedObjects.Add(go);
                }
                cam.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                RenderTexture.active = previous;
            }
            finally {
                foreach (var go in bakedObjects) {
                    var filter = go.GetComponent<MeshFilter>();
                    if (filter && filter.sharedMesh) UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
                    UnityEngine.Object.DestroyImmediate(go);
                }
                foreach (var original in originals) if (original) original.enabled = true;
            }
        }
    }
}
