using UnityEngine;
namespace Traigger {
    // Resuelve hombro-codo-muñeca o cadera-rodilla-tobillo conservando las longitudes.
    public static class TwoBoneIK {
        public static void Solve(Transform root,Transform joint,Transform tip,Vector3 target,float bend,float weight=1){
            if(!root||!joint||!tip||weight<=0)return;
            Vector2 a=root.position,b=joint.position,c=tip.position,t=target;
            float upper=Vector2.Distance(a,b),lower=Vector2.Distance(b,c);
            if(upper<1e-4f||lower<1e-4f)return;
            Vector2 offset=t-a;float raw=offset.magnitude;
            if(raw<1e-4f)return;
            float distance=Mathf.Clamp(raw,Mathf.Abs(upper-lower)+.0001f,upper+lower-.0001f);
            Vector2 direction=offset/raw;
            float along=(upper*upper-lower*lower+distance*distance)/(2*distance);
            float across=Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
            Vector2 wantedJoint=a+direction*along+new Vector2(-direction.y,direction.x)*across*Mathf.Sign(bend);
            float angle=Vector2.SignedAngle(b-a,wantedJoint-a);
            root.rotation=Quaternion.AngleAxis(angle*weight,Vector3.forward)*root.rotation;
            Vector2 solvedTip=a+direction*distance;
            angle=Vector2.SignedAngle((Vector2)(tip.position-joint.position),solvedTip-(Vector2)joint.position);
            joint.rotation=Quaternion.AngleAxis(angle*weight,Vector3.forward)*joint.rotation;
        }
    }
}
