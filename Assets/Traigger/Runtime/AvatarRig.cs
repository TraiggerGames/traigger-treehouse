using UnityEngine;
namespace Traigger {
    public class AvatarRig : MonoBehaviour {
        public RigDefinition definition; public Transform[] bones; public SkinnedMeshRenderer[] layers;
        [System.NonSerialized] public bool ownsResources;
        public LineRenderer closedMouth;
        Quaternion[] rest; Vector3[] restPositions;
        int[,] shapeIndices;
        static readonly string[] shapeNames={"TurnRight","TurnLeft","NodUp","NodDown","HairLeftPositive","HairLeftNegative","HairRightPositive","HairRightNegative","HairFringePositive","HairFringeNegative","HairBackPositive","HairBackNegative"};
        public void CaptureRest() { rest=new Quaternion[bones.Length];restPositions=new Vector3[bones.Length]; for(int i=0;i<bones.Length;i++){rest[i]=bones[i].localRotation;restPositions[i]=bones[i].localPosition;}
            shapeIndices=new int[layers.Length,shapeNames.Length];
            for(int i=0;i<layers.Length;i++)for(int j=0;j<shapeNames.Length;j++)shapeIndices[i,j]=layers[i].sharedMesh.GetBlendShapeIndex(shapeNames[j]);
        }
        public void ResetPose() { if(rest==null) CaptureRest(); for(int i=0;i<bones.Length;i++){bones[i].localRotation=rest[i];bones[i].localPosition=restPositions[i];} }
        public void Rotate(string name,float degrees) {
            for(int i=0;i<bones.Length;i++) if(bones[i].name==name) bones[i].localRotation=rest[i]*Quaternion.Euler(0,0,degrees);
        }
        public void Face(FaceDeform channel,float value) {
            if(channel==FaceDeform.Mouth && closedMouth)closedMouth.enabled=value<=.04f;
            for(int i=0;i<layers.Length;i++) if(definition.layers[i].face==channel){
                int shape=layers[i].sharedMesh.GetBlendShapeIndex(channel.ToString());
                if(shape>=0)layers[i].SetBlendShapeWeight(shape,Mathf.Clamp01(value)*100);
                if(channel==FaceDeform.Mouth && closedMouth)layers[i].enabled=value>.04f;
            }
        }
        // Forma de la boca: positivo ensancha (sonrisa), negativo estrecha y redondea («o/u»).
        public void MouthShape(float shape){
            for(int i=0;i<layers.Length;i++)if(definition.layers[i].face==FaceDeform.Mouth){
                var mesh=layers[i].sharedMesh;int wide=mesh.GetBlendShapeIndex("MouthWide"),narrow=mesh.GetBlendShapeIndex("MouthNarrow");
                if(wide>=0)layers[i].SetBlendShapeWeight(wide,Mathf.Clamp01(shape)*100);
                if(narrow>=0)layers[i].SetBlendShapeWeight(narrow,Mathf.Clamp01(-shape)*100);
            }
            if(closedMouth)closedMouth.transform.localScale=new Vector3(1+Mathf.Clamp(shape,-1,1)*.35f,1,1);
        }
        public void Look(float yaw,float pitch){
            float x=Mathf.Clamp(yaw/18f,-1,1),y=Mathf.Clamp(pitch/12f,-1,1);
            for(int i=0;i<layers.Length;i++){SetCached(i,0,Mathf.Max(0,x));SetCached(i,1,Mathf.Max(0,-x));SetCached(i,2,Mathf.Max(0,y));SetCached(i,3,Mathf.Max(0,-y));}
            if(closedMouth)closedMouth.transform.localPosition=new Vector3(x*.140f,y*.026f,0);
        }
        public void HairMotion(float left,float right,float fringe,float back){
            for(int i=0;i<layers.Length;i++){SetSigned(i,4,left);SetSigned(i,6,right);SetSigned(i,8,fringe);SetSigned(i,10,back);}
        }
        void SetSigned(int layer,int channel,float value){SetCached(layer,channel,Mathf.Clamp(value,0,1.15f));SetCached(layer,channel+1,Mathf.Clamp(-value,0,1.15f));}
        void SetCached(int layer,int channel,float value){if(shapeIndices==null)CaptureRest();int index=shapeIndices[layer,channel];if(index>=0)layers[layer].SetBlendShapeWeight(index,value*100);}
        void OnDestroy() {
            // Sólo libera recursos creados en ejecución; los assets guardados pertenecen al editor.
            if(!ownsResources||layers==null)return;
            if(closedMouth)Destroy(closedMouth.sharedMaterial);
            foreach(var r in layers) { if(r){Destroy(r.sharedMesh);Destroy(r.sharedMaterial);} }
        }
    }
}
