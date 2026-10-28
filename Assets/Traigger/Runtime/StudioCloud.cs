using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
namespace Traigger {
    // Escenas, carteles y diseño en iCloud: iPhone, iPad y Vision Pro comparten las mismas escenas. Todo va en un
    // solo valor del almacén clave-valor («traigger.studio», con la fecha del último cambio: gana el más nuevo).
    // Las imágenes y vídeos de carteles y fondos se copian a iCloud Drive (contenedor de la app) y en el valor se
    // guardan como «icloud://nombre»; cada aparato los descarga y usa su propia ruta. Necesita la compilación con
    // «Sincronizar con iCloud» activado (cuenta de desarrollador de pago); si no, todo sigue solo en el aparato.
    public static class StudioCloud {
        const string Key="traigger.studio",Stamp="traigger.studio.updated",Prefix="icloud://";
        static readonly string[] Keys={"traigger.scenes","traigger.cardCover","traigger.cardImage","traigger.cinemaLayout",
            "traigger.layer.movie","traigger.layer.capture","traigger.backgroundImage","traigger.backgroundSky",
            "traigger.card.brb","traigger.card.soon","traigger.card.pause","traigger.card.end","traigger.card.custom",
            "traigger.cardTitleMedia.brb","traigger.cardTitleMedia.soon","traigger.cardTitleMedia.pause","traigger.cardTitleMedia.end","traigger.cardTitleMedia.custom"};
        const string IntKeys="|traigger.cardCover|traigger.cinemaLayout|";
#if (UNITY_IOS||UNITY_VISIONOS)&&!UNITY_EDITOR
        [DllImport("__Internal")]static extern void TraiggerCloudSet(string key,string value);
        [DllImport("__Internal")]static extern IntPtr TraiggerCloudGet(string key);
        [DllImport("__Internal")]static extern bool TraiggerCloudAvailable();
        [DllImport("__Internal")]static extern IntPtr TraiggerCloudMediaPut(string path);
        [DllImport("__Internal")]static extern IntPtr TraiggerCloudMediaGet(string name);
#elif UNITY_STANDALONE_OSX&&!UNITY_EDITOR
        // Mac: dentro del plugin TraiggerMoviePlayer.bundle (MacCamera/MoviePlugin/build.sh).
        [DllImport("TraiggerMoviePlayer")]static extern void TraiggerCloudSet(string key,string value);
        [DllImport("TraiggerMoviePlayer")]static extern IntPtr TraiggerCloudGet(string key);
        [DllImport("TraiggerMoviePlayer")][return:MarshalAs(UnmanagedType.I1)]static extern bool TraiggerCloudAvailable();
        [DllImport("TraiggerMoviePlayer")]static extern IntPtr TraiggerCloudMediaPut(string path);
        [DllImport("TraiggerMoviePlayer")]static extern IntPtr TraiggerCloudMediaGet(string name);
#else
        static void TraiggerCloudSet(string key,string value){}static IntPtr TraiggerCloudGet(string key)=>IntPtr.Zero;static bool TraiggerCloudAvailable()=>false;
        static IntPtr TraiggerCloudMediaPut(string path)=>IntPtr.Zero;static IntPtr TraiggerCloudMediaGet(string name)=>IntPtr.Zero;
#endif
        [Serializable]class Entry{public string key,value;}
        [Serializable]class Blob{public long updated;public List<Entry> items=new List<Entry>();}
        public static string status="Escenas solo en este dispositivo";
        static string lastSnapshot;
        static readonly Dictionary<string,string> uploaded=new Dictionary<string,string>(); // ruta local → icloud://
        static readonly HashSet<string> pendingDownloads=new HashSet<string>();

        static string Take(IntPtr pointer){if(pointer==IntPtr.Zero)return null;var s=Marshal.PtrToStringUTF8(pointer);Marshal.FreeHGlobal(pointer);return s;}
        static string Read(string key)=>IntKeys.Contains("|"+key+"|")?(PlayerPrefs.HasKey(key)?PlayerPrefs.GetInt(key).ToString():null):(PlayerPrefs.HasKey(key)?PlayerPrefs.GetString(key):null);
        static void Write(string key,string value){
            if(value==null){PlayerPrefs.DeleteKey(key);return;}
            if(IntKeys.Contains("|"+key+"|")){if(int.TryParse(value,out var i))PlayerPrefs.SetInt(key,i);}else PlayerPrefs.SetString(key,value);
        }
        static string Snapshot(){var parts=new List<string>();foreach(var k in Keys)parts.Add(k+"="+Read(k));return string.Join("\n",parts);}

