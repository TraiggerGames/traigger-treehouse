using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace Traigger {
    // Outfits: ropa para un personaje sin duplicarlo. Cada outfit es una carpeta en StreamingAssets/Outfits/<personaje>/
    // con un outfit.json y sus PNG. Sobre el rig ya montado (cara, pelo por mechones, cuerpo, orejas y cola del personaje):
    //   hide     capas del personaje que se ocultan (p. ej. la camiseta y el pantalón de siempre)
    //   pieces   capas nuevas. «fit»: se encaja sobre una capa existente (su parte opaca ocupa la caja de esa capa, con
    //            «pad» como margen en fracción del tamaño: izquierda, abajo, derecha, arriba) y copia sus pesos de huesos y
    //            sus correctivos, así se deforma igual que la ropa que sustituye. «rect»: posición fija en el espacio del
    //            personaje, con los pesos de «weightsFrom» (p. ej. una capucha que sigue a la cabeza).
    //            Orden de dibujo: el de «orderFrom» (o de la capa encajada) + «orderOffset». «fullImage»: el PNG entero
    //            ocupa la caja (para piezas recortadas del propio dibujo del personaje, como el cuello).
    //   options  interruptores del outfit (p. ej. capucha puesta o bajada); una pieza con «option» solo sale si está activo.
    // Si al personaje le cambian la cara o el pelo, el outfit lo hereda: nunca guarda copia del personaje.
    public static class Outfits {
        [Serializable]public class Piece{public string name,texture,fit,weightsFrom,option,orderFrom,fitTexture,sleeveSide,sleevePart;public float[] rect,pad;public int orderOffset;public bool fullImage;}
        [Serializable]public class Option{public string id,label;public bool on=true;}
        [Serializable]public class Definition{public int format=1;public string name,character="Traigger",author,license;public string[] hide;public Piece[] pieces;public Option[] options;}
        public class Entry{public string id,name,preview;public Definition definition;}

        public static string Root=>Path.Combine(Application.streamingAssetsPath,"Outfits");
        public static string Status="";
        public static string Current{get=>PlayerPrefs.GetString("traigger.outfit","");set{PlayerPrefs.SetString("traigger.outfit",value??"");PlayerPrefs.Save();}}
        public static bool OptionOn(string outfit,Option option)=>PlayerPrefs.GetInt("traigger.outfit."+outfit+"."+option.id,option.on?1:0)==1;
        public static void SetOption(string outfit,string option,bool on){PlayerPrefs.SetInt("traigger.outfit."+outfit+"."+option,on?1:0);PlayerPrefs.Save();}

        // Outfits de un personaje («Normal» = sin outfit, siempre el primero).
        public static List<Entry> For(string character){
            var list=new List<Entry>{new Entry{id="",name="Normal"}};
            string folder=Path.Combine(Root,character);
            if(!Directory.Exists(folder))return list;
            foreach(var dir in Directory.GetDirectories(folder)){
                var json=Path.Combine(dir,"outfit.json");if(!File.Exists(json))continue;
                try{
                    var def=JsonUtility.FromJson<Definition>(File.ReadAllText(json));var preview=Path.Combine(dir,"preview.png");
                    list.Add(new Entry{id=Path.GetFileName(dir),name=string.IsNullOrEmpty(def.name)?Path.GetFileName(dir):def.name,preview=File.Exists(preview)?preview:"",definition=def});
                }catch(Exception e){Debug.LogWarning("Traigger: outfit no válido en "+dir+": "+e.Message);}
            }
            return list;
        }

        // Pone el outfit elegido sobre el rig recién montado (en reposo). Devuelve false si no hay outfit o falla.
        public static bool Apply(AvatarRig rig,string character)=>Apply(rig,character,Current);
        // La previsualización puede elegir un outfit sin modificar las preferencias del usuario.
        public static bool Apply(AvatarRig rig,string character,string id){
            Status="";if(string.IsNullOrEmpty(id)||!rig)return false;
            string folder=Path.Combine(Root,character,id),json=Path.Combine(folder,"outfit.json");
            if(!File.Exists(json)){Status="El outfit «"+id+"» ya no existe";Current="";return false;}
            Definition def;
            try{def=JsonUtility.FromJson<Definition>(File.ReadAllText(json));}catch(Exception e){Status="Outfit no válido: "+e.Message;return false;}
            var renderers=new Dictionary<string,SkinnedMeshRenderer>();
            foreach(var r in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(!renderers.ContainsKey(r.gameObject.name))renderers[r.gameObject.name]=r;
            var hidden=new List<Renderer>();
            foreach(var name in def.hide??new string[0])if(renderers.TryGetValue(name,out var r)){r.forceRenderingOff=true;hidden.Add(r);}
            var follower=rig.gameObject.AddComponent<OutfitFollower>();follower.hidden=hidden;
            int made=0;
            foreach(var piece in def.pieces??new Piece[0]){
                if(!string.IsNullOrEmpty(piece.option)){
                    var option=Array.Find(def.options??new Option[0],o=>o.id==piece.option);
                    if(option!=null&&!OptionOn(id,option))continue;
                }
                try{if(Build(rig,folder,piece,renderers,follower))made++;}
                catch(Exception e){Debug.LogError("Traigger: pieza «"+piece.name+"» del outfit: "+e.Message);}
            }
            foreach(string side in new[]{"Left","Right"})if(Array.Exists(def.pieces??new Piece[0],p=>p.sleeveSide==(side=="Left"?"Right":"Left"))&&renderers.TryGetValue("Kemono_UpperArm"+side,out var arm))follower.SafeSkinJoin(arm);
            Status="Outfit «"+(string.IsNullOrEmpty(def.name)?id:def.name)+"»: "+made+" piezas";
            Debug.Log("Traigger: "+Status+", "+hidden.Count+" capas del personaje ocultas");
            return made>0;
        }

        static bool Build(AvatarRig rig,string folder,Piece piece,Dictionary<string,SkinnedMeshRenderer> renderers,OutfitFollower follower){
            string targetName=!string.IsNullOrEmpty(piece.fit)?piece.fit:piece.weightsFrom;
            if(string.IsNullOrEmpty(targetName)||!renderers.TryGetValue(targetName,out var target)||!target.sharedMesh){Debug.LogWarning("Traigger: el outfit pide la capa «"+targetName+"», que este personaje no tiene");return false;}
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,true){name=piece.name,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};
            if(!texture.LoadImage(File.ReadAllBytes(Path.Combine(folder,piece.texture))))throw new Exception("no se puede leer "+piece.texture);
            var source=target.sharedMesh;var targetVertices=source.vertices;var targetWeights=source.boneWeights;
            // Caja de la pieza en el espacio de la capa objetivo.
            Rect area;
            if(!string.IsNullOrEmpty(piece.fit)){
                // «fullImage»: el PNG entero (no solo su parte opaca) ocupa la caja; para piezas recortadas del propio dibujo.
                var fitTexture=texture;
                if(!string.IsNullOrEmpty(piece.fitTexture)){
                    fitTexture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                    fitTexture.LoadImage(File.ReadAllBytes(Path.Combine(folder,piece.fitTexture)));
                }
                var opaque=piece.fullImage?new Rect(0,0,1,1):OpaqueBox(fitTexture);var b=source.bounds;
                if(fitTexture!=texture)Release(fitTexture);
                float[] pad=piece.pad!=null&&piece.pad.Length==4?piece.pad:new float[4];
                var box=Rect.MinMaxRect(b.min.x-pad[0]*b.size.x,b.min.y-pad[1]*b.size.y,b.max.x+pad[2]*b.size.x,b.max.y+pad[3]*b.size.y);
                // La parte opaca del dibujo ocupa la caja; el resto del PNG (transparente) se extiende proporcionalmente.
                float sx=box.width/Mathf.Max(opaque.width,1e-4f),sy=box.height/Mathf.Max(opaque.height,1e-4f);
                area=new Rect(box.xMin-opaque.xMin*sx,box.yMin-opaque.yMin*sy,sx,sy);
            }else{
                if(piece.rect==null||piece.rect.Length!=4)throw new Exception("falta «rect» o «fit»");
                Vector3 a=target.transform.InverseTransformPoint(rig.transform.TransformPoint(new Vector3(piece.rect[0],piece.rect[1]))),
                        c=target.transform.InverseTransformPoint(rig.transform.TransformPoint(new Vector3(piece.rect[0]+piece.rect[2],piece.rect[1]+piece.rect[3])));
                area=Rect.MinMaxRect(a.x,a.y,c.x,c.y);
            }
            // Malla en rejilla sobre la caja; cada vértice copia los pesos y los correctivos del vértice más cercano de la capa objetivo.
            bool sleeve=!string.IsNullOrEmpty(piece.sleeveSide);
            int cols=sleeve?36:14,rows=sleeve?36:18;var vertices=new Vector3[(cols+1)*(rows+1)];var uv=new Vector2[vertices.Length];var weights=new BoneWeight[vertices.Length];var nearest=new int[vertices.Length];
            for(int y=0,i=0;y<=rows;y++)for(int x=0;x<=cols;x++,i++){
                uv[i]=new Vector2(x/(float)cols,y/(float)rows);
                vertices[i]=new Vector3(area.xMin+uv[i].x*area.width,area.yMin+uv[i].y*area.height,0);
                int best=0;float bestDistance=float.MaxValue;
                for(int k=0;k<targetVertices.Length;k++){float d=((Vector2)targetVertices[k]-(Vector2)vertices[i]).sqrMagnitude;if(d<bestDistance){bestDistance=d;best=k;}}
                nearest[i]=best;weights[i]=targetWeights.Length>best?targetWeights[best]:new BoneWeight{weight0=1};
            }
            // Los ayudantes angulares reparten el giro por la sisa, sin comprimir toda la manga como una bisagra.
            // Las capas del tubo usan el mismo lienzo y esta misma función: no pueden separarse al animarse.
            if(sleeve)OutfitSleeveBinding.Bind(target,piece.sleeveSide,uv,weights);
            var triangles=new List<int>();
            for(int y=0;y<rows;y++)for(int x=0;x<cols;x++){int a=y*(cols+1)+x,b=a+1,c=a+cols+1,d=c+1;triangles.AddRange(new[]{a,c,b,b,c,d});}
            var mesh=new Mesh{name=piece.name+" · outfit"};mesh.vertices=vertices;mesh.uv=uv;mesh.boneWeights=weights;mesh.bindposes=source.bindposes;mesh.SetTriangles(triangles,0);
            var zero=new Vector3[vertices.Length];
            for(int shape=0;shape<source.blendShapeCount;shape++)for(int frame=0;frame<source.GetBlendShapeFrameCount(shape);frame++){
                var d=new Vector3[source.vertexCount];var n=new Vector3[d.Length];var t=new Vector3[d.Length];source.GetBlendShapeFrameVertices(shape,frame,d,n,t);
                var delta=new Vector3[vertices.Length];for(int i=0;i<delta.Length;i++)delta[i]=d[nearest[i]];
                mesh.AddBlendShapeFrame(source.GetBlendShapeName(shape),source.GetBlendShapeFrameWeight(shape,frame),delta,zero,zero);
            }
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            var go=new GameObject("Outfit_"+piece.name);go.transform.SetParent(target.transform.parent,false);
            go.transform.localPosition=target.transform.localPosition;go.transform.localRotation=target.transform.localRotation;go.transform.localScale=target.transform.localScale;
            go.layer=target.gameObject.layer;
            var renderer=go.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=mesh;renderer.bones=target.bones;renderer.rootBone=target.rootBone;
            renderer.quality=SkinQuality.Bone4;renderer.updateWhenOffscreen=true;renderer.localBounds=new Bounds(new Vector3(0,2,0),new Vector3(12,12,4));
            var material=new Material(Shader.Find("Traigger/UnlitAlpha")){name=piece.name+" · outfit",mainTexture=texture};renderer.sharedMaterial=material;
            if(sleeve){
                material.shader=Shader.Find("Traigger/OutfitSleeve");
                var pigment=new Texture2D(2,2,TextureFormat.RGBA32,true){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};
                pigment.LoadImage(File.ReadAllBytes(Path.Combine(folder,piece.fitTexture)));
                material.SetTexture("_SourceTex",pigment);material.SetFloat("_Mirror",piece.sleeveSide=="Left"?1:0);
                material.SetFloat("_Part",piece.sleevePart=="Lining"?2:piece.sleevePart=="Cuff"?1:0);
            }
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
            int order=target.sortingOrder;
            if(!string.IsNullOrEmpty(piece.orderFrom)&&renderers.TryGetValue(piece.orderFrom,out var reference))order=reference.sortingOrder;
            renderer.sortingLayerID=target.sortingLayerID;renderer.sortingOrder=order+piece.orderOffset;
            var orderTarget=target;
            if(!string.IsNullOrEmpty(piece.orderFrom)&&renderers.TryGetValue(piece.orderFrom,out var orderRenderer))orderTarget=orderRenderer;
            follower.Add(renderer,target,orderTarget,piece.orderOffset);
            return true;
        }
        internal static void Release(UnityEngine.Object item){if(Application.isPlaying)UnityEngine.Object.Destroy(item);else UnityEngine.Object.DestroyImmediate(item);}
        // Parte opaca del PNG en coordenadas 0–1 (origen abajo a la izquierda).
        static Rect OpaqueBox(Texture2D texture){
            var pixels=texture.GetPixels32(0);int w=texture.width,h=texture.height,x0=w,y0=h,x1=-1,y1=-1;
            for(int y=0;y<h;y+=2)for(int x=0;x<w;x+=2)if(pixels[y*w+x].a>20){if(x<x0)x0=x;if(x>x1)x1=x;if(y<y0)y0=y;if(y>y1)y1=y;}
            if(x1<0)return new Rect(0,0,1,1);
            return Rect.MinMaxRect(x0/(float)w,y0/(float)h,(x1+1)/(float)w,(y1+1)/(float)h);
        }
    }

    // Mantiene las piezas del outfit al día: copian los correctivos (giro 2,5D, etc.) de la capa que siguen y las capas
    // ocultas del personaje no vuelven a aparecer aunque otro sistema las active.
    [DefaultExecutionOrder(600)] public class OutfitFollower : MonoBehaviour {
        public List<Renderer> hidden=new List<Renderer>();
        readonly List<(SkinnedMeshRenderer piece,SkinnedMeshRenderer target,SkinnedMeshRenderer order,int offset)> pieces=new List<(SkinnedMeshRenderer,SkinnedMeshRenderer,SkinnedMeshRenderer,int)>();
        readonly List<(SkinnedMeshRenderer renderer,Material original)> skins=new List<(SkinnedMeshRenderer,Material)>();
        public void SafeSkinJoin(SkinnedMeshRenderer arm){skins.Add((arm,arm.sharedMaterial));arm.sharedMaterial=new Material(arm.sharedMaterial){name=arm.sharedMaterial.name+" · outfit"};}
        public void Add(SkinnedMeshRenderer piece,SkinnedMeshRenderer target,SkinnedMeshRenderer order,int offset)=>pieces.Add((piece,target,order,offset));
        void LateUpdate()=>Sync();
        public void Sync(){
            foreach(var (renderer,_) in skins)if(renderer)renderer.sharedMaterial.SetFloat("_SkinColorJoin",2);
            foreach(var r in hidden)if(r)r.forceRenderingOff=true;
            foreach(var (piece,target,order,offset) in pieces){
                if(!piece||!target)continue;
                if(order){piece.sortingOrder=order.sortingOrder+offset;piece.sortingLayerID=order.sortingLayerID;}
                int count=Mathf.Min(piece.sharedMesh.blendShapeCount,target.sharedMesh?target.sharedMesh.blendShapeCount:0);
                for(int i=0;i<count;i++)piece.SetBlendShapeWeight(i,target.GetBlendShapeWeight(i));
            }
        }
        void OnDestroy(){
            foreach(var (piece,_,__,___) in pieces)if(piece){Outfits.Release(piece.sharedMesh);Outfits.Release(piece.sharedMaterial.mainTexture);if(piece.sharedMaterial.HasProperty("_SourceTex"))Outfits.Release(piece.sharedMaterial.GetTexture("_SourceTex"));Outfits.Release(piece.sharedMaterial);Outfits.Release(piece.gameObject);}
            foreach(var (renderer,original) in skins)if(renderer){Outfits.Release(renderer.sharedMaterial);renderer.sharedMaterial=original;}
            foreach(var r in hidden)if(r)r.forceRenderingOff=false;
        }
    }
}
