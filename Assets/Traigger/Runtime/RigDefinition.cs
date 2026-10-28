using System;
using UnityEngine;
namespace Traigger {
    [Serializable] public class BoneSpec {
        public string name; public int parent = -1;
        [Tooltip("Posición absoluta en el espacio del avatar, en reposo.")] public Vector2 position;
        public BoneSpec(string n, int p, float x, float y) { name=n; parent=p; position=new Vector2(x,y); }
    }
    public enum FaceDeform { None, BlinkLeft, BlinkRight, Mouth }
    [Serializable] public class LayerSpec {
        public string name;
        public Texture2D texture;
        public Color tint = Color.white;
        [Tooltip("Región UV del atlas, en coordenadas normalizadas.")] public Rect uvRect = new Rect(0,0,1,1);
        public Rect rect = new Rect(-.5f,0,1,1);
        [Range(2,40)] public int columns=12, rows=16;
        [Tooltip("Huesos permitidos para esta capa. Evita mezclar brazo y torso por proximidad.")] public int[] bones;
        [Range(.1f,6)] public float falloff=2;
        [Tooltip("Extiende suavemente la zona superior central para solapar cuello y cabeza.")] public float topCenterExtension;
        [Tooltip("Hueco central inferior de la malla: ancho y alto normalizados. Evita pelo sobre el cuello.")] public Vector2 bottomCenterCutout;
        [Tooltip("Abre sólo el borde de los carrillos para que su pelo asome sin duplicarlo en los mechones.")] public float cheekSpread;
        [Tooltip("Retira los laterales de la malla (ancho normalizado, alto mínimo y máximo) para que una pieza aparte, p. ej. la manga, los sustituya.")] public Vector3 sideCutout;
        public int order;
        public FaceDeform face;
    }
    [CreateAssetMenu(menuName="Traigger/Rig Definition")]
    public class RigDefinition : ScriptableObject {
        public Shader layerShader;
        public BoneSpec[] bones = Array.Empty<BoneSpec>();
        public LayerSpec[] layers = Array.Empty<LayerSpec>();
    }
}
