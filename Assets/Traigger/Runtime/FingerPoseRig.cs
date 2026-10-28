using UnityEngine;
namespace Traigger {
 // Cuatro dedos kemono: pulgar, índice, medio y pareja anular/meñique humana.
 // Huesos adicionales de las manos: no se añaden ni se recolocan huesos del rig corporal.
 [DefaultExecutionOrder(750)] public class FingerPoseRig:MonoBehaviour {
  public Mesh sourceMesh;public Transform[] sourceBones;
  public Transform[] joints=new Transform[0];public Quaternion[] rest=new Quaternion[0];
  public bool experimentalDeformation=false;
  public bool leftHuman=true,manual;public float[] controls=new float[4];public float response=12;
  float[] current=new float[4];VisionMotionInput input;
  void LateUpdate(){Evaluate(Time.unscaledDeltaTime);}
  public void Evaluate(float dt){
   if(!input)input=FindAnyObjectByType<VisionMotionInput>();
   bool live=input&&Time.unscaledTime-(leftHuman?input.lastLeftHand:input.lastRightHand)<.3f;
   var curls=input?(leftHuman?input.leftCurls:input.rightCurls):null;
   for(int finger=0;finger<4;finger++){
    float target=!experimentalDeformation?0:manual?controls[finger]:live?(finger<3?curls[finger]:(curls[3]+curls[4])*.5f):0f;
    if(float.IsNaN(target)||float.IsInfinity(target))target=0;
    current[finger]=Mathf.Lerp(current[finger],Mathf.Clamp01(target),1-Mathf.Exp(-response*Mathf.Clamp(dt,0,.25f)));
    float direction=(finger==0?-1:1)*(leftHuman?1:-1);
    for(int segment=0;segment<3;segment++){int j=finger*3+segment;if(j<joints.Length&&joints[j])joints[j].localRotation=rest[j]*Quaternion.Euler(0,0,direction*current[finger]*(finger==0?12:14));}
   }
  }
  public void Pose(float thumb,float index,float middle,float outer){manual=true;controls=new[]{thumb,index,middle,outer};}
  void OnDisable(){for(int i=0;i<joints.Length;i++)if(joints[i])joints[i].localRotation=rest[i];}
 }
}
