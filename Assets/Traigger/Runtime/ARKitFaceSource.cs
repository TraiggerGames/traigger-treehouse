using System.Runtime.InteropServices;
using UnityEngine;
namespace Traigger {
    // Seguimiento facial con la cámara frontal del propio iPhone/iPad (ARKit, cámara TrueDepth de Face ID).
    // El plugin nativo (Plugins/iOS/TraiggerFaceTracking.mm) da la pose de la cabeza respecto a la pantalla y los
    // coeficientes de ARKit; aquí se convierten al mismo FaceFrame que llega por la red, así el resto no cambia.
    // En Mac y en el editor no hace nada: allí se usa FaceReceiver (UDP).
    public class ARKitFaceSource : MonoBehaviour {
        public FaceFrame latest {get;private set;}
        public float lastArrival {get;private set;}=-100;
        public string status {get;private set;}="Sólo en iPhone/iPad";
        [Tooltip("Como un espejo: si giras la cabeza a tu izquierda, el personaje la gira hacia la izquierda de la pantalla.")]
        public bool mirror=false;
        // Ajustes para calibrar en el dispositivo si algún eje sale al revés.
        public bool invertYaw,invertPitch,invertRoll,swapEyes;
        [Range(.5f,2f)]public float gazeGain=1.2f;
        readonly float[] values=new float[40];int sequence;
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int TraiggerFaceSupported();
        [DllImport("__Internal")] static extern int TraiggerFaceStart();
        [DllImport("__Internal")] static extern void TraiggerFaceStop();
        [DllImport("__Internal")] static extern int TraiggerFaceRead(float[] values,int count);
        [DllImport("__Internal")] static extern int TraiggerFaceInterrupted();
        [DllImport("__Internal")] static extern int TraiggerFaceSourceMode();
        [DllImport("__Internal")] static extern void TraiggerFaceDiagnostic(byte[] buffer,int count);
        readonly byte[] diagnostics=new byte[1024];
        string NativeDiagnostic(){TraiggerFaceDiagnostic(diagnostics,diagnostics.Length);int end=System.Array.IndexOf(diagnostics,(byte)0);return System.Text.Encoding.UTF8.GetString(diagnostics,0,end<0?diagnostics.Length:end);}
        void OnEnable(){
            if(TraiggerFaceSupported()==0){status="Este dispositivo no admite seguimiento facial (requiere cámara TrueDepth o chip A12)";enabled=false;return;}
            status=TraiggerFaceStart()!=0?"Buscando la cara…":"No se pudo iniciar la cámara frontal";
        }
        void OnDisable(){TraiggerFaceStop();}
        void OnApplicationPause(bool paused){if(paused)TraiggerFaceStop();else if(enabled)TraiggerFaceStart();}
        float diagAt;int diagReads;
        void Update(){
            int state=TraiggerFaceRead(values,values.Length);
            if(state>0)diagReads++;
            if(Time.unscaledTime-diagAt>3){Debug.Log($"Traigger: cara en Unity: {diagReads} lecturas en 3 s, estado «{status}», giro {values[0]:0}°, rastreada {values[16]:0}");diagAt=Time.unscaledTime;diagReads=0;}
            int sourceMode=TraiggerFaceSourceMode();
            bool approximate=sourceMode==2;
            bool hybrid=sourceMode==4;
            if(sourceMode==3){status="ARKit en pausa · prioridad vídeo USB (activa Priorizar expresiones completas para probar ambos)";return;}
            bool interrupted=TraiggerFaceInterrupted()!=0;
            if(state<0){status="Permiso de cámara denegado: actívalo en Ajustes";return;}
            if(state==0){if(Time.unscaledTime-lastArrival>.5f)status=NativeDiagnostic();return;}
            if(values[16]<.5f){status=hybrid?"MediaPipe híbrido · cara fuera de cámara":approximate?"Vision aproximado · cara fuera de cámara":"ARKit completo · cara fuera de cámara";return;}
            latest=Convert(values);lastArrival=Time.unscaledTime;
            status=hybrid?"MediaPipe híbrido · cara, ojos y parpadeos":approximate?"Seguimiento aproximado · Vision (sin todas las muecas)":interrupted?"ARKit interrumpido · "+NativeDiagnostic():"ARKit completo · siguiendo tu cara";
        }
#endif
        // Orden del búfer nativo: 0 giro, 1 arriba/abajo, 2 inclinación (grados, respecto a la pantalla),
        // 3 jawOpen, 4-5 eyeBlink izq/der, 6-7 mouthSmile izq/der, 8-11 eyeLook In/Out izq/der, 12-15 eyeLook Up/Down izq/der,
        // 16 cara visible, 17 mouthFunnel, 18 mouthPucker; 19–38: cejas, ojos, carrillos y muecas.
        // 39: tongueOut (lengua).
        // Los búferes antiguos siguen siendo válidos: los canales ausentes quedan a cero. Izquierda y derecha son las del usuario.
        public FaceFrame Convert(float[] v){
            if(v==null||v.Length<17)return null;
            float yaw=v[0],pitch=v[1],roll=v[2];
            // En espejo, girar a tu izquierda mira hacia la izquierda de la pantalla y la inclinación se refleja.
            if(mirror){yaw=-yaw;roll=-roll;}
            if(invertYaw)yaw=-yaw;if(invertPitch)pitch=-pitch;if(invertRoll)roll=-roll;
            float blinkL=v[4],blinkR=v[5];if(swapEyes){float t=blinkL;blinkL=blinkR;blinkR=t;}
            // Mirada horizontal hacia la izquierda del usuario: ojo izquierdo hacia fuera y ojo derecho hacia dentro.
            float lookUserLeft=((v[9]+v[10])-(v[8]+v[11]))*.5f,lookUp=((v[12]+v[13])-(v[14]+v[15]))*.5f;
            float gazeX=(mirror?-lookUserLeft:lookUserLeft)*gazeGain;
            float sign=mirror?-1:1;
            var gazeLeft=new Vector2(sign*(v[9]-v[8]),v[12]-v[14])*gazeGain;
            var gazeRight=new Vector2(sign*(v[10]-v[11]),v[13]-v[15])*gazeGain;
            if(swapEyes){var temp=gazeLeft;gazeLeft=gazeRight;gazeRight=temp;}
            return new FaceFrame{version=1,sequence=++sequence,headYaw=Mathf.Clamp(yaw,-60,60),headPitch=Mathf.Clamp(pitch,-45,45),headRoll=Mathf.Clamp(roll,-45,45),
                independentEyes=true,eyeLookLeftX=Mathf.Clamp(gazeLeft.x,-1,1),eyeLookLeftY=Mathf.Clamp(gazeLeft.y,-1,1),eyeLookRightX=Mathf.Clamp(gazeRight.x,-1,1),eyeLookRightY=Mathf.Clamp(gazeRight.y,-1,1),
                jawOpen=v[3],eyeBlinkLeft=blinkL,eyeBlinkRight=blinkR,mouthSmileLeft=v[6],mouthSmileRight=v[7],
                eyeLookX=Mathf.Clamp(gazeX,-1,1),eyeLookY=Mathf.Clamp(lookUp*gazeGain,-1,1),
                mouthFunnel=v.Length>18?v[17]:0,mouthPucker=v.Length>18?v[18]:0,
                browInnerUp=v.Length>19?Mathf.Clamp01(v[19]):0,
                browDownLeft=v.Length>20?Mathf.Clamp01(v[20]):0,
                browDownRight=v.Length>21?Mathf.Clamp01(v[21]):0,
                browOuterUpLeft=v.Length>22?Mathf.Clamp01(v[22]):0,
                browOuterUpRight=v.Length>23?Mathf.Clamp01(v[23]):0,
                eyeWideLeft=v.Length>24?Mathf.Clamp01(v[24]):0,
                eyeWideRight=v.Length>25?Mathf.Clamp01(v[25]):0,
                eyeSquintLeft=v.Length>26?Mathf.Clamp01(v[26]):0,
                eyeSquintRight=v.Length>27?Mathf.Clamp01(v[27]):0,
                cheekPuff=v.Length>28?Mathf.Clamp01(v[28]):0,
                cheekSquintLeft=v.Length>29?Mathf.Clamp01(v[29]):0,
                cheekSquintRight=v.Length>30?Mathf.Clamp01(v[30]):0,
                mouthFrownLeft=v.Length>31?Mathf.Clamp01(v[31]):0,
                mouthFrownRight=v.Length>32?Mathf.Clamp01(v[32]):0,
                mouthLeft=v.Length>33?Mathf.Clamp01(v[33]):0,
                mouthRight=v.Length>34?Mathf.Clamp01(v[34]):0,
                mouthPressLeft=v.Length>35?Mathf.Clamp01(v[35]):0,
                mouthPressRight=v.Length>36?Mathf.Clamp01(v[36]):0,
                noseSneerLeft=v.Length>37?Mathf.Clamp01(v[37]):0,
                noseSneerRight=v.Length>38?Mathf.Clamp01(v[38]):0,
                tongueOut=v.Length>39?Mathf.Clamp01(v[39]):0};
        }
    }
}