        // Al arrancar y al volver a la app: si iCloud tiene algo más nuevo, se aplica aquí. Devuelve si cambió algo.
        public static bool Pull(){
            if(!TraiggerCloudAvailable())return false;
            status="Escenas sincronizadas con iCloud";
            Blob blob=null;try{blob=JsonUtility.FromJson<Blob>(Take(TraiggerCloudGet(Key))??"");}catch(Exception){}
            if(blob==null)return false; // iCloud vacío: lo de aquí se sube en el siguiente PushIfChanged
            if(blob.updated<=long.Parse(PlayerPrefs.GetString(Stamp,"0"))){lastSnapshot??=Snapshot();return ResolveMedia();}
            var incoming=new Dictionary<string,string>();foreach(var e in blob.items)incoming[e.key]=e.value;
            foreach(var k in Keys)Write(k,incoming.TryGetValue(k,out var v)?Local(v):null);
            PlayerPrefs.SetString(Stamp,blob.updated.ToString());PlayerPrefs.Save();
            lastSnapshot=Snapshot();
            Debug.Log("Traigger: escenas recibidas de iCloud");
            return true;
        }
        // Cada pocos segundos: si algo cambió aquí, se sube (con sus imágenes y vídeos).
        public static void PushIfChanged(){
            if(!TraiggerCloudAvailable())return;
            string now=Snapshot();if(now==lastSnapshot)return;lastSnapshot=now;
            var blob=new Blob{updated=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()};
            foreach(var k in Keys){var v=Read(k);if(v!=null)blob.items.Add(new Entry{key=k,value=Cloud(v)});}
            TraiggerCloudSet(Key,JsonUtility.ToJson(blob));
            PlayerPrefs.SetString(Stamp,blob.updated.ToString());PlayerPrefs.Save();
            status="Escenas sincronizadas con iCloud";
        }

        // Rutas locales dentro de un valor (también dentro del JSON de escenas) → icloud://nombre.
        static string Cloud(string value){
            foreach(var path in Paths(value)){
                if(!uploaded.TryGetValue(path,out var cloud)){cloud=Take(TraiggerCloudMediaPut(path));if(cloud==null)continue;uploaded[path]=cloud;}
                value=value.Replace(path,cloud);
            }
            return value;
        }
        // icloud://nombre → ruta de este aparato (si aún se está descargando, se resuelve más tarde).
        static string Local(string value){
            int at=0;
            while((at=value.IndexOf(Prefix,at,StringComparison.Ordinal))>=0){
                int end=at+Prefix.Length;while(end<value.Length&&value[end]!='"'&&value[end]!='\n')end++;
                string name=value.Substring(at+Prefix.Length,end-at-Prefix.Length);
                string path=Take(TraiggerCloudMediaGet(name));
                if(path==null){pendingDownloads.Add(name);at=end;continue;}
                uploaded[path]=Prefix+name;
                value=value.Substring(0,at)+path+value.Substring(end);at+=path.Length;
            }
            return value;
        }
        // Vuelve a mirar los valores con icloud:// pendientes (descargas que han terminado).
        static bool ResolveMedia(){
            if(pendingDownloads.Count==0)return false;
            pendingDownloads.Clear();bool changed=false;
            foreach(var k in Keys){var v=Read(k);if(v==null||!v.Contains(Prefix))continue;var l=Local(v);if(l!=v){Write(k,l);changed=true;}}
            if(changed){PlayerPrefs.Save();lastSnapshot=Snapshot();}
            return changed;
        }
        // Archivos del aparato mencionados en un valor: valores que son una ruta, o rutas entre comillas en un JSON.
        static IEnumerable<string> Paths(string value){
            var found=new List<string>();
            if(value.StartsWith("/")&&!value.Contains("\n")&&File.Exists(value))found.Add(value);
            foreach(var line in value.Split('\n'))if(line.StartsWith("/")&&File.Exists(line)&&!found.Contains(line))found.Add(line);
            int at=0;
            while((at=value.IndexOf("\"/",at,StringComparison.Ordinal))>=0){
                int end=value.IndexOf('"',at+1);if(end<0)break;
                string p=value.Substring(at+1,end-at-1);
                if(File.Exists(p)&&!found.Contains(p))found.Add(p);
                at=end+1;
            }
            return found;
        }
    }
}
