using System;
using System.Collections.Generic;
using UnityEngine;
namespace Traigger {
    // Correctivos 2D aditivos: conservan texturas, huesos y forma neutral.
    // Las copias de malla viven sólo durante la sesión; nunca se sobreescribe el arte del usuario.
    public class FacialExpressionRig : MonoBehaviour {
        public AvatarRig rig;
        public bool manual;
        [Range(0,1.5f)] public float intensity=1.25f;
        [Range(.75f,2f)] public float tongueSize=1.4f;
        [Range(.3f,2.5f)] public float noseExpressiveness=.6f;
        public bool sniffMotion=true;
        // Olfateo de gato reconocido (SniffDetector): la nariz hace sus tironcitos aunque no esté arrugada, el hocico se
        // levanta un poco y no se copian ojos, cejas ni mejillas (la mueca humana los arrastra).
        [HideInInspector] public float catSniff;
        // Olfateo: ráfagas cortas (2–3 olfateos rápidos) con pausas, solo con la nariz arrugada a propósito y la boca
        // cerrada; antes era una onda continua que hacía temblar la nariz al hablar.
        float sniffPhase,sniffHeld,sniffRest,sniffEnvelope;int sniffsLeft;
        // Índices de blendshapes por malla, calculados una vez (antes se buscaban por nombre en cada fotograma).
        class ShapeIndices{public int sniffPositive=-1,sniffNegative=-1,blink=-1;public bool leftEye;public int[] channels=new int[20];}
        readonly Dictionary<SkinnedMeshRenderer,ShapeIndices> shapeIndices=new Dictionary<SkinnedMeshRenderer,ShapeIndices>();
        ShapeIndices Indices(SkinnedMeshRenderer renderer){
            var mesh=renderer.sharedMesh;
            if(shapeIndices.TryGetValue(renderer,out var cached)&&cached!=null)return cached;
            var found=new ShapeIndices{sniffPositive=mesh.GetBlendShapeIndex("SniffTwitchPositive"),sniffNegative=mesh.GetBlendShapeIndex("SniffTwitchNegative")};
            int left=mesh.GetBlendShapeIndex("BlinkLeft");found.blink=left>=0?left:mesh.GetBlendShapeIndex("BlinkRight");found.leftEye=left>=0;
            for(int c=0;c<20;c++)found.channels[c]=mesh.GetBlendShapeIndex("Expression_"+Channels[c]);
            shapeIndices[renderer]=found;return found;
        }
        public FaceFrame manualFrame=new FaceFrame();
        readonly Dictionary<SkinnedMeshRenderer,Mesh> originals=new Dictionary<SkinnedMeshRenderer,Mesh>();
        readonly List<Mesh> copies=new List<Mesh>();
        Vector3[] closedRest;
        SpriteRenderer tongueRenderer;Sprite tongueSprite;Material tongueMaterial;float tongueAmount,tongueSide;
        readonly float[] smooth=new float[20];
        public static readonly string[] Channels={"browInnerUp","browDownLeft","browDownRight","browOuterUpLeft","browOuterUpRight","eyeWideLeft","eyeWideRight","eyeSquintLeft","eyeSquintRight","cheekPuff","cheekSquintLeft","cheekSquintRight","mouthFrownLeft","mouthFrownRight","mouthLeft","mouthRight","mouthPressLeft","mouthPressRight","noseSneerLeft","noseSneerRight"};
        SkinnedMeshRenderer mouthRenderer;Material mouthOriginalMaterial,mouthRuntimeMaterial;
        bool initialized;float smileLeft,smileRight;
        public void Initialize(){
            if(initialized||!rig)return;initialized=true;
            if(rig.closedMouth){closedRest=new Vector3[rig.closedMouth.positionCount];rig.closedMouth.GetPositions(closedRest);}
            for(int layer=0;layer<rig.layers.Length;layer++){
                var renderer=rig.layers[layer];if(!renderer)continue;
                string name=rig.definition.layers[layer].name;
                bool cheekOverlay=name=="CheekLeft"||name=="CheekRight";
                if(name!="Eyebrows"&&name!="HeadBase"&&!cheekOverlay&&name!="Mouth"&&name!="EyeLeft"&&name!="EyeRight")continue;
                if(name=="Mouth"){
                    mouthRenderer=renderer;mouthOriginalMaterial=renderer.sharedMaterial;
                    mouthRuntimeMaterial=new Material(mouthOriginalMaterial);renderer.sharedMaterial=mouthRuntimeMaterial;
                    var rect=rig.definition.layers[layer].uvRect;
                    mouthRuntimeMaterial.SetVector("_LayerRect",new Vector4(rect.x,rect.y,rect.width,rect.height));
                }
                Mesh source=renderer.sharedMesh,mesh=Instantiate(source);mesh.name=source.name+" · facial runtime";
                originals.Add(renderer,source);copies.Add(mesh);renderer.sharedMesh=mesh;
                var vertices=mesh.vertices;var bounds=mesh.bounds;var zero=new Vector3[vertices.Length];
                for(int channel=0;channel<20;channel++){
                    var delta=new Vector3[vertices.Length];bool changes=false;
                    for(int i=0;i<vertices.Length;i++){
                        var v=vertices[i];float nx=Mathf.Clamp((v.x-bounds.center.x)/Mathf.Max(.001f,bounds.extents.x),-1,1);
                        float ny=Mathf.Clamp((v.y-bounds.center.y)/Mathf.Max(.001f,bounds.extents.y),-1,1);
                        // El lado izquierdo corresponde al usuario, como el rig y el modo espejo existentes.
                        float left=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.035f,.035f,v.x));float right=1-left;
                        if(name=="Eyebrows"){
                            float inner=1-Mathf.Abs(nx),outer=Mathf.Abs(nx);
                            if(channel==0)delta[i].y=.11f*inner;
                            if(channel==1||channel==2)delta[i].y=-.09f*inner*(channel==1?left:right);
                            if(channel==3||channel==4)delta[i].y=.10f*outer*(channel==3?left:right);
                        }
                        bool eye=name=="EyeLeft"||name=="EyeRight";bool isLeft=name=="EyeLeft";
                        if(eye){
                            if(channel==(isLeft?5:6))delta[i].y=(v.y-bounds.center.y)*.38f;
                            // El párpado interior acompaña el ceño aunque la ceja quede bajo el flequillo.
                            if(channel==(isLeft?1:2))delta[i].y=-.09f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.1f,1,ny))*(.3f+.7f*(isLeft?(nx+1)*.5f:(1-nx)*.5f));
                            if(channel==(isLeft?7:8))delta[i].y=-(v.y-bounds.center.y)*.64f;
                            if(channel==(isLeft?10:11))delta[i].y=-(v.y-bounds.center.y)*.22f+.018f;
                        }
                        if(name=="HeadBase"||cheekOverlay){
                            // Los recortes de pelaje reutilizan la cara: deben seguir exactamente el mismo campo.
                            // Si se quedan quietos al inflar la cara, aparecen dos siluetas del mismo mechón.
                            // Zona baja/lateral: no se ensanchan ojos, nariz, cráneo ni pelo.
                            float cheek=Mathf.Exp(-Mathf.Pow((v.y-3.48f)/.18f,2))*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.28f,.56f,Mathf.Abs(v.x)));
                            if(channel==9)delta[i].x=Mathf.Sign(v.x)*.060f*cheek;
                            if(channel==10||channel==11)delta[i].y=.028f*cheek*(channel==10?left:right);
                            if(name=="HeadBase"&&(channel==18||channel==19)){
                                // Arrugar cada lado eleva y ensancha el morrito; el cráneo y el pelaje lateral quedan fijos.
                                float muzzle=NoseMask(v),side=channel==18?left:right;
                                delta[i]=new Vector3((v.x*.18f+(channel==18?-.022f:.022f))*.8f,.075f,0)*muzzle*side;
                            }
                        }
                        if(name=="Mouth"){
                            float edge=Mathf.Abs(nx)*Mathf.Abs(nx);
                            if(channel==12||channel==13)delta[i].y=-.065f*edge*(channel==12?left:right);
                            if(channel==14)delta[i].x=-.050f;if(channel==15)delta[i].x=.050f;
                            if(channel==18||channel==19)delta[i].y=.020f*(1-Mathf.Abs(nx))*(channel==18?left:right);
                            if(channel==16||channel==17)delta[i].y=-(v.y-bounds.center.y)*.35f*(channel==16?left:right);
                        }
                        changes|=delta[i]!=Vector3.zero;
                    }
                    if(changes)mesh.AddBlendShapeFrame("Expression_"+Channels[channel],100,delta,zero,zero);
                }
            }
            int noseHead=Array.FindIndex(rig.definition.layers,l=>l.name=="HeadBase");
            if(noseHead>=0){var mesh=rig.layers[noseHead].sharedMesh;var vertices=mesh.vertices;var zero=new Vector3[vertices.Length];
                for(int sign=0;sign<2;sign++){var delta=new Vector3[vertices.Length];for(int i=0;i<delta.Length;i++)delta[i]=new Vector3(.006f,.014f,0)*NoseMask(vertices[i])*(sign==0?1:-1);mesh.AddBlendShapeFrame("SniffTwitch"+(sign==0?"Positive":"Negative"),100,delta,zero,zero);}
            }
            SynchronizeCheekCorrections();
        }
        // Olfateo natural: con la nariz arrugada a propósito (≥ 0,35 durante 0,25 s) y la boca cerrada, ráfagas de 2–3
        // olfateos rápidos (≈ 6 por segundo) y luego una pausa de 1,2–2,8 s. Devuelve el desplazamiento (−1…1).
        float Sniff(float nose,float jaw,float dt){
            bool wants=(nose>.3f||catSniff>.4f)&&jaw<.12f;
            sniffHeld=wants?sniffHeld+dt:0;sniffRest-=dt;
            // Reconocido como olfateo: ráfagas seguidas, con pausas cortas, mientras dure.
            if(catSniff>.4f)sniffRest=Mathf.Min(sniffRest,.25f);
            if(sniffsLeft==0&&sniffHeld>(catSniff>.4f?.05f:.25f)&&sniffRest<=0){sniffsLeft=UnityEngine.Random.Range(2,4);sniffPhase=0;}
            if(sniffsLeft>0){
                sniffPhase+=dt*Mathf.PI*2*6;
                if(sniffPhase>=Mathf.PI*2){sniffPhase-=Mathf.PI*2;if(--sniffsLeft==0)sniffRest=UnityEngine.Random.Range(1.2f,2.8f);}
            }
            // Envolvente suave para que la ráfaga entre y salga sin golpes; se corta en cuanto se habla.
            sniffEnvelope=Mathf.MoveTowards(sniffEnvelope,sniffsLeft>0&&jaw<.12f?1:0,dt*8);
            // Cada olfateo es un tirón corto hacia arriba (no una onda simétrica).
            float pulse=sniffsLeft>0?Mathf.Pow(Mathf.Max(0,Mathf.Sin(sniffPhase)),2):0;
            return pulse*sniffEnvelope*Mathf.Max(Mathf.Lerp(.1f,.2f,nose),.22f*catSniff);
        }
        static float NoseMask(Vector3 v){return Mathf.Exp(-Mathf.Pow((v.y-3.47f)/.15f,2))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.20f,.32f,Mathf.Abs(v.x))));}
        // Los recortes tienen otra densidad de malla. Muestrear el correctivo real de HeadBase
        // por UV evita una segunda silueta incluso con la intensidad máxima.
        void SynchronizeCheekCorrections(){
            int headIndex=Array.FindIndex(rig.definition.layers,l=>l.name=="HeadBase");if(headIndex<0)return;
            var head=rig.layers[headIndex];var mesh=head.sharedMesh;var uv=mesh.uv;var triangles=mesh.triangles;
            foreach(var pair in originals){
                int layer=Array.IndexOf(rig.layers,pair.Key);string name=rig.definition.layers[layer].name;
                if(name!="CheekLeft"&&name!="CheekRight")continue;
                var cheek=pair.Key;var target=cheek.sharedMesh;var targetUV=target.uv;
                var frames=new List<(string name,float weight,Vector3[] delta,Vector3[] normal,Vector3[] tangent)>();
                for(int shape=0;shape<target.blendShapeCount;shape++)for(int frame=0;frame<target.GetBlendShapeFrameCount(shape);frame++){
                    string shapeName=target.GetBlendShapeName(shape);var delta=new Vector3[target.vertexCount];var normal=new Vector3[target.vertexCount];var tangent=new Vector3[target.vertexCount];
                    target.GetBlendShapeFrameVertices(shape,frame,delta,normal,tangent);
                    if(shapeName=="Expression_cheekPuff"||shapeName=="Expression_cheekSquintLeft"||shapeName=="Expression_cheekSquintRight"){
                        int source=mesh.GetBlendShapeIndex(shapeName);if(source>=0){
                            var headDelta=new Vector3[mesh.vertexCount];mesh.GetBlendShapeFrameVertices(source,0,headDelta,null,null);
                            for(int vertex=0;vertex<delta.Length;vertex++)if(SampleFace(targetUV[vertex],uv,triangles,headDelta,out var sampled))delta[vertex]=cheek.transform.InverseTransformVector(head.transform.TransformVector(sampled));
                        }
                    }
                    frames.Add((shapeName,target.GetBlendShapeFrameWeight(shape,frame),delta,normal,tangent));
                }
                target.ClearBlendShapes();foreach(var frame in frames)target.AddBlendShapeFrame(frame.name,frame.weight,frame.delta,frame.normal,frame.tangent);
            }
        }
        static bool SampleFace(Vector2 point,Vector2[] uv,int[] indices,Vector3[] values,out Vector3 value){
            for(int i=0;i<indices.Length;i+=3){int ia=indices[i],ib=indices[i+1],ic=indices[i+2];var a=uv[ia];var b=uv[ib];var c=uv[ic];
                float d=(b.y-c.y)*(a.x-c.x)+(c.x-b.x)*(a.y-c.y);if(Mathf.Abs(d)<1e-12f)continue;
                float x=((b.y-c.y)*(point.x-c.x)+(c.x-b.x)*(point.y-c.y))/d;
                float y=((c.y-a.y)*(point.x-c.x)+(a.x-c.x)*(point.y-c.y))/d,z=1-x-y;
                if(x>=-.0001f&&y>=-.0001f&&z>=-.0001f){value=values[ia]*x+values[ib]*y+values[ic]*z;return true;}
            }
            value=Vector3.zero;return false;
        }
        void CreateTongue(){
            if(tongueRenderer||!rig.closedMouth||closedRest==null)return;
            var texture=Resources.Load<Texture2D>("TraiggerTongue-v2");if(!texture)return;
            // Recorte UV del dibujo generado: conserva el PNG y su canal alfa sin repintarlo.
            float k=texture.width/1254f;
            tongueSprite=Sprite.Create(texture,new Rect(310*k,335*k,634*k,576*k),new Vector2(.5f,1),5000*k);
            var node=new GameObject("Tongue — kemono");node.transform.SetParent(rig.closedMouth.transform,false);
            tongueRenderer=node.AddComponent<SpriteRenderer>();tongueRenderer.sprite=tongueSprite;
            var shader=Resources.Load<Shader>("TongueAlpha");if(shader){tongueMaterial=new Material(shader);tongueRenderer.sharedMaterial=tongueMaterial;}
            tongueRenderer.sortingLayerID=rig.closedMouth.sortingLayerID; tongueRenderer.sortingOrder=rig.closedMouth.sortingOrder+2;
            tongueRenderer.enabled=false;
        }
        public void Step(FaceFrame live,float dt){
            Initialize();if(!initialized)return;
            FaceFrame frame=manual?manualFrame:live;
            if(frame!=null&&!FaceReceiver.Finite(frame))frame=null;
            CreateTongue();
            float[] values=Read(frame);dt=Mathf.Clamp(dt,0,.1f);float blend=1-Mathf.Exp(-18*dt);
            // Al hablar, el seguimiento ve algo de «arrugar la nariz» (la boca mueve esa zona): se atenúa con la boca
            // abierta, salvo en modo manual o con la expresión elegida a mano.
            float jaw=frame==null?0:Mathf.Clamp01(frame.jawOpen);
            float speechDamp=manual?1:1-.85f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.04f,.25f,jaw));
            for(int c=0;c<20;c++){
                float value=Mathf.Clamp01(values[c]);
                // Nariz: zona muerta amplia con entrada suave, para que solo cuente el gesto hecho a propósito.
                if(c>=18)value=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.18f,.85f,value*speechDamp))*Mathf.Clamp(noseExpressiveness,.3f,2.5f);
                float target=value*Mathf.Clamp(intensity,0,1.5f);if(c>=18)target=Mathf.Min(.65f,target);
                if(catSniff>0&&!manual){
                    if(c>=18)target=Mathf.Lerp(target,.18f,catSniff);                            // hocico algo levantado
                    else if(c<=2||(c>=5&&c<=8)||c==10||c==11)target*=1-catSniff;               // cejas, ojos y mejillas quietos
                }
                // La nariz sube rápido y baja despacio: sin parpadeos de un fotograma.
                float speed=c>=18&&target<smooth[c]?1-Mathf.Exp(-6*dt):blend;
                smooth[c]=Mathf.Lerp(smooth[c],target,speed);
            }
            float noseAmount=Mathf.Clamp01((smooth[18]+smooth[19])*.5f);
            float twitch=sniffMotion?Sniff(noseAmount,jaw,dt):0;
            foreach(var pair in originals){var renderer=pair.Key;if(!renderer)continue;
                var shapes=Indices(renderer);
                if(shapes.sniffPositive>=0){renderer.SetBlendShapeWeight(shapes.sniffPositive,Mathf.Max(0,twitch)*100);if(shapes.sniffNegative>=0)renderer.SetBlendShapeWeight(shapes.sniffNegative,Mathf.Max(0,-twitch)*100);}
                int blink=shapes.blink;
                float eyelidRoom=blink<0?1:1-Mathf.Clamp01(renderer.GetBlendShapeWeight(blink)/100);
                bool leftEye=shapes.leftEye;
                float compression=blink<0?0:.64f*smooth[leftEye?7:8]+.22f*smooth[leftEye?10:11]+.45f*smooth[leftEye?1:2];
                eyelidRoom*=compression>.80f?.80f/compression:1;
                // No sumar un entrecerrado sobre un ojo ya cerrado: evita invertir su malla.
                for(int c=0;c<20;c++){int index=shapes.channels[c];if(index>=0)renderer.SetBlendShapeWeight(index,smooth[c]*100*(blink>=0?eyelidRoom:1));}
            }
            tongueAmount=Mathf.Lerp(tongueAmount,frame==null?0:Mathf.Clamp01(frame.tongueOut),blend);
            if(mouthRuntimeMaterial)mouthRuntimeMaterial.SetFloat("_TongueOut",tongueRenderer?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.015f,.12f,tongueAmount)):0);
            if(tongueRenderer){
                tongueRenderer.enabled=tongueAmount>.025f;
                tongueSide=Mathf.Lerp(tongueSide,frame==null?0:Mathf.Clamp(frame.mouthRight-frame.mouthLeft,-1,1),blend);
                float side=tongueSide;
                var root=closedRest[closedRest.Length/2];
                // Introducir la raíz en la cavidad; compensar el tramo oculto conserva la longitud exterior.
                tongueRenderer.transform.localPosition=root+new Vector3(side*.045f,.024f,0);
                tongueRenderer.transform.localScale=new Vector3((.85f+.15f*tongueAmount)*Mathf.Clamp(tongueSize,.75f,2f),(.15f+.85f*tongueAmount)*Mathf.Clamp(tongueSize,.75f,2f)+.032f/(576f/5000f),1);
                tongueRenderer.transform.localRotation=Quaternion.Euler(0,0,-side*15);
                if(tongueAmount>.025f)rig.closedMouth.enabled=false;
            }
            // La boca neutral sigue siendo la curva kemono, con comisuras independientes.
            if(closedRest!=null&&rig.closedMouth){
                smileLeft=Mathf.Lerp(smileLeft,frame==null?0:Mathf.Clamp01(frame.mouthSmileLeft),blend);smileRight=Mathf.Lerp(smileRight,frame==null?0:Mathf.Clamp01(frame.mouthSmileRight),blend);
                float smileL=smileLeft,smileR=smileRight;
                for(int i=0;i<closedRest.Length;i++){
                    float x=i/(float)(closedRest.Length-1),left=1-x,right=x;
                    var v=closedRest[i];
                    v.y+=.010f*(smooth[18]*left+smooth[19]*right)*(1-Mathf.Abs(x*2-1));
                    v.y+=.075f*(smileL*left*left+smileR*right*right)*intensity;
                    v.y-=.075f*(smooth[12]*left*left+smooth[13]*right*right);
                    float press=Mathf.Clamp01((smooth[16]+smooth[17])*.5f);float centerY=closedRest[closedRest.Length/2].y;
                    v.y=Mathf.Lerp(v.y,centerY,press*.6f);
                    float centerX=(closedRest[0].x+closedRest[closedRest.Length-1].x)*.5f;
                    v.x=centerX+(v.x-centerX)*(1-press*.4f)+(smooth[15]-smooth[14])*.050f;rig.closedMouth.SetPosition(i,v);
                }
            }
        }
        public static float[] Read(FaceFrame f){return f==null?new float[20]:new[]{f.browInnerUp,f.browDownLeft,f.browDownRight,f.browOuterUpLeft,f.browOuterUpRight,f.eyeWideLeft,f.eyeWideRight,f.eyeSquintLeft,f.eyeSquintRight,f.cheekPuff,f.cheekSquintLeft,f.cheekSquintRight,f.mouthFrownLeft,f.mouthFrownRight,f.mouthLeft,f.mouthRight,f.mouthPressLeft,f.mouthPressRight,f.noseSneerLeft,f.noseSneerRight};}
        public void Preset(int pose){
            manual=true;manualFrame=new FaceFrame();var f=manualFrame;
            if(pose==1){f.jawOpen=.85f;f.mouthPucker=.8f;f.browInnerUp=.6f;f.browOuterUpLeft=f.browOuterUpRight=.6f;f.eyeWideLeft=f.eyeWideRight=.8f;}
            if(pose==2){f.browDownLeft=f.browDownRight=.85f;f.eyeSquintLeft=f.eyeSquintRight=.5f;f.mouthFrownLeft=f.mouthFrownRight=.7f;}
            if(pose==3){f.eyeBlinkRight=.7f;f.browOuterUpLeft=.9f;f.eyeSquintRight=.7f;f.mouthRight=.7f;f.mouthSmileLeft=.8f;}
            if(pose==4){f.cheekPuff=1;f.mouthPressLeft=f.mouthPressRight=.9f;}
            if(pose==5){f.browInnerUp=.8f;f.mouthFrownLeft=f.mouthFrownRight=.8f;f.eyeSquintLeft=f.eyeSquintRight=.25f;}
            if(pose==8){f.noseSneerLeft=f.noseSneerRight=.7f;f.browInnerUp=.25f;}
            if(pose==7){f.tongueOut=1;f.jawOpen=.4f;f.eyeBlinkRight=1;f.browOuterUpLeft=.9f;}
            if(pose==6){f.jawOpen=.7f;f.mouthSmileLeft=f.mouthSmileRight=1;f.cheekSquintLeft=f.cheekSquintRight=.8f;f.eyeSquintLeft=f.eyeSquintRight=.5f;}
        }
        void OnDestroy(){Release();}
        public void Release(){
            if(mouthRenderer&&mouthRenderer.sharedMaterial==mouthRuntimeMaterial)mouthRenderer.sharedMaterial=mouthOriginalMaterial;
            if(mouthRuntimeMaterial){if(Application.isPlaying)Destroy(mouthRuntimeMaterial);else DestroyImmediate(mouthRuntimeMaterial);mouthRuntimeMaterial=null;}
            mouthRenderer=null;mouthOriginalMaterial=null;
            if(tongueRenderer){if(Application.isPlaying)Destroy(tongueRenderer.gameObject);else DestroyImmediate(tongueRenderer.gameObject);tongueRenderer=null;}
            if(tongueMaterial){if(Application.isPlaying)Destroy(tongueMaterial);else DestroyImmediate(tongueMaterial);tongueMaterial=null;}
            if(tongueSprite){if(Application.isPlaying)Destroy(tongueSprite);else DestroyImmediate(tongueSprite);tongueSprite=null;}
            foreach(var pair in originals)if(pair.Key&&pair.Key.sharedMesh&&copies.Contains(pair.Key.sharedMesh))pair.Key.sharedMesh=pair.Value;
            foreach(var mesh in copies)if(mesh){if(Application.isPlaying)Destroy(mesh);else DestroyImmediate(mesh);}
            if(rig&&rig.closedMouth&&closedRest!=null)rig.closedMouth.SetPositions(closedRest);
            originals.Clear();copies.Clear();Array.Clear(smooth,0,smooth.Length);sniffPhase=sniffHeld=sniffRest=sniffEnvelope=0;sniffsLeft=0;shapeIndices.Clear();initialized=false;
        }
    }
}
