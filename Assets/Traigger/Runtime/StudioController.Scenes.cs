using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace Traigger {
    // Escenas: composiciones listas con un toque (como las escenas de OBS) y las que guarde el usuario. Cada escena
    // fija dónde va Traigger (o si se oculta) y qué cartel se muestra. Se recuerdan entre sesiones.
    public partial class StudioController {
        [Serializable]public class SavedScene{public string name,layout="center",card="",title="",subtitle="",image="";public bool avatarHidden,cover;public string titleMedia="";
            // Layout: zonas de la película y la capturadora, y posición y tamaño de Traigger.
            public string movieRect="",captureRect="";public Vector2 avatarOffset;public float avatarScale;}
        [Serializable]class SceneList{public List<SavedScene> items=new List<SavedScene>();}
        public static readonly string[] BuiltInScenes={"Solo Traigger","Contenido + logo","Contenido + al lado","Solo contenido","Vuelvo enseguida","Empezamos pronto","Pausa","Fin"};
        StreamCards cards;SceneList savedScenes;public string currentScene="";
        public StreamCards Cards{get{
            if(!cards&&avatarCamera){cards=gameObject.AddComponent<StreamCards>();cards.target=avatarCamera;}
            return cards;}}
        SceneList Saved{get{
            if(savedScenes==null){try{savedScenes=JsonUtility.FromJson<SceneList>(PlayerPrefs.GetString("traigger.scenes",""));}catch(Exception){}savedScenes??=new SceneList();}
            return savedScenes;}}
        public string[] SceneNames(){var names=new List<string>(BuiltInScenes);foreach(var s in Saved.items)names.Add(s.name);return names.ToArray();}

        public void ApplyScene(string name){
            var saved=Saved.items.Find(s=>s.name==name);
            if(saved!=null){
                SetAvatarHidden(saved.avatarHidden);
                if(saved.layout!="custom")SetLayout(saved.layout);
                if(saved.movieRect.Length>0)PlayerPrefs.SetString("traigger.layer.movie",saved.movieRect);
                if(saved.captureRect.Length>0)PlayerPrefs.SetString("traigger.layer.capture",saved.captureRect);
                ApplyLayerRects();
                if(manipulator&&saved.avatarScale>0)manipulator.MoveTo(saved.avatarOffset,saved.avatarScale);
                layout=saved.layout;
                if(string.IsNullOrEmpty(saved.card))Cards?.Hide();
                else{var card=StreamCards.Preset(saved.card);if(!string.IsNullOrEmpty(saved.title))card.title=saved.title;if(saved.subtitle!=null)card.subtitle=saved.subtitle;card.image=saved.image;card.titleMedia=saved.titleMedia??"";card.coverAvatar=saved.cover;Cards?.Show(card);}
                currentScene=name;return;
            }
            switch(name){
            case "Solo Traigger":SetAvatarHidden(false);SetLayout("center");Cards?.Hide();break;
            case "Contenido + logo":SetAvatarHidden(false);SetLayout("corner");Cards?.Hide();break;
            case "Contenido + al lado":SetAvatarHidden(false);SetLayout("side");Cards?.Hide();break;
            case "Solo contenido":SetAvatarHidden(true);Cards?.Hide();break;
            case "Vuelvo enseguida":SetAvatarHidden(false);SetLayout("corner");ShowCard("brb");break;
            case "Empezamos pronto":SetAvatarHidden(false);SetLayout("center");ShowCard("soon");break;
            case "Pausa":SetAvatarHidden(false);SetLayout("corner");ShowCard("pause");break;
            case "Fin":SetAvatarHidden(false);SetLayout("center");ShowCard("end");break;
            default:return;
            }
            currentScene=name;
        }
        // Textos propios para los carteles (se recuerdan por tipo de cartel).
        void ShowCard(string kind){
            var card=StreamCards.Preset(kind);
            string custom=PlayerPrefs.GetString("traigger.card."+kind,"");
            if(custom.Length>0){var lines=custom.Split('\n');card.title=lines[0];card.subtitle=lines.Length>1?lines[1]:"";}
            card.image=PlayerPrefs.GetString("traigger.cardImage","");
            card.titleMedia=PlayerPrefs.GetString("traigger.cardTitleMedia."+kind,"");
            card.coverAvatar=PlayerPrefs.GetInt("traigger.cardCover",0)==1;
            Cards?.Show(card);
        }
        public void SaveScene(string name){
            if(string.IsNullOrWhiteSpace(name)||Array.IndexOf(BuiltInScenes,name)>=0)return;
            var c=Cards!=null?Cards.current:null;
            Saved.items.RemoveAll(s=>s.name==name);
            Saved.items.Add(new SavedScene{name=name,layout=layout,movieRect=Format(MovieRect),captureRect=Format(CaptureRect),
                avatarOffset=manipulator?manipulator.offset:Vector2.zero,avatarScale=manipulator?manipulator.scale:0,avatarHidden=avatarHidden,card=c!=null?c.kind:"",title=c!=null?c.title:"",subtitle=c!=null?c.subtitle:"",image=c!=null?c.image:"",titleMedia=c!=null?c.titleMedia:"",cover=c!=null&&c.coverAvatar});
            PlayerPrefs.SetString("traigger.scenes",JsonUtility.ToJson(Saved));currentScene=name;
        }
        public void DeleteScene(string name){Saved.items.RemoveAll(s=>s.name==name);PlayerPrefs.SetString("traigger.scenes",JsonUtility.ToJson(Saved));}

        // Personajes (paquetes): cambiar reinicia el estudio con el elegido; importar copia la carpeta o el .zip.
        public void LoadCharacter(string path){
            PlayerPrefs.SetString("traigger.character",path??"");PlayerPrefs.Save();
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
        public static string ImportCharacter(string source){
            Directory.CreateDirectory(CharacterPack.Folder);
            string name=Path.GetFileNameWithoutExtension(source.TrimEnd('/'));
            string destination=Path.Combine(CharacterPack.Folder,name);int n=2;
            while(Directory.Exists(destination))destination=Path.Combine(CharacterPack.Folder,name+" "+(n++));
            if(Directory.Exists(source))CopyFolder(source,destination);
            else{
                // .zip / .traigger: si todo viene dentro de una carpeta, se usa esa carpeta.
                using(var zip=new System.IO.Compression.ZipArchive(File.OpenRead(source),System.IO.Compression.ZipArchiveMode.Read)){
                    string root=Path.GetFullPath(destination)+Path.DirectorySeparatorChar;
                    foreach(var entry in zip.Entries){
                        if(entry.FullName.StartsWith("__MACOSX"))continue;
                        string target=Path.GetFullPath(Path.Combine(destination,entry.FullName));
                        if(!target.StartsWith(root))throw new Exception("El zip tiene rutas no válidas");   // nada fuera de su carpeta
                        if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(target);continue;}
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using(var input=entry.Open())using(var output=File.Create(target))input.CopyTo(output);
                    }
                }
                if(!File.Exists(Path.Combine(destination,"character.json"))){
                    var inner=Directory.GetDirectories(destination);
                    if(inner.Length==1&&File.Exists(Path.Combine(inner[0],"character.json"))){var temp=destination+".tmp";Directory.Move(inner[0],temp);Directory.Delete(destination,true);Directory.Move(temp,destination);}
                }
            }
            CharacterPack.Load(destination,out _); // valida: si está mal, avisa ya
            return destination;
        }
        static void CopyFolder(string from,string to){
            Directory.CreateDirectory(to);
            foreach(var f in Directory.GetFiles(from))File.Copy(f,Path.Combine(to,Path.GetFileName(f)),true);
            foreach(var d in Directory.GetDirectories(from))CopyFolder(d,Path.Combine(to,Path.GetFileName(d)));
        }
        public string characterMessage="";

        // Órdenes de escenas y carteles desde la interfaz (TraiggerBridge).
        bool ApplySceneCommand(BridgeCommand c){
            switch(c.cmd){
            case "scene.apply":ApplyScene(c.s);return true;
            case "scene.save":SaveScene(c.s);return true;
            case "scene.delete":DeleteScene(c.s);return true;
            case "card.show":ShowCard(c.s);currentScene="";return true;
            case "card.hide":Cards?.Hide();currentScene="";return true;
            case "card.text":{
                // «tipo\ntítulo\nsubtítulo»: se guarda y, si ese cartel está a la vista, se actualiza.
                var parts=(c.s??"").Split('\n');if(parts.Length<2)return true;
                PlayerPrefs.SetString("traigger.card."+parts[0],parts[1]+"\n"+(parts.Length>2?parts[2]:""));
                if(Cards&&Cards.Showing&&Cards.current.kind==parts[0])ShowCard(parts[0]);
                return true;}
            case "character.load":LoadCharacter(c.s);return true;
            // Outfit y sus opciones: se guardan y el estudio se reinicia con la ropa nueva (como al cambiar de personaje).
            case "outfit.select":Outfits.Current=c.s??"";LoadCharacter(PlayerPrefs.GetString("traigger.character",""));return true;
            case "outfit.option":Outfits.SetOption(Outfits.Current,c.s??"",c.b);LoadCharacter(PlayerPrefs.GetString("traigger.character",""));return true;
            case "character.import":
                try{var path=ImportCharacter(c.s);characterMessage="Personaje importado: "+Path.GetFileName(path);}
                catch(Exception e){characterMessage="No se pudo importar: "+e.Message;Debug.LogWarning("Traigger: "+characterMessage);}
                return true;
            case "character.delete":
                if(!string.IsNullOrEmpty(c.s)&&c.s.StartsWith(CharacterPack.Folder)&&Directory.Exists(c.s)){
                    if(PlayerPrefs.GetString("traigger.character","")==c.s)PlayerPrefs.DeleteKey("traigger.character");
                    Directory.Delete(c.s,true);characterMessage="Personaje borrado";
                }
                return true;
            case "card.cover":PlayerPrefs.SetInt("traigger.cardCover",c.b?1:0);if(Cards&&Cards.Showing)ShowCard(Cards.current.kind);return true;
            case "card.titleMedia":{
                // «tipo\nruta»: título de ese cartel como imagen o vídeo (ruta vacía = volver al texto).
                var parts=(c.s??"").Split('\n');if(parts.Length<2)return true;
                PlayerPrefs.SetString("traigger.cardTitleMedia."+parts[0],parts[1]);
                if(Cards&&Cards.Showing&&Cards.current.kind==parts[0])ShowCard(parts[0]);
                return true;}
            case "card.image":PlayerPrefs.SetString("traigger.cardImage",c.s??"");if(Cards&&Cards.Showing)ShowCard(Cards.current.kind);return true;
            }
            return false;
        }
    }
}
