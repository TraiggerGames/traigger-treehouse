using System;
using System.Runtime.InteropServices;
using UnityEngine;
namespace Traigger {
    // Guarda el perfil de voz en el dispositivo y, en iPhone, iPad y Vision Pro, también en iCloud (almacén
    // clave-valor de Apple): el que se calibró más recientemente gana, así que basta con calibrar en un aparato.
    // iCloud solo funciona si la compilación se hizo con «Sincronizar perfil de voz con iCloud» activado.
    public static class VoiceProfileStore {
        const string Key="traigger.voiceProfile";
#if (UNITY_IOS||UNITY_VISIONOS)&&!UNITY_EDITOR
        [DllImport("__Internal")]static extern void TraiggerCloudSet(string key,string value);
        [DllImport("__Internal")]static extern IntPtr TraiggerCloudGet(string key);
        [DllImport("__Internal")]static extern bool TraiggerCloudAvailable();
#elif UNITY_STANDALONE_OSX&&!UNITY_EDITOR
        // Mac: dentro del plugin TraiggerMoviePlayer.bundle (MacCamera/MoviePlugin/build.sh).
        [DllImport("TraiggerMoviePlayer")]static extern void TraiggerCloudSet(string key,string value);
        [DllImport("TraiggerMoviePlayer")]static extern IntPtr TraiggerCloudGet(string key);
        [DllImport("TraiggerMoviePlayer")][return:MarshalAs(UnmanagedType.I1)]static extern bool TraiggerCloudAvailable();
#else
        static void TraiggerCloudSet(string key,string value){}static IntPtr TraiggerCloudGet(string key)=>IntPtr.Zero;static bool TraiggerCloudAvailable()=>false;
#endif
        public static string status="Perfil solo en este dispositivo";
        static VoiceProfile Parse(string json){
            if(string.IsNullOrEmpty(json))return null;
            try{var p=JsonUtility.FromJson<VoiceProfile>(json);
                return p!=null&&p.centroids!=null&&p.centroids.Length==VoiceEmotion.Count*VoiceEmotion.Features&&p.samples!=null&&p.samples.Length==VoiceEmotion.Count?p:null;}
            catch{return null;}
        }
        public static VoiceProfile Load(){
            var local=Parse(PlayerPrefs.GetString(Key,""));
            VoiceProfile cloud=null;
            if(TraiggerCloudAvailable()){
                var pointer=TraiggerCloudGet(Key);
                if(pointer!=IntPtr.Zero){cloud=Parse(Marshal.PtrToStringUTF8(pointer));Marshal.FreeHGlobal(pointer);}
                status="Perfil sincronizado con iCloud";
            }
            if(cloud!=null&&(local==null||cloud.updated>local.updated))return cloud;
            return local??new VoiceProfile();
        }
        public static void Save(VoiceProfile profile){
            string json=JsonUtility.ToJson(profile);
            PlayerPrefs.SetString(Key,json);PlayerPrefs.Save();
            if(TraiggerCloudAvailable()){TraiggerCloudSet(Key,json);status="Perfil sincronizado con iCloud";}
        }
    }
}
