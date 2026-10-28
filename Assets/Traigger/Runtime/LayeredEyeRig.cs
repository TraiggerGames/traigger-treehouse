using System;using System.Collections.Generic;using UnityEngine;
namespace Traigger {
 // Tres capas de renderizado con el mismo atlas: no se modifica ni sustituye el dibujo original.
 // El contorno permanece fijo; iris y pupila se desplazan dentro de la abertura de los párpados.
 public class LayeredEyeRig:MonoBehaviour {
  public AvatarRig rig;
  public bool manual;
  public Vector2 manualLeft,manualRight;
  [Range(.04f,.28f)]public float horizontalRange=.16f;
  [Range(.03f,.22f)]public float verticalRange=.12f;
  [Range(.75f,1.25f)]public float pupilScale=1;
  public Vector2 leftGaze {get;private set;}public Vector2 rightGaze {get;private set;}
  class Eye {public SkinnedMeshRenderer source;public Material original;public Material[] materials;public int order;public bool left;}
  readonly List<Eye> eyes=new List<Eye>();
  public void Initialize(){
   if(eyes.Count>0||!rig)return;var shader=Resources.Load<Shader>("LayeredEye");if(!shader)return;
   for(int i=0;i<rig.layers.Length;i++){
    string name=rig.definition.layers[i].name;if(name!="EyeLeft"&&name!="EyeRight")continue;
    var source=rig.layers[i];if(!source||!source.sharedMesh)continue;
    var eye=new Eye{source=source,original=source.sharedMaterial,order=source.sortingOrder,left=name=="EyeLeft"};
    eye.materials=new Material[1];var spec=rig.definition.layers[i];
    for(int layer=0;layer<1;layer++){
     var material=new Material(shader){name=name+" · blanco, iris y pupila"};
     material.mainTexture=eye.original.mainTexture;material.color=eye.original.color;
     material.SetVector("_EyeUV",new Vector4(spec.uvRect.x,spec.uvRect.y,spec.uvRect.width,spec.uvRect.height));
     material.SetVector("_Iris",eye.left?new Vector4(128f/212,1-99f/182,56f/212,80f/182):new Vector4(85f/214,1-99f/182,56f/214,80f/182));
     material.SetVector("_Pupil",eye.left?new Vector4(129f/212,1-98f/182,7f/212,21f/182):new Vector4(84f/214,1-98f/182,7f/214,21f/182));
     eye.materials[layer]=material;
    }
    source.sharedMaterial=eye.materials[0];eyes.Add(eye);
   }
  }
  public void Step(FaceFrame frame,Vector2 fallback,float dt){
   Initialize();
   Vector2 left=fallback,right=fallback;
   if(frame!=null&&frame.independentEyes){left=new Vector2(frame.eyeLookLeftX,frame.eyeLookLeftY);right=new Vector2(frame.eyeLookRightX,frame.eyeLookRightY);}
   if(manual){left=manualLeft;right=manualRight;}
   float blend=1-Mathf.Exp(-26*Mathf.Clamp(dt,0,.1f));leftGaze=Vector2.Lerp(leftGaze,Clamp(left),blend);rightGaze=Vector2.Lerp(rightGaze,Clamp(right),blend);
   foreach(var eye in eyes){
    var source=eye.source;var mesh=source.sharedMesh;if(!mesh)continue;
    var gaze=eye.left?leftGaze:rightGaze;
    foreach(var material in eye.materials){
     material.SetVector("_Gaze",new Vector4(gaze.x*horizontalRange,gaze.y*verticalRange,0,0));material.SetFloat("_PupilScale",Mathf.Clamp(pupilScale,.75f,1.25f));material.SetFloat("_Blink",Weight(source,eye.left?"BlinkLeft":"BlinkRight"));
    }
   }
  }
  static Vector2 Clamp(Vector2 v){if(float.IsNaN(v.x)||float.IsInfinity(v.x)||float.IsNaN(v.y)||float.IsInfinity(v.y))return Vector2.zero;return new Vector2(Mathf.Clamp(v.x,-1,1),Mathf.Clamp(v.y,-1,1));}
  static float Weight(SkinnedMeshRenderer r,string name){int i=r.sharedMesh.GetBlendShapeIndex(name);return i<0?0:Mathf.Clamp01(r.GetBlendShapeWeight(i)/100);}
  void OnDisable(){Release();}void OnDestroy(){Release();}
  public void Release(){foreach(var eye in eyes){if(eye.source){eye.source.sharedMaterial=eye.original;eye.source.sortingOrder=eye.order;}foreach(var material in eye.materials)if(material)Remove(material);}eyes.Clear();}
  static void Remove(UnityEngine.Object value){if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
 }
}
