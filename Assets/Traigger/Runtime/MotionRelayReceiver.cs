using System;using System.Net;using System.Net.Sockets;using System.Threading;using UnityEngine;
namespace Traigger {
 // Canal distinto del iPhone facial: no compiten por el mismo puerto ni por el hilo de Unity.
 public class MotionRelayReceiver:MonoBehaviour {
  public int port=49984;public VisionMotionInput target;public string status="Apagado";
  UdpClient socket;Thread worker;volatile bool running;readonly object gate=new object();string pending,error;
  public int BoundPort=>socket==null?0:((IPEndPoint)socket.Client.LocalEndPoint).Port;
  void OnEnable(){StartListening();}
  public void StartListening(){if(running)return;try{socket=new UdpClient(new IPEndPoint(IPAddress.Any,port));socket.Client.ReceiveTimeout=200;running=true;worker=new Thread(Receive){IsBackground=true};worker.Start();status="Manos Vision Pro · UDP "+port;}catch(Exception e){status=e.Message;}}
  void Receive(){while(running)try{var sender=new IPEndPoint(IPAddress.Any,0);var data=socket.Receive(ref sender);if(data.Length>32768)continue;string json=System.Text.Encoding.UTF8.GetString(data);lock(gate)pending=json;}catch(SocketException e){if(running&&e.SocketErrorCode!=SocketError.TimedOut)lock(gate)error=e.Message;}catch(ObjectDisposedException){break;}}
  void Update(){ConsumePending();}
  // Se aplica siempre desde el hilo principal, también durante las pruebas de recepción.
  public bool ConsumePending(){string json;lock(gate){json=pending;pending=null;if(error!=null){status=error;error=null;}}if(json!=null&&target){target.OnNative(json);return true;}return false;}
  void OnDisable(){StopListening();}
  public void StopListening(){running=false;socket?.Close();worker?.Join(500);worker=null;socket=null;}
 }
}
