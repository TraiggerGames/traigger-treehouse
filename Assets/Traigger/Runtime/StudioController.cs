using UnityEngine;
namespace Traigger {
    public partial class StudioController : MonoBehaviour {
        public FacialExpressionRig facialExpressions;
        public LayeredEyeRig layeredEyes;
        public HairDetailRig hairDetails;
        public AvatarFraming framing;
        public Rect AvatarViewport {get{
            if(!avatarCamera||avatarCamera.targetDisplay!=0||!showPanel||cleanOutput)return new Rect(0,0,1,1);
            bool touch=Application.isMobilePlatform||Input.touchSupported||Application.platform==RuntimePlatform.VisionOS;
            float scale=Application.isMobilePlatform?Mathf.Clamp(Screen.dpi/200f,1,2.5f):1;
            float panelWidth=Mathf.Min(touch?400:340,Screen.width/scale-24);
            float left=Mathf.Min(.65f,(panelWidth+24)*scale/Mathf.Max(1,Screen.width));return new Rect(left,0,1-left,1);
        }}
        public AvatarRig rig; public FaceReceiver receiver;
        public struct PoseState{public float roll,yaw,pitch,chest,hips,armLeft,armRight,mouth,breathe,blinkLeft,blinkRight,smile,mouthShape;}
        public PoseState Pose{get;private set;}
        public ARKitFaceSource deviceFace;      // seguimiento facial del propio iPhone/iPad (ARKit)
#if UNITY_IOS&&!UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void TraiggerFaceRelayStart();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int TraiggerFaceRelayEnabled();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void TraiggerFaceRelaySend(byte[] bytes,int count);
        bool relayStarted;float relaySentAt=-1;
#endif
        // Con «Enviar mi cara» (TraiggerFaceRelay.swift) cada fotograma nuevo de ARKit va también a otro aparato.
        void RelayFace(){
#if UNITY_IOS&&!UNITY_EDITOR
            if(!relayStarted){relayStarted=true;TraiggerFaceRelayStart();}
            if(!deviceFace||deviceFace.latest==null||deviceFace.lastArrival==relaySentAt||TraiggerFaceRelayEnabled()==0)return;
            relaySentAt=deviceFace.lastArrival;
            var bytes=System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(deviceFace.latest));
            TraiggerFaceRelaySend(bytes,bytes.Length);
#endif
        }
        public TouchManipulator manipulator;
        public MicMouth mic;
        public VisionMotionInput vision;
        public bool naturalSpeech=true;          // animación a partir de la voz cuando no hay cámara de cara
        readonly SpeechAnimator speech=new SpeechAnimator();float speechWeight;        // cabeza y manos de Vision Pro                    // boca con la voz    // arrastrar y pellizcar al personaje
        Vector3 rootScale,footLocalL,footLocalR;float uiScale=1;Rect panelRect;
        public bool demo=true, tracking=true, showPanel=true, autoBlink=true;
        public bool cleanOutput;
        [Range(-20,20)]public float head;
        [Range(-18,18)]public float turn;
        [Range(-12,12)]public float nod;
        [Range(-1,1)]public float lateral;
        [Range(0,2)]public float lateralAmount=1.3f;
        [Range(-135,135)]public float armLeft,armRight;
        [Range(-100,100)]public float legLeft,legRight;
        [Range(-15,15)]public float torso,hips,neck;
        [Range(-35,35)]public float tail,earLeft,earRight;
        [Range(0,1.5f)]public float tailWag=.8f;      // amplitud del meneo propio de la cola
        [Range(.3f,2.5f)]public float tailSpeed=1f;   // velocidad del meneo
        [Range(0,1)]public float mouth,blinkLeft,blinkRight,smile;
        [Range(0,2)]public float physics=.65f;
        [Range(.08f,.5f)]public float response=.20f;
        [Range(.2f,2.5f)]public float hairFreedom=1.5f;
        [Range(.2f,2)] public float earFlexibility=1f;
        [Range(0,1.3f)] public float earArc=1f;
        [Range(-1,1)] public float earMood; // -1: bajas y abiertas; +1: erguidas y atentas.
        [Range(.01f,.2f)] public float mouthSmoothing=.035f; // más alto = boca más suave pero con más retraso
        // Expresiones con un botón: mueven sólo lo que la cámara no ve (orejas, cola, torso, brazos, sonrisa),
        // con una transición suave. La cara en vivo sigue mandando sobre cabeza, ojos y boca.
        struct Expression {public string name;public float earMood,earLeft,earRight,tail,tailWag,smile,torso,armLeft,armRight,head;}
        static readonly Expression[] expressions={
            new Expression{name="Neutro",tailWag=.8f},
            new Expression{name="Contento",earMood=1,earLeft=-10,earRight=10,tail=20,tailWag=1.5f,smile=1,torso=-3,armLeft=-18,armRight=18,head=5},
            new Expression{name="Triste",earMood=-1,earLeft=8,earRight=-8,tail=-35,tailWag=0,torso=8,armLeft=6,armRight=-6,head=-12},
            new Expression{name="Enfado",earMood=-.5f,earLeft=25,earRight=-25,tail=30,tailWag=.15f,torso=-6,armLeft=-12,armRight=12,head=-4},
            new Expression{name="Sorpresa",earMood=1,earLeft=-18,earRight=18,tail=-20,tailWag=0,torso=-5,armLeft=-30,armRight=30,head=8},
            new Expression{name="Curioso",earMood=.6f,earLeft=-20,earRight=30,tail=12,tailWag=.7f,armLeft=-8,head=14},
            new Expression{name="Vergüenza",earMood=-.8f,earLeft=10,earRight=-10,tail=-28,tailWag=.4f,smile=.5f,torso=6,armLeft=10,armRight=-10,head=-12},
            new Expression{name="Saludo",earMood=.8f,earLeft=-12,earRight=12,tail=15,tailWag=1.5f,smile=1,armLeft=-60,head=6}};
        // Cámara del iPhone/iPad: 0 frontal · cara (ARKit Face ID); 1 frontal · cuerpo; 2 trasera · cuerpo;
        // 3 ultra gran angular · cuerpo; 4 cuerpo 3D (ARKit con la trasera y LiDAR). En los modos de cuerpo el
        // personaje se mueve en el propio aparato y, si hay unas Vision Pro en la misma Wi-Fi, también allí.
        static readonly string[] CameraModeNames={"Frontal · cara","Frontal · cuerpo","Trasera · cuerpo","Ultra gran angular · cuerpo","Cuerpo 3D (ARKit)"};
        int cameraMode=-1,cameraModes=-1;bool saveBattery;
        [Tooltip("Si al abrir las piernas el personaje las cierra (o dobla la rodilla al revés), actívalo.")]public bool invertLegs;
#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void TraiggerBodySensorStartMode(int mode);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void TraiggerBodySensorStop();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int TraiggerBodySensorAvailable();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern string TraiggerBodySensorStatus();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern System.IntPtr TraiggerBodySensorPoll();
#else
        static void TraiggerBodySensorStartMode(int mode){}static void TraiggerBodySensorStop(){}static int TraiggerBodySensorAvailable()=>0;
        static string TraiggerBodySensorStatus()=>"sólo en iPhone/iPad";static System.IntPtr TraiggerBodySensorPoll()=>System.IntPtr.Zero;
#endif
        static string BodySensorStatus()=>TraiggerBodySensorStatus();
        bool CameraModeAvailable(int m){
            if(m==0)return deviceFace;
            if(cameraModes<0)cameraModes=TraiggerBodySensorAvailable();
            return (cameraModes&(1<<m))!=0;
        }
        bool HasLiDAR=>cameraModes>=0&&(cameraModes&(1<<8))!=0;
        void SetCameraMode(int m){
            cameraMode=m;PlayerPrefs.SetInt("traigger.cameraMode",m);
            // Una cámara no puede usarse a la vez para la cara (ARKit) y para el cuerpo.
            if(deviceFace)deviceFace.enabled=m==0;
            if(m>0&&!vision){var input=new GameObject("TraiggerVisionInput");vision=input.AddComponent<VisionMotionInput>();}
            if(avatarCamera)avatarCamera.enabled=!(m>0&&saveBattery);
            Screen.sleepTimeout=SleepTimeout.NeverSleep;
            if(m>0)TraiggerBodySensorStartMode(m);else TraiggerBodySensorStop();
        }
        // Los fotogramas del cuerpo entran por el mismo lector que los de Vision Pro.
        void ReadBodySensor(){
            if(cameraMode<=0||!vision)return;
            var pointer=TraiggerBodySensorPoll();if(pointer==System.IntPtr.Zero)return;
            string json=System.Runtime.InteropServices.Marshal.PtrToStringUTF8(pointer);System.Runtime.InteropServices.Marshal.FreeHGlobal(pointer);
            vision.OnNative(json);
        }
        readonly Channel mouthShapeChannel=new Channel();float previousPitch;
        [Range(0,2)]public float earFeedback=1;   // cuánto se retrasa y rebota el cartílago de las orejas
        // Emoción por la entonación de la voz: se suma suave a la pose; los botones de expresión mandan sobre ella.
        public bool voiceEmotion=true;[Range(0,1.5f)]public float voiceEmotionStrength=1;
        readonly VoiceEmotion emotion=new VoiceEmotion();Expression emotionOffset;
        // Olfateo de gato: lo reconoce por el sonido o por el ritmo de la nariz (SniffDetector.cs).
        readonly SniffDetector sniff=new SniffDetector();float faceDiagAt;
        // Idioma de las frases (Vision Pro, iPhone/iPad). Automático = el del sistema si es español o inglés.
        static readonly string[] SpeechLanguageNames={"Auto","Español","English"},SpeechLanguageCodes={"auto","es-ES","en-US"};
        int speechLanguage=-1;
        void ApplySpeechLanguage(){
            string code=SpeechLanguageCodes[speechLanguage];PlayerPrefs.SetInt("traigger.speechLanguage",speechLanguage);
            if(vision)vision.SetSpeechLanguage(code);if(mic)mic.SetSpeechLanguage(code);
        }
        void ApplyEmotion(float sign){
            var e=emotionOffset;
            earMood+=e.earMood*sign;earLeft+=e.earLeft*sign;earRight+=e.earRight*sign;tail+=e.tail*sign;tailWag+=e.tailWag*sign;
            smile+=e.smile*sign;torso+=e.torso*sign;armLeft+=e.armLeft*sign;armRight+=e.armRight*sign;
        }
        // La IA rellena lo que el hardware no capta: orejas, cola, brazos, postura y gestos. Las palabras dicen qué
        // emoción es; la entonación (o tu perfil de voz calibrado), cuánta. Lo que sí siguen los sensores (cara,
        // manos) manda sobre ello: los brazos solo reciben una pizca si ya los mueve un sensor.
        float[] emotionWeights=new float[8];float emotionPitch,emotionYaw,manualExpressionTime=-100,profileSaveTime;
        //                                        Neutro Contento Triste Enfado Sorpresa Curioso Vergüenza Saludo
        static readonly float[] EmotionNod=      {0,     4,       -11,   -4,    7,       3,      -9,       2};
        static readonly float[] EmotionTurn=     {0,     0,       -4,    0,     0,       6,      -14,      0};
        void UpdateEmotion(float open,float pitchHz,float zcrNow,float lowNow,float dt,bool armsTracked){
            emotion.Step(open,pitchHz,zcrNow,lowNow,dt);
            if(emotion.dirty&&(emotion.saveNow||Time.unscaledTime-profileSaveTime>10)){emotion.dirty=emotion.saveNow=false;profileSaveTime=Time.unscaledTime;VoiceProfileStore.Save(emotion.profile);}
            // Un botón de expresión manda 6 s; después la voz vuelve a sumar poco a poco.
            float manualAge=Time.unscaledTime-manualExpressionTime;
            float w=voiceEmotion?voiceEmotionStrength*Mathf.Clamp01((manualAge-6)/2):0;
            float wordTarget=0;int wordIndex=-1;
            // Vision Pro (host nativo) o iPhone/iPad (plugin del micrófono).
            bool fromVision=vision&&vision.wordEmotion!=null&&Application.platform==RuntimePlatform.VisionOS;
            string word=fromVision?vision.wordEmotion:mic?mic.wordEmotion:null;
            float wordLevel=fromVision?vision.wordEmotionLevel:mic?mic.wordEmotionLevel:0;
            float wordTime=fromVision?vision.wordEmotionTime:mic?mic.wordEmotionTime:-100;
            string cue=fromVision?vision.emotionCue:mic?mic.emotionCue:null;
            // Las frases duran 4 s; los sonidos (risa, llanto…), 2 s.
            float hold=string.IsNullOrEmpty(cue)?4:2;
            float voiceIntensity=0;for(int i=1;i<VoiceEmotion.Count;i++)voiceIntensity=Mathf.Max(voiceIntensity,emotion.scores[i]);
            if(word!=null){
                float age=Time.unscaledTime-wordTime;
                wordIndex=System.Array.FindIndex(expressions,x=>x.name==word);
                // Una voz animada refuerza la emoción de las palabras.
                if(wordIndex>0)wordTarget=Mathf.Clamp01(wordLevel*(1-Mathf.Clamp01((age-hold)/2))*(.8f+.5f*voiceIntensity));
            }
            for(int i=1;i<emotionWeights.Length;i++){
                float target=i==wordIndex?wordTarget:0;
                if(i<VoiceEmotion.Count)target=Mathf.Max(target,emotion.scores[i]*(1-wordTarget));
                emotionWeights[i]=Mathf.Lerp(emotionWeights[i],target,1-Mathf.Exp(-dt/(target>emotionWeights[i]?.3f:1.2f)));
            }
            var e=new Expression();Expression neutral=expressions[0];float t=Time.time;
            float arms=armsTracked?.3f:1;emotionPitch=emotionYaw=0;
            for(int i=1;i<expressions.Length;i++){
                float k=emotionWeights[i]*w;if(k<=.01f)continue;var x=expressions[i];
                e.earMood+=x.earMood*k;e.earLeft+=x.earLeft*k;e.earRight+=x.earRight*k;e.tail+=x.tail*k;e.tailWag+=(x.tailWag-neutral.tailWag)*k;
                e.smile+=x.smile*k;e.torso+=x.torso*k;e.armLeft+=x.armLeft*k*arms;e.armRight+=x.armRight*k*arms;e.head+=x.head*k;
                emotionPitch+=EmotionNod[i]*k;emotionYaw+=EmotionTurn[i]*k;
            }
            // Gestos vivos, no solo una pose: botes de alegría, temblor de enfado, suspiro triste, respingo de sorpresa.
            float happy=emotionWeights[1]*w,sad=emotionWeights[2]*w,angry=emotionWeights[3]*w,surprise=emotionWeights[4]*w,shy=emotionWeights[6]*w;
            float bounce=Mathf.Sin(t*6.5f);
            e.earMood+=happy*.35f*bounce+surprise*.4f;
            e.armLeft+=(-happy*7*bounce+angry*6*Noise(t*7,401))*arms;e.armRight+=(happy*7*bounce-angry*6*Noise(t*7,402))*arms;
            e.torso+=angry*1.8f*Noise(t*8,403)-happy*1.5f*bounce;
            e.tailWag+=happy*.9f-sad*.6f;
            emotionPitch+=sad*2.5f*Mathf.Sin(t*.9f)+happy*1.5f*bounce;
            emotionYaw+=shy*4*Mathf.Sin(t*.7f);
            emotionOffset=e;
        }
        string EmotionLabel(){
            int best=0;float bestWeight=.15f;
            for(int i=1;i<expressions.Length;i++)if(emotionWeights[i]>bestWeight){best=i;bestWeight=emotionWeights[i];}
            return best==0?emotion.status:expressions[best].name;
        }
        int expressionIndex=-1;float expressionBlend=1;Expression expressionFrom,expressionTo;
        void SetExpression(int i){manualExpressionTime=Time.unscaledTime;expressionFrom=CurrentExpression();expressionTo=expressions[i];expressionIndex=i;expressionBlend=0;}
        Expression CurrentExpression()=>new Expression{earMood=earMood,earLeft=earLeft,earRight=earRight,tail=tail,tailWag=tailWag,smile=smile,torso=torso,armLeft=armLeft,armRight=armRight,head=head};
        void BlendExpression(float dt){
            if(expressionBlend>=1)return;
            expressionBlend=Mathf.Min(1,expressionBlend+dt/.45f);float k=Mathf.SmoothStep(0,1,expressionBlend);
            Expression a=expressionFrom,b=expressionTo;
            earMood=Mathf.Lerp(a.earMood,b.earMood,k);earLeft=Mathf.Lerp(a.earLeft,b.earLeft,k);earRight=Mathf.Lerp(a.earRight,b.earRight,k);
            tail=Mathf.Lerp(a.tail,b.tail,k);tailWag=Mathf.Lerp(a.tailWag,b.tailWag,k);smile=Mathf.Lerp(a.smile,b.smile,k);
            torso=Mathf.Lerp(a.torso,b.torso,k);armLeft=Mathf.Lerp(a.armLeft,b.armLeft,k);armRight=Mathf.Lerp(a.armRight,b.armRight,k);head=Mathf.Lerp(a.head,b.head,k);
        }
        [Range(-25,25)]public float shoulderLeft,shoulderRight;
        [Range(-110,110)]public float upperArmLeft,upperArmRight;
        [Range(-85,85)]public float wristLeft,wristRight;
        [Range(-65,65)]public float thighLeft,thighRight;
        [Range(-60,60)]public float ankleLeft,ankleRight;
        [Range(-35,35)]public float toesLeft,toesRight;
        [Range(0,.35f)]public float volume=0;
        public Camera avatarCamera;
        BodyRigUpgrade body;
        KemonoBodyRig segmentedBody;
        public bool guideHandLeft,guideHandRight;
        public Vector2 handTargetLeft=new Vector2(-.95f,2.20f),handTargetRight=new Vector2(.95f,2.20f);
        readonly Channel handXL=new Channel(),handYL=new Channel(),handXR=new Channel(),handYR=new Channel();
        Transform armRootL,elbowBoneL,handBoneL,armRootR,elbowBoneR,handBoneR;
        readonly Channel shoulderL=new Channel(),shoulderR=new Channel(),upperL=new Channel(),upperR=new Channel(),wristL=new Channel(),wristR=new Channel();
        readonly Channel thighPoseL=new Channel(),thighPoseR=new Channel(),ankleL=new Channel(),ankleR=new Channel(),toesL=new Channel(),toesR=new Channel();
        EarFlex cartilageR,cartilageL;
        Transform earBoneR,earBoneL;Vector3 earRestR,earRestL;
        readonly Channel earBaseR=new Channel(),earBaseL=new Channel();
        // Cada canal conserva velocidad: los cambios de objetivo no producen saltos de pose.
        class Channel {
            public float value,velocity;
            public float Step(float target,float time,float dt){value=Mathf.SmoothDamp(value,target,ref velocity,time,Mathf.Infinity,dt);return value;}
        }
        // Resorte amortiguado con pequeños subpasos: rebote suave sin inestabilidad por fotogramas largos.
        class Strand {
            float position,velocity;
            public float Step(float target,float stiffness,float damping,float dt){
                int count=Mathf.Max(1,Mathf.CeilToInt(dt/.008f));float h=dt/count;
                for(int i=0;i<count;i++){velocity+=(stiffness*(target-position)-damping*velocity)*h;velocity=Mathf.Clamp(velocity,-12,12);position=Mathf.Clamp(position+velocity*h,-1.15f,1.15f);}
                return position;
            }
        }
        readonly Strand strandL=new Strand(),strandR=new Strand(),strandB=new Strand();
        readonly Channel side=new Channel(),neckTilt=new Channel();
        Vector3 rootRest,hipRest,leftFootGoal,rightFootGoal;Transform hipBone,thighL,kneeL,footL,thighR,kneeR,footR;
        float previousSide;
        readonly Channel roll=new Channel(),yaw=new Channel(),pitch=new Channel(),jaw=new Channel(),bl=new Channel(),br=new Channel();
        readonly Channel chest=new Channel(),hip=new Channel(),leftArm=new Channel(),rightArm=new Channel(),leftLeg=new Channel(),rightLeg=new Channel();
        readonly Channel hair=new Channel();
        Transform leftEye,rightEye,chestBone,headBone;Vector3 eyeL,eyeR,chestRest,headRest;float neckStretch=.3f;
        float nextBlink=2.4f,blinkStart=-100,previousRoll,previousYaw,blinkInterval=4.1f;
        Vector2 panelScroll,gazeSmooth;
        // Mac: cámara virtual «Traigger» para FaceTime y demás videollamadas (VirtualCameraOutput, MacCamera).
        VirtualCameraOutput virtualCamera;
        // Cine: películas de fondo con libVLC (MoviePlayer).
        MoviePlayer movie;
        TraiggerBridge bridge;
        void Start(){
            StudioCloud.Pull(); // escenas y carteles de iCloud antes de usarlos
            if(MoviePlayer.Supported&&avatarCamera){movie=gameObject.AddComponent<MoviePlayer>();movie.target=avatarCamera;}
            ApplyLayerRects(); // posición de la película y la capturadora guardada en el modo layout
            cinemaLayout=PlayerPrefs.GetInt("traigger.cinemaLayout",1)==1;
            // Interfaz nativa (SwiftUI): en los dispositivos sustituye al panel de depuración, que sigue a mano con Tab.
            bridge=gameObject.AddComponent<TraiggerBridge>();bridge.studio=this;
            if(avatarCamera){var background=gameObject.AddComponent<BackgroundImage>();background.target=avatarCamera;}
            // Vision Pro: fondo transparente, para poner a Traigger encima de la película, la capturadora o el navegador.
            if(avatarCamera&&Application.platform==RuntimePlatform.VisionOS){var c=avatarCamera.backgroundColor;c.a=0;avatarCamera.backgroundColor=c;avatarCamera.clearFlags=CameraClearFlags.SolidColor;}
            if(avatarCamera){var capture=gameObject.AddComponent<CaptureBackground>();capture.target=avatarCamera;}
            VisionBackground.SetImage(PlayerPrefs.GetString("traigger.backgroundImage",""));
            if(Application.platform==RuntimePlatform.VisionOS&&PlayerPrefs.GetString("traigger.backgroundSky","")!="")VisionBackground.SetSky(PlayerPrefs.GetString("traigger.backgroundSky",""));
            // Fuera del editor de Unity no hay panel OnGUI: solo la interfaz nativa.
            if(!Application.isEditor)showPanel=false;
            if(VirtualCameraOutput.Supported&&avatarCamera){
                virtualCamera=gameObject.AddComponent<VirtualCameraOutput>();virtualCamera.source=avatarCamera;
                virtualCamera.active=PlayerPrefs.GetInt("traigger.virtualCamera",0)==1;
            }
            // Emisión en directo: iPhone, iPad, Mac y Vision Pro.
            if(StreamOutput.Supported&&avatarCamera){stream=gameObject.AddComponent<StreamOutput>();stream.source=avatarCamera;}
            emotion.profile=VoiceProfileStore.Load();
            if(!rig)rig=FindAnyObjectByType<AvatarRig>();
            if(!rig)return;rig.CaptureRest();rootRest=rig.transform.localPosition;
            foreach(var b in rig.bones){
                if(b.name=="Hips")hipBone=b;if(b.name=="RightUpperLeg")thighL=b;if(b.name=="RightLowerLeg")kneeL=b;if(b.name=="RightFoot")footL=b;
                if(b.name=="LeftUpperLeg")thighR=b;if(b.name=="LeftLowerLeg")kneeR=b;if(b.name=="LeftFoot")footR=b;
            }
            if(hipBone)hipRest=hipBone.localPosition;if(footL)leftFootGoal=footL.position;if(footR)rightFootGoal=footR.position;
            // Apoyo de los pies en el espacio del rig: así siguen plantados aunque se arrastre o escale al personaje.
            rootScale=rig.transform.localScale;if(footL)footLocalL=rig.transform.InverseTransformPoint(footL.position);if(footR)footLocalR=rig.transform.InverseTransformPoint(footR.position);
            if(!manipulator)manipulator=GetComponent<TouchManipulator>()??gameObject.AddComponent<TouchManipulator>();
            manipulator.avatarCamera=avatarCamera?avatarCamera:Camera.main;manipulator.target=rig.transform;manipulator.RestPosition=rootRest;
            framing=GetComponent<AvatarFraming>()??gameObject.AddComponent<AvatarFraming>();framing.studio=this;
            manipulator.blockedAt=screen=>showPanel&&panelRect.Contains(new Vector2(screen.x,Screen.height-screen.y)/uiScale)||!showPanel&&TogglePanelRect.Contains(new Vector2(screen.x,Screen.height-screen.y)/uiScale);
            foreach(var b in rig.bones){if(b.name=="RightEye")leftEye=b;if(b.name=="LeftEye")rightEye=b;if(b.name=="Chest")chestBone=b;if(b.name=="Head")headBone=b;if(b.name=="RightEar")earBoneR=b;if(b.name=="LeftEar")earBoneL=b;}
            if(leftEye)eyeL=leftEye.localPosition;if(rightEye)eyeR=rightEye.localPosition;if(chestBone)chestRest=chestBone.localPosition;if(headBone)headRest=headBone.localPosition;
            // Con el cuerpo nuevo el cuello tiene relleno que se estira hasta dentro de la cabeza: puede moverse más.
            if(rig.GetComponent<KemonoBodyRig>())neckStretch=1;
            if(earBoneR)earRestR=earBoneR.localPosition;if(earBoneL)earRestL=earBoneL.localPosition;
            segmentedBody=rig.GetComponent<KemonoBodyRig>();
            if(!segmentedBody)body=new BodyRigUpgrade(rig);
            foreach(var bone in rig.bones){
                if(bone.name=="RightUpperArm")armRootL=bone;if(bone.name=="RightLowerArm")elbowBoneL=bone;if(bone.name=="RightHand")handBoneL=bone;
                if(bone.name=="LeftUpperArm")armRootR=bone;if(bone.name=="LeftLowerArm")elbowBoneR=bone;if(bone.name=="LeftHand")handBoneR=bone;
            }
            if(handBoneL){var point=rig.transform.InverseTransformPoint(handBoneL.position);handXL.value=point.x;handYL.value=point.y;}
            if(handBoneR){var point=rig.transform.InverseTransformPoint(handBoneR.position);handXR.value=point.x;handYR.value=point.y;}
            foreach(var layer in rig.layers){
                if(layer.name=="EarLeft")cartilageR=new EarFlex(layer);
                if(layer.name=="EarRight")cartilageL=new EarFlex(layer);
            }
        }
        // Conserva la inserción de reposo elegida por el usuario. El arco es pequeño
        // y la base se hunde ligeramente bajo la melena al abrirse hacia fuera.
        public static Vector3 EarPlacement(Vector3 rest,float angle,float amount){
            Vector3 center=new Vector3(0,.37f,rest.z);
            Vector3 arm=rest-center;
            float arc=angle*.45f*Mathf.Clamp(amount,0,1.3f);
            Vector3 offset=Quaternion.Euler(0,0,arc)*arm;
            bool outward=angle*rest.x<0;
            // Al caer hacia fuera (triste, asustado) la oreja baja y se hunde más en la melena para que la base no asome.
            float t=Mathf.Clamp(Mathf.Abs(angle)/25f,0,2);
            float sink=(outward?.018f*t+.012f*t*t:.008f*Mathf.Min(t,1))*Mathf.Clamp(amount,0,1.3f);
            float drop=outward?.03f*Mathf.Max(0,t-.6f)*Mathf.Clamp(amount,0,1.3f):0;
            return center+offset-offset.normalized*sink+Vector3.down*drop;
        }
        // Resorte angular con subpasos: la oreja lleva inercia, rebota un poco y se detiene en su tope.
        class EarSpring {
            float angle,velocity;
            public float Step(float target,float stiffness,float damping,float dt){
                int count=Mathf.Max(1,Mathf.CeilToInt(dt/.008f));float h=dt/count;
                for(int i=0;i<count;i++){
                    velocity+=(stiffness*(target-angle)-damping*velocity)*h;angle+=velocity*h;
                    if(Mathf.Abs(angle)>30){angle=Mathf.Sign(angle)*30;velocity*=-.2f;}
                }
                return angle;
            }
        }
        readonly EarSpring springR=new EarSpring(),springL=new EarSpring();
        // El tallo y las hojas de la skin de calabaza tienen inercia propia; las skins sin ese hueso no cambian.
        readonly EarSpring pumpkinTopSpring=new EarSpring();
        // Muelle angular de la cola: sin tope duro, con subpasos para que el rebote sea estable.
        class TailSpring {
            float angle,velocity;public float Velocity=>velocity;
            public float Step(float target,float stiffness,float damping,float dt){
                int count=Mathf.Max(1,Mathf.CeilToInt(dt/.008f));float h=dt/count;
                for(int i=0;i<count;i++){velocity+=(stiffness*(target-angle)-damping*velocity)*h;velocity=Mathf.Clamp(velocity,-600,600);angle=Mathf.Clamp(angle+velocity*h,-55,55);}
                return angle;
            }
        }
        readonly TailSpring[] tailSprings={new TailSpring(),new TailSpring(),new TailSpring()};
        float tailPhase,previousHip;
        static float Noise(float t,float seed)=>Mathf.PerlinNoise(seed,t)*2-1;
        // iCloud: se sube lo cambiado cada pocos segundos y se recoge lo de otros aparatos al volver a la app.
        float cloudAt;
        void SyncCloud(){
            if(Time.unscaledTime<cloudAt)return;cloudAt=Time.unscaledTime+5;
            if(StudioCloud.Pull())ReloadFromCloud();else StudioCloud.PushIfChanged();
        }
        void OnApplicationFocus(bool focus){if(focus&&StudioCloud.Pull())ReloadFromCloud();}
        void ReloadFromCloud(){
            savedScenes=null;ApplyLayerRects();
            cinemaLayout=PlayerPrefs.GetInt("traigger.cinemaLayout",1)==1;
            if(!string.IsNullOrEmpty(currentScene))ApplyScene(currentScene);
        }
        void Update(){
            SyncCloud();
            if(!rig)return;
            UpdateCinemaLayout();
            if(Application.platform==RuntimePlatform.IPhonePlayer){if(cameraMode<0)SetCameraMode(Mathf.Clamp(PlayerPrefs.GetInt("traigger.cameraMode",0),0,4));ReadBodySensor();}
            if(Application.isEditor&&Input.GetKeyDown(KeyCode.Tab))showPanel=!showPanel;
            if(Input.GetKeyDown(KeyCode.Space))mouth=mouth>.5f?0:1;
            if(Input.GetKeyDown(KeyCode.Alpha1)){smile=0;mouth=0;blinkLeft=blinkRight=0;}
            if(Input.GetKeyDown(KeyCode.Alpha2))smile=1;
            if(Input.GetKeyDown(KeyCode.Alpha3))blinkLeft=blinkRight=1;
            if(Input.GetKeyDown(KeyCode.R)){head=turn=nod=lateral=mouth=smile=blinkLeft=blinkRight=armLeft=armRight=legLeft=legRight=torso=hips=neck=tail=earLeft=earRight=earMood=0;ResetBodyPose();demo=false;}
            // En vertical (iPhone) se amplía el encuadre para que quepan brazos y cola; luego se ajusta con el pellizco.
            if(avatarCamera&&Application.isMobilePlatform&&(!framing||!framing.Active))avatarCamera.orthographicSize=Mathf.Max(2.8f,1.8f/Mathf.Max(.1f,avatarCamera.aspect));
            float t=Time.time,dt=Mathf.Min(Time.deltaTime,.05f);if(dt<=0)return;
            float targetRoll=head,targetYaw=turn,targetPitch=nod,targetJaw=mouth,l=blinkLeft,r=blinkRight,gazeX=0,gazeY=0;
            // Fuente en vivo: primero la cámara frontal del propio dispositivo (ARKit); si no, la red (UDP).
            FaceFrame liveFrame=null;
            if(tracking){
                // La cara que llega de otro aparato (p. ej. el iPhone como Face ID mientras el iPad usa la capturadora)
                // manda sobre la propia: si alguien la envía es porque aquí no hay Face ID completo.
                if(receiver&&receiver.latest!=null&&Time.unscaledTime-receiver.lastArrival<.5f)liveFrame=receiver.latest;
                else if(deviceFace&&deviceFace.latest!=null&&Time.unscaledTime-deviceFace.lastArrival<.5f)liveFrame=deviceFace.latest;
                RelayFace();
            }
            bool live=liveFrame!=null;
            if(Time.unscaledTime-faceDiagAt>3){faceDiagAt=Time.unscaledTime;Debug.Log($"Traigger: seguimiento {(tracking?"activo":"apagado")}, cara {(live?"en vivo":"sin datos")}, modo de cámara {cameraMode}, fuente {(deviceFace?(deviceFace.enabled?"activa":"desactivada"):"ninguna")}");}
            // Vision Pro: la cabeza del casco mueve la del personaje (no hay cara ni ojos: visionOS no los da a las apps).
            bool visionLive=tracking&&vision&&vision.hasHead&&Time.unscaledTime-vision.lastArrival<.5f;
            // Cuerpo desde un iPhone o iPad en modo sensor (Vision Pro en modo compartido).
            bool bodyLive=tracking&&vision&&vision.bodyLive&&!visionLive;
            if(!live&&bodyLive){targetRoll=head+Mathf.Clamp(vision.bodyRoll,-25,25);targetYaw=turn+Mathf.Clamp(vision.bodyYaw,-18,18);targetPitch=nod+Mathf.Clamp(vision.bodyPitch,-12,12);}
            if(!live&&visionLive){targetRoll=head+vision.roll;targetYaw=turn+vision.yaw*.6f;targetPitch=nod+vision.pitch*.6f;gazeX=Mathf.Clamp(vision.yaw/40f,-1,1)*.4f;}
            if(live){var f=liveFrame;targetRoll=f.headRoll;targetYaw=f.headYaw;targetPitch=f.headPitch;targetJaw=f.jawOpen;l=f.eyeBlinkLeft;r=f.eyeBlinkRight;gazeX=f.eyeLookX;gazeY=f.eyeLookY;}
            else if(demo){
                // Respiración y cambios de atención discretos, sin mover las piernas como péndulos.
                targetRoll+=Noise(t*.21f,11)*4.5f;targetYaw+=Noise(t*.16f,27)*11;targetPitch+=Noise(t*.19f,46)*3;
                gazeX=Noise(t*.32f,54)*.35f;gazeY=Noise(t*.28f,64)*.18f;
            }
            if(!live&&autoBlink){
                if(t>=nextBlink){blinkStart=t;blinkInterval=3.1f+Mathf.PerlinNoise(t,8)*2.8f;nextBlink=t+blinkInterval;}
                float age=t-blinkStart;
                float blink=age<.065f?Mathf.SmoothStep(0,1,age/.065f):1-Mathf.SmoothStep(0,1,(age-.065f)/.13f);
                l=Mathf.Max(l,blink);r=Mathf.Max(r,blink);
            }
            FaceFrame expressionFrame=facialExpressions&&facialExpressions.manual?facialExpressions.manualFrame:liveFrame;
            if(facialExpressions&&facialExpressions.manual){targetJaw=expressionFrame.jawOpen;l=Mathf.Max(l,expressionFrame.eyeBlinkLeft);r=Mathf.Max(r,expressionFrame.eyeBlinkRight);}
            if(expressionFrame!=null)targetJaw=Mathf.Max(targetJaw,Mathf.Clamp01(expressionFrame.tongueOut)*.35f);
            BlendExpression(dt);
            // En Vision Pro la voz llega ya aislada por el sistema; el micrófono de Unity queda como reserva.
            bool nativeVoice=vision&&Time.unscaledTime-vision.lastVoice<.5f;
            float voiceOpen=0,voiceZcr=0,voiceLow=0;bool hasVoice=false;
            if(nativeVoice){if(mic)mic.active=false;voiceOpen=vision.voiceOpen;voiceZcr=vision.voiceZcr;voiceLow=vision.voiceLow;hasVoice=true;}
            else if(mic&&mic.active){voiceOpen=mic.open;voiceZcr=mic.zcr;voiceLow=mic.lowRatio;hasVoice=true;}
            if(hasVoice)sniff.FeedAudio(voiceOpen,voiceZcr,nativeVoice?vision.voicePitch:mic&&mic.active?mic.pitch:0,Time.unscaledTime);
            if(live&&liveFrame!=null)sniff.FeedFace((liveFrame.noseSneerLeft+liveFrame.noseSneerRight)*.5f,liveFrame.jawOpen,Time.unscaledTime);
            sniff.Update(Time.unscaledTime,dt);
            // Olfateando, el soplido no debe abrir la boca.
            if(hasVoice)targetJaw=Mathf.Max(targetJaw,voiceOpen*(1-sniff.amount));
            if(speechLanguage<0){speechLanguage=Mathf.Clamp(PlayerPrefs.GetInt("traigger.speechLanguage",0),0,SpeechLanguageCodes.Length-1);ApplySpeechLanguage();}
            bool armsTracked=visionLive&&(vision.leftTracked||vision.rightTracked)||bodyLive;
            UpdateEmotion(hasVoice?voiceOpen:0,nativeVoice?vision.voicePitch:mic&&mic.active?mic.pitch:0,voiceZcr,voiceLow,dt,armsTracked);
            // Olfateo: orejas erguidas y hacia delante, hocico un poco arriba.
            if(sniff.amount>0){float k=sniff.amount;emotionOffset.earMood+=.6f*k;emotionOffset.earLeft-=10*k;emotionOffset.earRight+=10*k;targetPitch+=4*k;targetJaw*=1-.8f*k;}
            ApplyEmotion(1);targetRoll+=emotionOffset.head;targetPitch+=emotionPitch;targetYaw+=emotionYaw;
            // Sin cámara de cara, la voz anima cabeza, mirada, boca y orejas de forma natural.
            speechWeight=Mathf.MoveTowards(speechWeight,naturalSpeech&&!live&&hasVoice?1:0,dt*2);
            speech.Step(hasVoice?voiceOpen:0,voiceZcr,voiceLow,dt,t);
            targetRoll+=speech.roll*speechWeight;targetYaw+=speech.yaw*speechWeight;targetPitch+=speech.pitch*speechWeight;
            if(speechWeight>0){gazeX=Mathf.Lerp(gazeX,speech.gazeX,speechWeight);gazeY=Mathf.Lerp(gazeY,speech.gazeY,speechWeight);}
            float rr=roll.Step(Mathf.Clamp(targetRoll,-20,20),response,dt);
            float yy=yaw.Step(Mathf.Clamp(targetYaw,-18,18),response*.85f,dt);
            float pp=pitch.Step(Mathf.Clamp(targetPitch,-12,12),response,dt);
            // Boca: zona muerta pequeña para el temblor de la cámara y respuesta casi inmediata al abrir (para no ir
            // detrás de la voz); al cerrar es algo más lenta. La forma (ancha o en «o/u») viene de la sonrisa y los labios.
            float mouthTarget=Mathf.InverseLerp(.04f,.7f,Mathf.Clamp01(targetJaw));mouthTarget=1-(1-mouthTarget)*(1-mouthTarget);
            float shapeTarget=expressionFrame!=null?Mathf.Clamp((expressionFrame.mouthSmileLeft+expressionFrame.mouthSmileRight)*.9f-(expressionFrame.mouthFunnel+expressionFrame.mouthPucker)*.9f,-1,1):Mathf.Clamp(smile*.6f+speech.mouthShape*speechWeight,-1,1);
            float mouthShape=mouthShapeChannel.Step(shapeTarget,mouthSmoothing*1.5f+.02f,dt);
            float jj=jaw.Step(mouthTarget,mouthTarget>jaw.value?mouthSmoothing:mouthSmoothing*2.2f,dt),ll=bl.Step(Mathf.Clamp01(l),.025f,dt),bb=br.Step(Mathf.Clamp01(r),.025f,dt);
            float breathe=demo?Mathf.Sin(t*1.65f)*.5f+.5f:0;
            float sideTarget=lateral+yy/18f*.55f+(demo?Mathf.Sin(t*.54f)*.28f+Noise(t*.19f,150)*.7f:0);
            float sway=side.Step(Mathf.Clamp(sideTarget,-1,1)*lateralAmount,.40f,dt);
            float cc=chest.Step(torso+(bodyLive?Mathf.Clamp(vision.bodyLean,-20,20)*.6f:0)+rr*.13f+yy*.025f-sway*2.6f+(demo?Noise(t*.15f,77)*.7f:0),.42f,dt);
            float hh=hip.Step(hips-cc*.22f,.65f,dt);
            // Hacia dentro el brazo cruzaría el torso, que el dibujo frontal no tiene; se limita ese lado.
            // Sin espejo: tu mano izquierda mueve el brazo de la izquierda de la pantalla.
            float visionArmL=visionLive&&vision.leftTracked?-vision.leftArm*110:0,visionArmR=visionLive&&vision.rightTracked?vision.rightArm*110:0;
            if(bodyLive){if(vision.bodyLeftTracked)visionArmL=-Mathf.Max(0,vision.bodyArmLeft-15);if(vision.bodyRightTracked)visionArmR=Mathf.Max(0,vision.bodyArmRight-15);}
            float al=leftArm.Step(Mathf.Clamp(armLeft+visionArmL-speech.armSway*speechWeight,-135,135)+(demo?Noise(t*.24f,85)*1.8f:0)-cc*.25f,.32f,dt);
            float ar=rightArm.Step(Mathf.Clamp(armRight+visionArmR+speech.armSway*speechWeight,-135,135)+(demo?Noise(t*.22f,94)*1.6f:0)-cc*.25f,.35f,dt);
            float userScale=manipulator?manipulator.scale:1;Vector3 userOffset=manipulator?(Vector3)manipulator.offset:Vector3.zero;
            rig.ResetPose();rig.transform.localScale=rootScale*userScale;rig.transform.localPosition=rootRest+userOffset+new Vector3(sway*.15f*userScale,0,0);
            var stage=rig.transform.parent;Vector3 Stage(Vector3 local){var point=rootRest+userOffset+Vector3.Scale(rootScale*userScale,local);return stage?stage.TransformPoint(point):point;}
            if(footL)leftFootGoal=Stage(footLocalL);if(footR)rightFootGoal=Stage(footLocalR);
            if(hipBone)hipBone.localPosition=hipRest+new Vector3(sway*.025f,-.006f-sway*sway*.006f,0);
            rig.Rotate("Hips",hh);rig.Rotate("Spine",cc*.4f);rig.Rotate("Chest",cc*.6f);
            // El cuello inicia el gesto y la cabeza lo completa: ninguna articulación se dobla sola.
            float neckRoll=(rr-cc-hh)*.4f,nm=neckTilt.Step(neck,response,dt);rig.Rotate("Neck",neckRoll+nm);rig.Rotate("Head",rr-cc-hh-neckRoll);
            // Cadena completa: hombro, brazo, codo y muñeca. Cada canal conserva velocidad.
            rig.Rotate("RightShoulder",shoulderL.Step(shoulderLeft,.24f,dt));
            rig.Rotate("LeftShoulder",shoulderR.Step(shoulderRight,.24f,dt));
            rig.Rotate("RightUpperArm",upperL.Step(upperArmLeft,.26f,dt));
            rig.Rotate("LeftUpperArm",upperR.Step(upperArmRight,.26f,dt));
            rig.Rotate("RightLowerArm",al+cc*.3f);rig.Rotate("LeftLowerArm",ar+cc*.3f);
            rig.Rotate("RightHand",wristL.Step(wristLeft,.16f,dt)-leftArm.velocity*.012f);
            rig.Rotate("LeftHand",wristR.Step(wristRight,.16f,dt)-rightArm.velocity*.012f);
            // Piernas del cuerpo 3D (ARKit): se suman a la pose; al moverse, el pie deja de apoyarse solo.
            // Por convención del rig, negativo abre la pierna izquierda (del usuario) y positivo la derecha.
            float sensorThighL=0,sensorThighR=0,sensorKneeL=0,sensorKneeR=0;
            if(tracking&&vision&&vision.legsLive){
                float sign=invertLegs?-1:1;
                sensorThighL=-Mathf.Clamp(vision.bodyLegSpreadLeft-4,-20,60)*sign;sensorThighR=Mathf.Clamp(vision.bodyLegSpreadRight-4,-20,60)*sign;
                sensorKneeL=Mathf.Clamp(vision.bodyKneeLeft-8,0,95)*sign;sensorKneeR=-Mathf.Clamp(vision.bodyKneeRight-8,0,95)*sign;
            }
            rig.Rotate("RightUpperLeg",thighPoseL.Step(Mathf.Clamp(thighLeft+sensorThighL,-65,65),.30f,dt));
            rig.Rotate("LeftUpperLeg",thighPoseR.Step(Mathf.Clamp(thighRight+sensorThighR,-65,65),.30f,dt));
            rig.Rotate("RightFoot",ankleL.Step(ankleLeft,.20f,dt));rig.Rotate("LeftFoot",ankleR.Step(ankleRight,.20f,dt));
            rig.Rotate("RightToes",toesL.Step(toesLeft,.16f,dt));rig.Rotate("LeftToes",toesR.Step(toesRight,.16f,dt));
            body?.SetVolume(volume);segmentedBody?.SetVolume(volume);
            // La pierna gira bajo el bajo del pantalón, no desde la cadera oculta.
            rig.Rotate("RightLowerLeg",leftLeg.Step(Mathf.Clamp(legLeft+sensorKneeL,-100,100)-hh,.35f,dt));rig.Rotate("LeftLowerLeg",rightLeg.Step(Mathf.Clamp(legRight+sensorKneeR,-100,100)-hh,.35f,dt));
            if(chestBone)chestBone.localPosition=chestRest+new Vector3(cc*.0015f+sway*.045f,breathe*.009f,0);
            // Arriba/abajo: la cabeza baja o sube a lo largo del cuello. Al bajar, la barbilla tapa el cuello (hasta un
            // 22 % más corto); al subir solo un 8 %, para que el relleno del cuello siga cubriendo y no se separe la
            // cabeza (nada de cabezas flotantes tipo Rayman). Sin el cuerpo nuevo, mucho menos.
            if(headBone){float nod=Mathf.Clamp(pp/12f,-1,1);headBone.localPosition=headRest*(1+(nod<0?.22f*nod:.08f*nod)*neckStretch);}
            // El apoyo se mezcla con la pose manual según el canal suavizado: sin saltos al tocar el control.
            float supportL=1-Mathf.SmoothStep(0,1,(Mathf.Abs(leftLeg.value+hh)+Mathf.Abs(thighPoseL.value)+Mathf.Abs(ankleL.value)+Mathf.Abs(toesL.value))/6);
            float supportR=1-Mathf.SmoothStep(0,1,(Mathf.Abs(rightLeg.value+hh)+Mathf.Abs(thighPoseR.value)+Mathf.Abs(ankleR.value)+Mathf.Abs(toesR.value))/6);
            if(segmentedBody){
                TwoBoneIK.Solve(thighL,kneeL,footL,leftFootGoal,-1,supportL);
                TwoBoneIK.Solve(thighR,kneeR,footR,rightFootGoal,1,supportR);
            }else{
                PlantFoot(kneeL,null,footL,leftFootGoal,-1,supportL);PlantFoot(kneeR,null,footR,rightFootGoal,1,supportR);
            }
            // Guiar la mano cambia hombro y codo juntos y conserva la longitud de ambos segmentos.
            if(guideHandLeft)TwoBoneIK.Solve(armRootL,elbowBoneL,handBoneL,rig.transform.TransformPoint(new Vector3(handXL.Step(handTargetLeft.x,.22f,dt),handYL.Step(handTargetLeft.y,.22f,dt),0)),-1);
            else if(handBoneL){var point=rig.transform.InverseTransformPoint(handBoneL.position);handXL.value=point.x;handYL.value=point.y;handXL.velocity=handYL.velocity=0;}
            if(guideHandRight)TwoBoneIK.Solve(armRootR,elbowBoneR,handBoneR,rig.transform.TransformPoint(new Vector3(handXR.Step(handTargetRight.x,.22f,dt),handYR.Step(handTargetRight.y,.22f,dt),0)),1);
            else if(handBoneR){var point=rig.transform.InverseTransformPoint(handBoneR.position);handXR.value=point.x;handYR.value=point.y;handXR.velocity=handYR.velocity=0;}
            // Respuesta secundaria al movimiento real de la cabeza; no osciladores independientes.
            float rollSpeed=Mathf.Clamp((rr-previousRoll)/dt,-100,100),yawSpeed=Mathf.Clamp((yy-previousYaw)/dt,-100,100);
            previousRoll=rr;previousYaw=yy;
            float sideSpeed=(sway-previousSide)/dt;previousSide=sway;
            float force=Mathf.Clamp((-rollSpeed*.016f-yawSpeed*.017f-sideSpeed*.38f+(demo?Noise(t*.37f,201)*.14f:0))*physics*hairFreedom,-.90f,.90f);
            rig.HairMotion(strandL.Step(force,65,6,dt),strandR.Step(force*.90f,48,5,dt),0,strandB.Step(force*.80f,38,4.5f,dt));
            if(!hairDetails){hairDetails=GetComponent<HairDetailRig>()??gameObject.AddComponent<HairDetailRig>();hairDetails.rig=rig;LoadSavedControls();}
            float pitchSpeed=Mathf.Clamp((pp-previousPitch)/dt,-100,100);previousPitch=pp;
            hairDetails.Step(rollSpeed,yawSpeed,pitchSpeed,sideSpeed,physics*hairFreedom,dt);
            // El adorno sigue la cabeza con un pequeño retraso, como una pieza ligera de tela/hojas.
            // El ángulo es moderado para evitar el efecto de antena o marioneta.
            float pumpkinTopTarget=Mathf.Clamp((-rollSpeed*.035f-yawSpeed*.045f-sideSpeed*.16f+pitchSpeed*.012f)*physics,-9,9);
            rig.Rotate("PumpkinStem",pumpkinTopSpring.Step(pumpkinTopTarget,44,7,dt));
            // Gesto felino: orientación por la base y curvatura secundaria de la punta.
            // El arco acompaña a la inserción bajo el pelo; no sustituye al giro de la oreja.
            float flexibility=Mathf.Clamp(earFlexibility,.2f,2);
            float mood=Mathf.Clamp(earMood+speech.earAttention*speechWeight,-1,1);
            float openness=mood<0?-mood*42:-mood*12; // tristeza: orejas caídas a los lados
            float attention=(yy/18f*2.5f+gazeSmooth.x*2)*earArc;
            // Inercia de la base: al mover la cabeza o el cuerpo las orejas se quedan atrás y luego vuelven.
            float baseInertia=(-rollSpeed*.14f-yawSpeed*.07f-sideSpeed*3f)*physics*earFeedback;
            // Recorrido: hasta 50° hacia fuera (caídas) y 22° hacia dentro. La oreja izquierda de la pantalla abre en negativo.
            float erTarget=Mathf.Clamp(earLeft+openness+smile*7+attention+baseInertia,-22,50);
            float elTarget=Mathf.Clamp(earRight-openness-smile*7+attention+baseInertia,-50,22);
            float er=earBaseR.Step(erTarget,.14f,dt),el=earBaseL.Step(elTarget,.17f,dt);
            rig.Rotate("RightEar",er*.85f);rig.Rotate("LeftEar",el*.85f);
            if(earBoneR)earBoneR.localPosition=EarPlacement(earRestR,er,earArc);
            if(earBoneL)earBoneL.localPosition=EarPlacement(earRestL,el,earArc);
            // Cartílago: la punta se retrasa al mover la cabeza (inclinar, girar y asentir) y rebota un poco al pararse.
            float earInertia=(-rollSpeed*.2f-yawSpeed*.1f-sideSpeed*4f)*physics*flexibility*earFeedback;
            float earBounce=Mathf.Abs(pitchSpeed)*.04f*physics*flexibility*earFeedback;
            // La punta tarda un poco más: al frenar la base, continúa y se asienta.
            float eR=springR.Step(Mathf.Clamp(er*.28f-earBaseR.velocity*.035f+earInertia+earBounce,-30,30),62/flexibility,6.5f,dt);
            float eL=springL.Step(Mathf.Clamp(el*.28f-earBaseL.velocity*.04f+earInertia*.88f-earBounce,-30,30),56/flexibility,6.2f,dt);
            cartilageR?.Apply(eR);cartilageL?.Apply(eL);
            // Cola: cadena de tres muelles. El meneo es una onda que recorre la cola de la base a la punta
            // (cada tramo con retraso y más amplitud), con ritmo irregular. Al girar la cadera o desplazarse el
            // cuerpo, cada tramo se queda atrás por inercia y luego da un pequeño latigazo antes de asentarse.
            tailPhase+=dt*tailSpeed*(2.1f+Noise(t*.13f,301)*.6f);
            float wag=tailWag*(.75f+.25f*Noise(t*.23f,311));
            float hipSpeed=(hh-previousHip)/dt;previousHip=hh;
            float drive=(tail+yy*.18f-yawSpeed*.05f)*physics;
            float inertia=(-sideSpeed*9f-hipSpeed*.6f)*physics;
            float a1=tailSprings[0].Step(drive+wag*7*Mathf.Sin(tailPhase)+inertia*.6f,70,7,dt);
            float a2=tailSprings[1].Step(a1*.35f+wag*10*Mathf.Sin(tailPhase-.9f)+inertia*.9f-tailSprings[0].Velocity*.03f,55,5.5f,dt);
            float a3=tailSprings[2].Step(a2*.35f+wag*13*Mathf.Sin(tailPhase-1.8f)+inertia*1.1f-tailSprings[1].Velocity*.04f,42,4.2f,dt);
            rig.Rotate("Tail1",a1);rig.Rotate("Tail2",a2);rig.Rotate("Tail3",a3);
            // Pose final para otros cuerpos (skins 3D, Skin3DAvatar.cs). Brazos: positivo = levantarlo hacia fuera; solo la
            // pose y el seguimiento, sin el vaivén alterno al hablar ni el ruido orgánico (en 3D tiemblan y parecen el meme del 67).
            Pose=new PoseState{roll=rr,yaw=yy,pitch=pp,chest=cc,hips=hh,armLeft=-(armLeft+visionArmL),armRight=armRight+visionArmR,mouth=jj,breathe=breathe,blinkLeft=ll,blinkRight=bb,smile=Mathf.Max(0,mouthShape),mouthShape=mouthShape};
            rig.Look(yy,pp);rig.Face(FaceDeform.Mouth,jj);rig.MouthShape(mouthShape);rig.Face(FaceDeform.BlinkLeft,ll);rig.Face(FaceDeform.BlinkRight,bb);
            if(!facialExpressions){facialExpressions=gameObject.GetComponent<FacialExpressionRig>()??gameObject.AddComponent<FacialExpressionRig>();facialExpressions.rig=rig;}
            facialExpressions.catSniff=sniff.amount;
            facialExpressions.Step(liveFrame,dt);
            gazeSmooth=Vector2.Lerp(gazeSmooth,new Vector2(Mathf.Clamp(gazeX,-1,1),Mathf.Clamp(gazeY,-1,1)),1-Mathf.Exp(-18*dt));
            // La mirada pertenece al iris y a la pupila: el blanco y el contorno no se arrastran.
            if(leftEye)leftEye.localPosition=eyeL;if(rightEye)rightEye.localPosition=eyeR;
            if(!layeredEyes){layeredEyes=GetComponent<LayeredEyeRig>()??gameObject.AddComponent<LayeredEyeRig>();layeredEyes.rig=rig;}
            layeredEyes.Step(liveFrame,gazeSmooth,dt);
            ApplyEmotion(-1);
        }
        void OnDestroy(){cartilageR?.Dispose();cartilageL?.Dispose();body?.Dispose();}
        void ResetBodyPose(){guideHandLeft=guideHandRight=false;shoulderLeft=shoulderRight=upperArmLeft=upperArmRight=wristLeft=wristRight=thighLeft=thighRight=ankleLeft=ankleRight=toesLeft=toesRight=0;}
        // Piezas rígidas: la pierna gira desde la rodilla, bajo el pantalón, para apuntar el pie a su apoyo.
        // El control manual de pierna reduce el peso del apoyo hasta liberarla por completo.
        void PlantFoot(Transform thigh,Transform knee,Transform foot,Vector3 goal,float bendSign,float weight){
            if(!thigh||!foot||weight<=.001f)return;
            Vector2 current=foot.position-thigh.position,target=goal-thigh.position;
            float angle=Vector2.SignedAngle(current,target)*weight;
            thigh.rotation=Quaternion.AngleAxis(angle,Vector3.forward)*thigh.rotation;
        }
        static readonly string[] PanelTabs={"Inicio","Seguimiento","Cuerpo","Física","Ajustes","Cara","Cine"};
        int panelTab=-1;
        static readonly Rect TogglePanelRect=new Rect(12,12,90,44);
        void OnGUI(){
            if(!Application.isEditor)return; // panel de depuración: solo en el editor
            if(cleanOutput)return;
            // En iPhone/iPad el panel se escala según la densidad de pantalla y tiene un botón para mostrarlo.
            bool touchUI=Application.isMobilePlatform||Input.touchSupported||Application.platform==RuntimePlatform.VisionOS;
            uiScale=Application.isMobilePlatform?Mathf.Clamp(Screen.dpi/200f,1,2.5f):1;
            GUI.matrix=Matrix4x4.Scale(new Vector3(uiScale,uiScale,1));
            TouchStyles(touchUI);
            if(!showPanel){
                if(GUI.Button(TogglePanelRect,"Panel"))showPanel=true;
                return;
            }
            if(panelTab<0)panelTab=Mathf.Clamp(PlayerPrefs.GetInt("traigger.panelTab",0),0,PanelTabs.Length-1);
            float panelWidth=Mathf.Min(touchUI?400:340,Screen.width/uiScale-24);
            panelRect=new Rect(12,12,panelWidth,Mathf.Min(Screen.height/uiScale-24,880));
            GUILayout.BeginArea(panelRect,GUI.skin.box);
            // Cabecera fija (no se desplaza): título y botón de cerrar siempre a la vista. Tab también lo oculta.
            GUILayout.BeginHorizontal();
            GUILayout.Label("Traigger");GUILayout.FlexibleSpace();
            if(GUILayout.Button("✕ Cerrar",GUILayout.Width(touchUI?120:80)))showPanel=false;
            GUILayout.EndHorizontal();
            // Pestañas: lo habitual en «Inicio»; el resto, agrupado. La pestaña elegida se recuerda.
            // En táctil van en dos filas para que los nombres quepan sin cortarse.
            int previousTab=panelTab;
            panelTab=GUILayout.SelectionGrid(panelTab,PanelTabs,3,GUILayout.Height((touchUI?50:26)*Mathf.CeilToInt(PanelTabs.Length/3f)));
            if(panelTab!=previousTab){panelScroll=Vector2.zero;PlayerPrefs.SetInt("traigger.panelTab",panelTab);}
            GUILayout.BeginHorizontal();
            if(framing&&GUILayout.Button("Cuerpo entero"))framing.Center(false);
            if(framing&&GUILayout.Button("Cara · llamada"))framing.Center(true);
            GUILayout.EndHorizontal();
            if(manipulator){bool locked=GUILayout.Toggle(manipulator.gesturesLocked,"Bloquear arrastre y pellizco");if(locked!=manipulator.gesturesLocked)manipulator.SetGesturesLocked(locked);}
            GUILayout.Space(4);
            // Solo desplazamiento vertical: el contenido se ajusta al ancho del panel y el texto largo salta de línea.
            panelScroll=GUILayout.BeginScrollView(panelScroll,false,false,GUIStyle.none,GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical(GUILayout.Width(panelWidth-GUI.skin.box.padding.horizontal-GUI.skin.verticalScrollbar.fixedWidth-8));
            switch(panelTab){
            case 0: // Inicio
                GUILayout.Label("Expresiones");
                for(int row=0;row<expressions.Length;row+=4){GUILayout.BeginHorizontal();
                    for(int i=row;i<Mathf.Min(row+4,expressions.Length);i++){
                        var style=new GUIStyle(GUI.skin.button);if(i==expressionIndex)style.fontStyle=FontStyle.Bold;
                        if(GUILayout.Button(expressions[i].name,style))SetExpression(i);}
                    GUILayout.EndHorizontal();}
                GUILayout.Space(6);
                demo=GUILayout.Toggle(demo,"Movimiento orgánico y respiración");
                naturalSpeech=GUILayout.Toggle(naturalSpeech,"Animación natural con la voz");
                voiceEmotion=GUILayout.Toggle(voiceEmotion,"Emoción por la voz · "+EmotionLabel());
                if(voiceEmotion){
                    voiceEmotionStrength=Slider("Intensidad de la emoción",voiceEmotionStrength,0,1);
                    GUILayout.Label("Entonación: "+emotion.status+"\n"+emotion.debug);
                    if(vision||Application.platform==RuntimePlatform.IPhonePlayer){
                        GUILayout.Label("Idioma de las frases");
                        int chosen=GUILayout.Toolbar(Mathf.Max(0,speechLanguage),SpeechLanguageNames,GUILayout.Height(bigUI?48:22));
                        if(chosen!=speechLanguage){speechLanguage=chosen;ApplySpeechLanguage();}
                    }
                }
                tracking=GUILayout.Toggle(tracking,"Seguimiento (cara, sensores)");
                if(vision&&GUILayout.Button("Conectar sensores con el Mac…"))vision.OpenRelaySettings();
                if(vision&&GUILayout.Button(vision.immersive?"Desactivar sensores (volver con otras apps)":"Activar sensores (cabeza y manos; oculta otras apps)"))vision.SetSensors(!vision.immersive);
                GUILayout.Space(6);
                GUILayout.Label("Estado");
                // La escena puede traer el objeto de Vision Pro también en iPhone/iPad: allí manda el micrófono propio.
                if(vision&&Application.platform==RuntimePlatform.VisionOS){GUILayout.Label("· Frases: "+vision.speechStatus);if(vision.speechText.Length>0)GUILayout.Label("  «"+vision.speechText+"»"+(vision.wordEmotion!=null&&string.IsNullOrEmpty(vision.emotionCue)?" → "+vision.wordEmotion:""));if(!string.IsNullOrEmpty(vision.emotionCue))GUILayout.Label("· Sonido: "+vision.emotionCue+" → "+vision.wordEmotion);GUILayout.Label("· Vision Pro: "+vision.sensorStatus);GUILayout.Label("· Cuerpo (iPhone/iPad): "+vision.bodyStatus);GUILayout.Label("· Boca: "+vision.voiceStatus+(Time.unscaledTime-vision.lastVoice<1?$" · nivel {new string('|',Mathf.RoundToInt(vision.voiceOpen*12))}":" · no llega audio"));}
                else if(mic){GUILayout.Label("· Boca: "+mic.status);if(Application.platform==RuntimePlatform.IPhonePlayer){GUILayout.Label("· Frases: "+mic.speechStatus);if(mic.speechText.Length>0)GUILayout.Label("  «"+mic.speechText+"»"+(mic.wordEmotion!=null&&string.IsNullOrEmpty(mic.emotionCue)?" → "+mic.wordEmotion:""));if(!string.IsNullOrEmpty(mic.emotionCue))GUILayout.Label("· Sonido: "+mic.emotionCue+" → "+mic.wordEmotion);}}
                if(deviceFace)GUILayout.Label("· Cámara frontal: "+deviceFace.status);
                GUILayout.Space(6);
                if(GUILayout.Button("Pose neutra")){head=turn=nod=lateral=mouth=smile=blinkLeft=blinkRight=armLeft=armRight=legLeft=legRight=torso=hips=neck=tail=earLeft=earRight=earMood=0;ResetBodyPose();demo=false;expressionIndex=-1;}
                if(manipulator&&GUILayout.Button("Restablecer posición y tamaño"))manipulator.ResetView();
                // Personaje (en los dispositivos se elige en la interfaz nativa: Inicio → Personaje).
                GUILayout.Space(8);GUILayout.Label("Personaje");
                string currentCharacter=PlayerPrefs.GetString("traigger.character","");
                foreach(var c in CharacterPack.List())
                    if(GUILayout.Toggle(currentCharacter==c.path,c.name+(string.IsNullOrEmpty(c.version)?"":" · "+c.version))&&currentCharacter!=c.path)LoadCharacter(c.path);
                // Outfits de Traigger y sus opciones (cambiar reinicia el estudio).
                if(currentCharacter.Length==0){
                    var outfits=Outfits.For("Traigger");
                    if(outfits.Count>1){
                        GUILayout.Space(6);GUILayout.Label("Outfit");string currentOutfit=Outfits.Current;
                        foreach(var o in outfits)if(GUILayout.Toggle(currentOutfit==o.id,o.name)&&currentOutfit!=o.id){Outfits.Current=o.id;LoadCharacter(currentCharacter);}
                        var chosen=outfits.Find(o=>o.id==currentOutfit);
                        if(chosen?.definition?.options!=null)foreach(var option in chosen.definition.options){
                            bool on=Outfits.OptionOn(chosen.id,option);
                            if(GUILayout.Toggle(on,"  "+option.label)!=on){Outfits.SetOption(chosen.id,option.id,!on);LoadCharacter(currentCharacter);}
                        }
                    }
                }
                if(StudioBootstrap.CharacterStatus.Length>0)GUILayout.Label(StudioBootstrap.CharacterStatus);
                break;
            case 1: // Seguimiento
                if(vision){
                    GUILayout.Label("Vision Pro · "+vision.sensorStatus);
                    if(vision.immersive){
                        GUILayout.Label($"Cabeza: {(vision.hasHead?"recibida":"sin datos")} · muestras {vision.headSamplesReceived} · mensajes {vision.messages}\ngiro {vision.yaw:0} · arriba/abajo {vision.pitch:0} · inclinación {vision.roll:0}");
                    }
                    if(GUILayout.Button("Recentrar cabeza"))vision.Recenter();
                    GUILayout.Label("Cuerpo (iPhone/iPad): "+vision.bodyStatus);
                    vision.voiceSensitivity=Slider("Sensibilidad de la voz",vision.voiceSensitivity,.5f,4);
                }
                if(mic){mic.active=GUILayout.Toggle(mic.active,"Boca con el micrófono · "+mic.status);mic.sensitivity=Slider("Sensibilidad del micrófono",mic.sensitivity,.5f,4);}
                mouthSmoothing=Slider("Suavidad de la boca (más = más lenta)",mouthSmoothing,.01f,.2f);
                if(deviceFace){
                    GUILayout.Label("Cámara frontal: "+deviceFace.status);
                    deviceFace.mirror=GUILayout.Toggle(deviceFace.mirror,"Modo espejo");
                    GUILayout.BeginHorizontal();
                    deviceFace.invertYaw=GUILayout.Toggle(deviceFace.invertYaw,"Invertir giro");
                    deviceFace.invertRoll=GUILayout.Toggle(deviceFace.invertRoll,"Invertir inclinación");
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    deviceFace.invertPitch=GUILayout.Toggle(deviceFace.invertPitch,"Invertir arriba/abajo");
                    deviceFace.swapEyes=GUILayout.Toggle(deviceFace.swapEyes,"Intercambiar ojos");
                    GUILayout.EndHorizontal();
                }
                if(Application.platform==RuntimePlatform.IPhonePlayer){
                    GUILayout.Label("Cámara");
                    for(int m=0;m<CameraModeNames.Length;m++){
                        if(!CameraModeAvailable(m))continue;
                        var style=new GUIStyle(GUI.skin.button);if(m==cameraMode)style.fontStyle=FontStyle.Bold;
                        string label=(m==cameraMode?"● ":"")+CameraModeNames[m]+(m==4&&HasLiDAR?" + LiDAR":"");
                        if(GUILayout.Button(label,style)&&m!=cameraMode)SetCameraMode(m);
                    }
                    if(cameraMode==0)GUILayout.Label("Cara, ojos y boca con Face ID. Las orejas, la cola y los gestos los pone la voz.");
                    else{
                        GUILayout.Label("Sensor: "+BodySensorStatus());
                        GUILayout.Label(cameraMode==4?"Apoya el aparato a 2–3 m, apuntándote con la cámara trasera: te sigue el cuerpo entero en 3D. La cara la ponen la voz y las Vision Pro.":
                            cameraMode==1?"Que se te vea de cintura para arriba. Si llevas las Vision Pro en la misma Wi-Fi, también las mueve.":
                            "Apoya el aparato apuntándote con la cámara trasera, de cintura para arriba.");
                        if(cameraMode==4)invertLegs=GUILayout.Toggle(invertLegs,"Invertir piernas (si se mueven al revés)");
                        bool battery=GUILayout.Toggle(saveBattery,"Ahorrar batería: no dibujar al personaje aquí (solo sensor para Vision Pro)");
                        if(battery!=saveBattery){saveBattery=battery;if(avatarCamera)avatarCamera.enabled=!saveBattery;}
                    }
                }
                GUILayout.Label(receiver?"Red (UDP): "+receiver.status:"Sin receptor de red");
                break;
            case 2: // Cuerpo
                GUILayout.Label("Cabeza y tronco");
                head=Slider("Inclinar cabeza",head,-20,20);turn=Slider("Mirar a los lados",turn,-18,18);nod=Slider("Mirar arriba / abajo",nod,-12,12);
                neck=Slider("Cuello",neck,-15,15);torso=Slider("Torso",torso,-15,15);hips=Slider("Cadera",hips,-15,15);
                lateral=Slider("Moverse de lado a lado",lateral,-1,1);
                GUILayout.Label("Cara");
                mouth=Slider("Boca",mouth,0,1);smile=Slider("Expresión alegre",smile,0,1);
                blinkLeft=Slider("Ojo izquierdo",blinkLeft,0,1);blinkRight=Slider("Ojo derecho",blinkRight,0,1);
                GUILayout.Label("Orejas y cola");
                earMood=Slider("Orejas: bajas / atentas",earMood,-1,1);
                earLeft=Slider("Oreja izquierda",earLeft,-25,45);earRight=Slider("Oreja derecha",earRight,-45,25);
                tail=Slider("Cola",tail,-35,35);
                GUILayout.Label("Brazos");
                shoulderLeft=Slider("Hombro izquierdo",shoulderLeft,-25,25);shoulderRight=Slider("Hombro derecho",shoulderRight,-25,25);
                upperArmLeft=Slider("Brazo izquierdo",upperArmLeft,-110,110);upperArmRight=Slider("Brazo derecho",upperArmRight,-110,110);
                armLeft=Slider("Codo izquierdo",armLeft,-135,135);armRight=Slider("Codo derecho",armRight,-135,135);
                wristLeft=Slider("Muñeca izquierda",wristLeft,-85,85);wristRight=Slider("Muñeca derecha",wristRight,-85,85);
                guideHandLeft=GUILayout.Toggle(guideHandLeft,"Guiar brazo izquierdo por la mano");
                if(guideHandLeft){handTargetLeft.x=Slider("Mano izquierda · horizontal",handTargetLeft.x,-1.5f,-.15f);handTargetLeft.y=Slider("Mano izquierda · altura",handTargetLeft.y,1.75f,3.6f);}
                guideHandRight=GUILayout.Toggle(guideHandRight,"Guiar brazo derecho por la mano");
                if(guideHandRight){handTargetRight.x=Slider("Mano derecha · horizontal",handTargetRight.x,.15f,1.5f);handTargetRight.y=Slider("Mano derecha · altura",handTargetRight.y,1.75f,3.6f);}
                GUILayout.Label("Piernas");
                thighLeft=Slider("Muslo izquierdo",thighLeft,-65,65);thighRight=Slider("Muslo derecho",thighRight,-65,65);
                legLeft=Slider("Rodilla izquierda",legLeft,-100,100);legRight=Slider("Rodilla derecha",legRight,-100,100);
                ankleLeft=Slider("Tobillo izquierdo",ankleLeft,-60,60);ankleRight=Slider("Tobillo derecho",ankleRight,-60,60);
                toesLeft=Slider("Punta pie izquierdo",toesLeft,-35,35);toesRight=Slider("Punta pie derecho",toesRight,-35,35);
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("Probar saludo")){ResetBodyPose();demo=false;upperArmLeft=-60;armLeft=-75;wristLeft=15;}
                if(GUILayout.Button("Probar paso")){ResetBodyPose();demo=false;thighLeft=-18;legLeft=25;ankleLeft=-12;}
                GUILayout.EndHorizontal();
                break;
            case 3: // Física
                response=Slider("Suavidad de transición",response,.08f,.5f);
                lateralAmount=Slider("Amplitud del balanceo",lateralAmount,0,2);
                physics=Slider("Inercia de pelo, orejas y cola",physics,0,1.3f);
                hairFreedom=Slider("Pelo suelto / recorrido",hairFreedom,.2f,2.5f);
                if(hairDetails){
                    hairDetails.crownFreedom=Slider("Mechoncitos superiores",hairDetails.crownFreedom,0,2);
                    hairDetails.fringeFreedom=Slider("Flexibilidad del flequillo",hairDetails.fringeFreedom,0,2);
                    hairDetails.sharpness=Slider("Nitidez del pelo",hairDetails.sharpness,0,.8f);
                    hairDetails.gradient=Slider("Degradado de mechas",hairDetails.gradient,0,1);
                }
                earFlexibility=Slider("Flexibilidad de las orejas",earFlexibility,.2f,2);
                earFeedback=Slider("Rebote de las orejas al mover la cabeza",earFeedback,0,2);
                earArc=Slider("Arco de las orejas",earArc,0,1.3f);
                tailWag=Slider("Cola: meneo",tailWag,0,1.5f);tailSpeed=Slider("Cola: velocidad del meneo",tailSpeed,.3f,2.5f);
                break;
            case 5: // Cara
                DrawFaceControls();
                break;
            case 6: // Cine
                if(movie)movie.DrawPanel(touchUI);else GUILayout.Label("El cine está disponible en Mac, iPhone y iPad.");
                break;
            case 4: // Ajustes
                // Perfil de tu voz: calibrar cada emoción hace que la entonación se lea con tu voz, no con una media.
                GUILayout.Label("Tu voz");
                GUILayout.Label(emotion.calibrating>=0?emotion.status:"Pulsa una emoción y habla 5 s con ella (cuenta algo, no hace falta actuar mucho). Empieza por «Neutro».");
                for(int row=0;row<VoiceEmotion.Count;row+=4){GUILayout.BeginHorizontal();
                    for(int i=row;i<Mathf.Min(row+4,VoiceEmotion.Count);i++)
                        if(GUILayout.Button((emotion.Calibrated(i)?"✓ ":"")+VoiceEmotion.Names[i])&&emotion.calibrating<0)emotion.Calibrate(i);
                    GUILayout.EndHorizontal();}
                GUILayout.Label(emotion.UsesProfile?"Usando tu perfil calibrado":"Sin calibrar: se usan reglas generales");
                GUILayout.Label("· "+VoiceProfileStore.status);
                if(GUILayout.Button("Borrar perfil de voz"))emotion.ResetProfile();
                if(virtualCamera){
                    GUILayout.Space(8);GUILayout.Label("Cámara virtual (FaceTime, Zoom, Meet…)");
                    bool mobile=Application.platform==RuntimePlatform.IPhonePlayer;
                    bool on=GUILayout.Toggle(virtualCamera.active,mobile?"Enviar al Mac como cámara «Traigger»":"Enviar a la cámara «Traigger»");
                    if(on!=virtualCamera.active){virtualCamera.active=on;PlayerPrefs.SetInt("traigger.virtualCamera",on?1:0);}
                    GUILayout.Label("· "+virtualCamera.status);
                    GUILayout.Label((mobile?"El Mac necesita Traigger Camera instalada y estar en la misma Wi-Fi; no hace falta abrir nada allí. ":"")+"En la videollamada elige la cámara «Traigger». El fondo es el color de «Fondo» de abajo.");
                }
                GUILayout.Space(8);
                if(avatarCamera){
                    GUILayout.Label("Fondo");
                    GUILayout.BeginHorizontal();
                    if(GUILayout.Button("Verde (OBS)"))avatarCamera.backgroundColor=Color.green;
                    if(GUILayout.Button("Oscuro"))avatarCamera.backgroundColor=new Color(.07f,.08f,.12f,0);
                    GUILayout.EndHorizontal();
                    if(GUILayout.Button("Avatar a pantalla 2")){if(Display.displays.Length>1){Display.displays[1].Activate();avatarCamera.targetDisplay=1;}}
                }
                volume=Slider("Sombras suaves",volume,0,.35f);
                if(manipulator&&!manipulator.gesturesLocked)GUILayout.Label("Arrastra al personaje con un dedo; pellizca para escalarlo. Tamaño: "+manipulator.scale.ToString("0.00"));
                GUILayout.Label(segmentedBody?"Cuerpo kemono · piezas preparadas":"Cuerpo · articulaciones independientes");
                GUILayout.Label("Atajos: Tab panel · Espacio boca · R reposo · 1 neutro · 2 alegre · 3 ojos cerrados");
                break;
            }
            GUILayout.EndVertical();GUILayout.EndScrollView();GUILayout.EndArea();
        }
        // Interfaz táctil e inmersiva (iPhone, iPad, Vision Pro): botones y pestañas grandes, deslizadores con tirador
        // amplio y fácil de pellizcar, texto más grande. En Mac con ratón se mantiene compacta.
        static bool bigUI;static GUIStyle sliderStyle,thumbStyle;
        static readonly string[] FacePresets={"Neutral","Sorpresa","Enfado","Pícaro","Carrillos","Tristeza","Alegría","Lengua + guiño","Olfatear"};
        void DrawFaceControls(){
            if(!facialExpressions){GUILayout.Label("Preparando controles de cara…");return;}
            for(int row=0;row<FacePresets.Length;row+=2){GUILayout.BeginHorizontal();for(int i=row;i<Mathf.Min(row+2,FacePresets.Length);i++)if(GUILayout.Button(FacePresets[i]))facialExpressions.Preset(i);GUILayout.EndHorizontal();}
            if(GUILayout.Button("Seguir mi cara · iPhone")){facialExpressions.manual=false;if(layeredEyes)layeredEyes.manual=false;tracking=true;}
            facialExpressions.manual=GUILayout.Toggle(facialExpressions.manual,"Controles manuales de cara");
            facialExpressions.intensity=Slider("Intensidad de las muecas",facialExpressions.intensity,0,1.5f);
            facialExpressions.noseExpressiveness=Slider("Expresividad de nariz",facialExpressions.noseExpressiveness,.3f,2.5f);
            facialExpressions.sniffMotion=GUILayout.Toggle(facialExpressions.sniffMotion,"Temblorcito de olfateo");
            facialExpressions.tongueSize=Slider("Tamaño de lengua",facialExpressions.tongueSize,.75f,2);
            if(layeredEyes){
                GUILayout.Label("Mirada · iris y pupilas");
                layeredEyes.manual=GUILayout.Toggle(layeredEyes.manual,"Mirada manual");
                if(GUILayout.Button("Mirada desde el iPhone")){layeredEyes.manual=false;tracking=true;}
                if(GUILayout.Button("Centrar mirada")){layeredEyes.manual=true;layeredEyes.manualLeft=layeredEyes.manualRight=Vector2.zero;}
                EyeControl("Ojo izquierdo · horizontal",ref layeredEyes.manualLeft.x);
                EyeControl("Ojo izquierdo · vertical",ref layeredEyes.manualLeft.y);
                EyeControl("Ojo derecho · horizontal",ref layeredEyes.manualRight.x);
                EyeControl("Ojo derecho · vertical",ref layeredEyes.manualRight.y);
                layeredEyes.horizontalRange=Slider("Recorrido horizontal de la mirada",layeredEyes.horizontalRange,.04f,.28f);
                layeredEyes.verticalRange=Slider("Recorrido vertical de la mirada",layeredEyes.verticalRange,.03f,.22f);
                layeredEyes.pupilScale=Slider("Tamaño de la pupila",layeredEyes.pupilScale,.75f,1.25f);
            }
            var f=facialExpressions.manualFrame;
            FaceControl("Abrir boca",ref f.jawOpen);
            FaceControl("Fruncir boca",ref f.mouthPucker);
            FaceControl("Sonrisa izquierda",ref f.mouthSmileLeft);
            FaceControl("Sonrisa derecha",ref f.mouthSmileRight);
            FaceControl("Guiño izquierdo",ref f.eyeBlinkLeft);
            FaceControl("Guiño derecho",ref f.eyeBlinkRight);
            FaceControl("Sacar lengua",ref f.tongueOut);
            FaceControl("Centro de las cejas",ref f.browInnerUp);
            FaceControl("Ceño izquierdo",ref f.browDownLeft);
            FaceControl("Ceño derecho",ref f.browDownRight);
            FaceControl("Ceja izquierda arriba",ref f.browOuterUpLeft);
            FaceControl("Ceja derecha arriba",ref f.browOuterUpRight);
            FaceControl("Ojo izquierdo abierto",ref f.eyeWideLeft);
            FaceControl("Ojo derecho abierto",ref f.eyeWideRight);
            FaceControl("Ojo izquierdo entrecerrado",ref f.eyeSquintLeft);
            FaceControl("Ojo derecho entrecerrado",ref f.eyeSquintRight);
            FaceControl("Inflar carrillos",ref f.cheekPuff);
            FaceControl("Mejilla izquierda arriba",ref f.cheekSquintLeft);
            FaceControl("Mejilla derecha arriba",ref f.cheekSquintRight);
            FaceControl("Comisura izquierda abajo",ref f.mouthFrownLeft);
            FaceControl("Comisura derecha abajo",ref f.mouthFrownRight);
            FaceControl("Boca a la izquierda",ref f.mouthLeft);
            FaceControl("Boca a la derecha",ref f.mouthRight);
            FaceControl("Presionar labio izquierdo",ref f.mouthPressLeft);
            FaceControl("Presionar labio derecho",ref f.mouthPressRight);
            FaceControl("Arrugar nariz izquierda",ref f.noseSneerLeft);
            FaceControl("Arrugar nariz derecha",ref f.noseSneerRight);
            GUILayout.Label("Mover un control activa la mueca manual. El seguimiento de cabeza y cuerpo continúa.");
        }
        void EyeControl(string label,ref float value){float next=Slider(label,value,-1,1);if(next!=value){value=next;layeredEyes.manual=true;}}
        void FaceControl(string label,ref float value){float next=Slider(label,value,0,1);if(next!=value){value=next;facialExpressions.manual=true;}}
        static void TouchStyles(bool touch){
            bigUI=touch;
            if(sliderStyle==null){sliderStyle=new GUIStyle(GUI.skin.horizontalSlider);thumbStyle=new GUIStyle(GUI.skin.horizontalSliderThumb);}
            int font=touch?18:12;
            GUI.skin.label.fontSize=GUI.skin.button.fontSize=GUI.skin.toggle.fontSize=font;
            GUI.skin.label.wordWrap=GUI.skin.toggle.wordWrap=GUI.skin.button.wordWrap=true;
            GUI.skin.button.fixedHeight=0;GUI.skin.button.padding=touch?new RectOffset(10,10,12,12):new RectOffset(6,6,3,3);
            GUI.skin.toggle.padding=touch?new RectOffset(34,4,10,10):new RectOffset(15,0,3,0);
            sliderStyle.fixedHeight=touch?36:12;thumbStyle.fixedHeight=touch?36:12;thumbStyle.fixedWidth=touch?36:10;
        }
        static float Slider(string label,float v,float a,float b){
            GUILayout.Label(label);
            float result=GUILayout.HorizontalSlider(v,a,b,sliderStyle,thumbStyle,GUILayout.Height(bigUI?40:14));
            if(bigUI)GUILayout.Space(4);
            return result;
        }
    }
}
