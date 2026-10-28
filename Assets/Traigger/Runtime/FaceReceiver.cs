using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
namespace Traigger {
    [Serializable] public class FaceFrame {
        public int version=1, sequence; public float headRoll,headYaw,headPitch;
        public float eyeBlinkLeft,eyeBlinkRight,jawOpen,mouthSmileLeft,mouthSmileRight,eyeLookX,eyeLookY;
        public float browInnerUp,browDownLeft,browDownRight,browOuterUpLeft,browOuterUpRight,eyeWideLeft,eyeWideRight,eyeSquintLeft,eyeSquintRight,cheekPuff,cheekSquintLeft,cheekSquintRight,mouthFrownLeft,mouthFrownRight,mouthLeft,mouthRight,mouthPressLeft,mouthPressRight,noseSneerLeft,noseSneerRight;
        public bool independentEyes;
        public float eyeLookLeftX,eyeLookLeftY,eyeLookRightX,eyeLookRightY;
        public float tongueOut;
        public float mouthFunnel,mouthPucker; // labios en «o» / «u» (opcionales; 0 si no llegan)
    }
    // El hilo de red no toca objetos Unity. Update consume sólo el último datagrama.
    public class FaceReceiver : MonoBehaviour {
        public int port=49983; public bool listenOnLAN=false;
        public FaceFrame latest {get;private set;} public float lastArrival {get;private set;}=-100;
        public string status {get;private set;}="Desactivado";
        UdpClient socket; Thread worker; volatile bool running; readonly object gate=new object(); string pending;
        string networkError;
        void OnEnable() {
            try {
                socket=new UdpClient(new IPEndPoint(listenOnLAN?IPAddress.Any:IPAddress.Loopback,port));
                socket.Client.ReceiveTimeout=250; running=true; status="Escuchando UDP "+port;
                worker=new Thread(Receive){IsBackground=true}; worker.Start();
            } catch(Exception e){status=e.Message;}
        }
        void Receive() {
            while(running) try {
                var endpoint=new IPEndPoint(IPAddress.Any,0); byte[] bytes=socket.Receive(ref endpoint);
                if(bytes.Length>8192)continue;
                string value=System.Text.Encoding.UTF8.GetString(bytes);
                lock(gate) pending=value;
            }catch(SocketException e){if(e.SocketErrorCode!=SocketError.TimedOut&&running){lock(gate) networkError=e.Message;}}
            catch(ObjectDisposedException){break;}
        }
        void Update() {
            string json; lock(gate){json=pending;pending=null;if(networkError!=null){status=networkError;networkError=null;}}
            if(json==null)return;
            try {
                var f=JsonUtility.FromJson<FaceFrame>(json);
                if(f==null||f.version!=1||!Finite(f))return;
                f.headRoll=Mathf.Clamp(f.headRoll,-45,45);f.headYaw=Mathf.Clamp(f.headYaw,-60,60);f.headPitch=Mathf.Clamp(f.headPitch,-45,45);
                latest=f; lastArrival=Time.unscaledTime;
            }catch(ArgumentException){status="Paquete JSON rechazado";}
        }
        public static bool Finite(FaceFrame f) {
            float[] values={f.headRoll,f.headYaw,f.headPitch,f.eyeBlinkLeft,f.eyeBlinkRight,f.jawOpen,f.mouthSmileLeft,f.mouthSmileRight,f.eyeLookX,f.eyeLookY,f.eyeLookLeftX,f.eyeLookLeftY,f.eyeLookRightX,f.eyeLookRightY,f.mouthFunnel,f.mouthPucker,f.tongueOut,f.browInnerUp,f.browDownLeft,f.browDownRight,f.browOuterUpLeft,f.browOuterUpRight,f.eyeWideLeft,f.eyeWideRight,f.eyeSquintLeft,f.eyeSquintRight,f.cheekPuff,f.cheekSquintLeft,f.cheekSquintRight,f.mouthFrownLeft,f.mouthFrownRight,f.mouthLeft,f.mouthRight,f.mouthPressLeft,f.mouthPressRight,f.noseSneerLeft,f.noseSneerRight};
            foreach(float v in values)if(float.IsNaN(v)||float.IsInfinity(v))return false;return true;
        }
        void OnDisable(){running=false;socket?.Close();worker?.Join(600);socket=null;worker=null;}
    }
}
