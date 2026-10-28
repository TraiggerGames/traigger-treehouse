using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;
namespace Traigger {
    // Canal entre Traigger (C#) y su interfaz nativa en SwiftUI. Mensajes en JSON: el estado sale de aquí unas 5 veces
    // por segundo (solo si cambia) y las órdenes llegan como {"cmd":…,"i","f","b","s"} (StudioController.Bridge.cs).
    //  · iPhone/iPad y Vision Pro: dentro de la app, con funciones nativas (Plugins/Apple/UI).
    //  · Editor de Unity y Traigger para Mac: por el puerto local 47820, al que se conecta «Traigger Control»
    //    (NativeUI/Control). Así la misma interfaz sirve para depurar en el editor.
    public class TraiggerBridge : MonoBehaviour {
        public StudioController studio;
        public const int Port=47820;
        public string status {get;private set;}="Sin interfaz conectada";
        static readonly ConcurrentQueue<string> commands=new ConcurrentQueue<string>();
        string lastState;float nextState,lastFull;

#if (UNITY_IOS||UNITY_VISIONOS)&&!UNITY_EDITOR
        delegate void CommandCallback(string json);
        [DllImport("__Internal")]static extern void TraiggerUIRegister(CommandCallback callback);
        [DllImport("__Internal")]static extern void TraiggerUIPushState(string json);
        [AOT.MonoPInvokeCallback(typeof(CommandCallback))]static void OnNativeCommand(string json){commands.Enqueue(json);}
        public static bool InProcess=>true;
        void Start(){TraiggerUIRegister(OnNativeCommand);status="Interfaz nativa";}
        void Publish(string json){TraiggerUIPushState(json);}
        void OnDestroy(){}
#else
        public static bool InProcess=>false;
        TcpListener listener;Thread acceptThread;readonly List<TcpClient> clients=new List<TcpClient>();volatile bool running;
        void Start(){
            try{
                listener=new TcpListener(IPAddress.Loopback,Port);listener.Start();running=true;
                acceptThread=new Thread(Accept){IsBackground=true,Name="Traigger Bridge"};acceptThread.Start();
                status=$"Esperando a Traigger Control (puerto {Port})";
            }catch(Exception e){status="No se pudo abrir el puerto del puente: "+e.Message;}
        }
        void Accept(){
            while(running){
                try{
                    var client=listener.AcceptTcpClient();client.NoDelay=true;
                    lock(clients)clients.Add(client);lastState=null;
                    new Thread(()=>Read(client)){IsBackground=true,Name="Traigger Bridge client"}.Start();
                }catch(Exception){if(!running)return;}
            }
        }
        void Read(TcpClient client){
            try{
                using var reader=new StreamReader(client.GetStream(),Encoding.UTF8);
                string line;while(running&&(line=reader.ReadLine())!=null)if(line.Length>0)commands.Enqueue(line);
            }catch(Exception){}
            lock(clients)clients.Remove(client);client.Close();
        }
        void Publish(string json){
            byte[] bytes=Encoding.UTF8.GetBytes(json+"\n");
            lock(clients){
                for(int i=clients.Count-1;i>=0;i--){
                    try{clients[i].GetStream().Write(bytes,0,bytes.Length);}catch(Exception){clients[i].Close();clients.RemoveAt(i);}
                }
                status=clients.Count>0?$"Traigger Control conectado ({clients.Count})":$"Esperando a Traigger Control (puerto {Port})";
            }
        }
        // App de Mac: el menú es Traigger Control (la misma interfaz SwiftUI que en el iPhone). Se abre sola al arrancar
        // y con Esc; va dentro de la app (Contents/Helpers) o, si no, la instalada en /Aplicaciones.
        float controlCheckAt=2;
        void MacControl(){
            if(Application.platform!=RuntimePlatform.OSXPlayer)return;
            bool connected;lock(clients)connected=clients.Count>0;
            if(Input.GetKeyDown(KeyCode.Escape))OpenControl();
            else if(controlCheckAt>0&&Time.unscaledTime>controlCheckAt){controlCheckAt=0;if(!connected)OpenControl();}
        }
        static void OpenControl(){
            try{
                string bundled=Path.Combine(Application.dataPath,"Helpers/Traigger Control.app");
                string args=Directory.Exists(bundled)?"\""+bundled+"\"":"-b com.traigger.control";
                System.Diagnostics.Process.Start("/usr/bin/open",args);
            }catch(Exception e){Debug.LogWarning("Traigger: no se pudo abrir Traigger Control: "+e.Message);}
        }
        void OnDestroy(){
            running=false;try{listener?.Stop();}catch(Exception){}
            lock(clients){foreach(var c in clients)c.Close();clients.Clear();}
        }
#endif
        void Update(){
            if(!studio)return;
#if !((UNITY_IOS||UNITY_VISIONOS)&&!UNITY_EDITOR)
            MacControl();
#endif
            while(commands.TryDequeue(out var json)){
                StudioController.BridgeCommand command=null;
                try{command=JsonUtility.FromJson<StudioController.BridgeCommand>(json);}catch(Exception){}
                if(command!=null&&!string.IsNullOrEmpty(command.cmd)){
                    try{studio.ApplyBridgeCommand(command);}catch(Exception e){Debug.LogException(e);}
                    nextState=0; // respuesta inmediata
                }
            }
            if(Time.unscaledTime<nextState)return;
            nextState=Time.unscaledTime+.2f;
            string state=JsonUtility.ToJson(studio.GetBridgeState());
            // Solo si cambia, y una vez por segundo de todos modos (por si la interfaz se acaba de conectar).
            if(state==lastState&&Time.unscaledTime-lastFull<1)return;
            lastState=state;lastFull=Time.unscaledTime;
            Publish(state);
        }
    }
}
