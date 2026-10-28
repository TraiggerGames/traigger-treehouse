using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace Traigger {
    // Paquetes de personaje: cualquier creador puede meter su propio VTuber sin Unity ni código. Un paquete es una
    // carpeta (o un .zip con extensión .traigger) con:
    //   character.json   huesos, capas y datos del personaje (formato en docs/PAQUETES_DE_PERSONAJE.md)
    //   *.png            las piezas dibujadas, con transparencia (cabeza, ojos, boca, pelo, brazos…)
    // Traigger lo monta con AutoRig (malla, pesos, parpadeo y boca). Las mejoras específicas (orejas, pelo, nariz,
    // ojos por capas) se activan solas si el personaje usa los nombres estándar de huesos y capas.
    public static class CharacterPack {
        public const int Format=1;
        [Serializable]public class Bone{public string name;public int parent=-1;public float x,y;}
        [Serializable]public class Layer{
            public string name,texture;
            public float[] uv={0,0,1,1};          // zona de la imagen (x, y, ancho, alto; 0–1, origen abajo a la izquierda)
            public float[] rect={-.5f,0,1,1};     // dónde va en el personaje (x, y, ancho, alto; unidades del personaje)
            public int columns=12,rows=16;        // densidad de la malla
            public int[] bones;                   // huesos que la mueven
            public float falloff=2;
            public int order;                     // orden de dibujo (mayor = delante)
            public string face="None";            // None, BlinkLeft, BlinkRight o Mouth
            public string tint="#FFFFFF";
            public float topCenterExtension,cheekSpread;
            public float[] bottomCenterCutout={0,0},sideCutout={0,0,0};
        }
        [Serializable]public class Info{
            public int format=Format;
            public string name="",author="",version="1.0",license="",description="",website="";
            public float cameraSize=2.8f,cameraHeight=2.45f,cameraX; // encuadre (alto visible y centro X/Y)
            public Bone[] bones=new Bone[0];
            public Layer[] layers=new Layer[0];
        }
        public class Installed{public string path,name,author,version,license,preview;public bool builtIn;}

        public static string Folder=>Path.Combine(Application.persistentDataPath,"Personajes");

        // Personajes instalados (cada uno en su carpeta con character.json).
        public static List<Installed> List(){
            var list=new List<Installed>{new Installed{path="",name="Traigger",author="Traigger",version="original",license="Todos los derechos reservados (LICENSE-ART.md)",builtIn=true}};
            // Skins 3D incluidos (Resources/Skins3D), experimentales.
            foreach(var skin in Skin3DAvatar.Available)list.Add(new Installed{path=Skin3DAvatar.Prefix+skin.id,name=skin.name,author="Traigger",version="3D experimental",license="Todos los derechos reservados (LICENSE-ART.md)",builtIn=true});
            if(!Directory.Exists(Folder))return list;
            foreach(var dir in Directory.GetDirectories(Folder)){
                var json=Path.Combine(dir,"character.json");if(!File.Exists(json))continue;
                try{
                    var info=JsonUtility.FromJson<Info>(File.ReadAllText(json));
                    var preview=Path.Combine(dir,"preview.png");
                    list.Add(new Installed{path=dir,name=string.IsNullOrEmpty(info.name)?Path.GetFileName(dir):info.name,author=info.author,version=info.version,license=info.license,preview=File.Exists(preview)?preview:""});
                }catch(Exception e){Debug.LogWarning("Traigger: paquete de personaje no válido en "+dir+": "+e.Message);}
            }
            return list;
        }

        // Lee un paquete y crea su receta en memoria. Lanza una excepción con un mensaje claro si algo falla.
        public static RigDefinition Load(string folder,out Info info){
            var json=Path.Combine(folder,"character.json");
            if(!File.Exists(json))throw new FileNotFoundException("Falta character.json en el paquete");
            info=JsonUtility.FromJson<Info>(File.ReadAllText(json));
            if(info.format>Format)throw new Exception("Este paquete es de una versión más nueva de Traigger (formato "+info.format+")");
            if(info.bones==null||info.bones.Length==0)throw new Exception("El paquete no tiene huesos");
            var definition=ScriptableObject.CreateInstance<RigDefinition>();
            definition.name=string.IsNullOrEmpty(info.name)?Path.GetFileName(folder):info.name;
            var bones=new BoneSpec[info.bones.Length];
            for(int i=0;i<bones.Length;i++){var b=info.bones[i];bones[i]=new BoneSpec(b.name,b.parent,b.x,b.y);}
            definition.bones=bones;
            var textures=new Dictionary<string,Texture2D>();
            var layers=new List<LayerSpec>();
            foreach(var l in info.layers??new Layer[0]){
                if(string.IsNullOrEmpty(l.texture))throw new Exception("La capa «"+l.name+"» no tiene imagen");
                if(!textures.TryGetValue(l.texture,out var texture)){
                    var file=Path.Combine(folder,l.texture);
                    if(!File.Exists(file))throw new FileNotFoundException("Falta la imagen "+l.texture);
                    texture=new Texture2D(2,2,TextureFormat.RGBA32,true){name=l.texture,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};
                    if(!texture.LoadImage(File.ReadAllBytes(file)))throw new Exception("No se puede leer "+l.texture+" (usa PNG)");
                    textures[l.texture]=texture;
                }
                ColorUtility.TryParseHtmlString(l.tint??"#FFFFFF",out var tint);
                layers.Add(new LayerSpec{
                    name=l.name,texture=texture,tint=tint,uvRect=R(l.uv,new Rect(0,0,1,1)),rect=R(l.rect,new Rect(-.5f,0,1,1)),
                    columns=Mathf.Clamp(l.columns,2,40),rows=Mathf.Clamp(l.rows,2,40),bones=l.bones,falloff=l.falloff,order=l.order,
                    face=Enum.TryParse<FaceDeform>(l.face,out var face)?face:FaceDeform.None,
                    topCenterExtension=l.topCenterExtension,cheekSpread=l.cheekSpread,
                    bottomCenterCutout=V2(l.bottomCenterCutout),sideCutout=V3(l.sideCutout)});
            }
            definition.layers=layers.ToArray();
            AutoRig.Validate(definition);
            return definition;
        }

        // Escribe una receta como paquete (lo usa el exportador del editor y sirve de plantilla para creadores).
        public static void Save(RigDefinition definition,string folder,Info meta,Func<Texture2D,byte[]> png){
            Directory.CreateDirectory(folder);
            var info=meta??new Info();info.format=Format;
            info.bones=Array.ConvertAll(definition.bones,b=>new Bone{name=b.name,parent=b.parent,x=b.position.x,y=b.position.y});
            var written=new Dictionary<Texture2D,string>();var layers=new List<Layer>();
            foreach(var l in definition.layers){
                if(l.texture&&!written.ContainsKey(l.texture)){
                    string file=Safe(l.texture.name)+".png";int n=1;while(written.ContainsValue(file))file=Safe(l.texture.name)+"-"+(n++)+".png";
                    File.WriteAllBytes(Path.Combine(folder,file),png(l.texture));written[l.texture]=file;
                }
                layers.Add(new Layer{name=l.name,texture=l.texture?written[l.texture]:"",uv=F(l.uvRect),rect=F(l.rect),columns=l.columns,rows=l.rows,bones=l.bones,
                    falloff=l.falloff,order=l.order,face=l.face.ToString(),tint="#"+ColorUtility.ToHtmlStringRGBA(l.tint),
                    topCenterExtension=l.topCenterExtension,cheekSpread=l.cheekSpread,bottomCenterCutout=new[]{l.bottomCenterCutout.x,l.bottomCenterCutout.y},
                    sideCutout=new[]{l.sideCutout.x,l.sideCutout.y,l.sideCutout.z}});
            }
            info.layers=layers.ToArray();
            File.WriteAllText(Path.Combine(folder,"character.json"),JsonUtility.ToJson(info,true));
        }

        static Rect R(float[] v,Rect fallback)=>v!=null&&v.Length==4?new Rect(v[0],v[1],v[2],v[3]):fallback;
        static Vector2 V2(float[] v)=>v!=null&&v.Length>=2?new Vector2(v[0],v[1]):Vector2.zero;
        static Vector3 V3(float[] v)=>v!=null&&v.Length>=3?new Vector3(v[0],v[1],v[2]):Vector3.zero;
        static float[] F(Rect r)=>new[]{r.x,r.y,r.width,r.height};
        static string Safe(string s){foreach(var c in Path.GetInvalidFileNameChars())s=s.Replace(c,'-');return string.IsNullOrEmpty(s)?"pieza":s;}
    }
}
