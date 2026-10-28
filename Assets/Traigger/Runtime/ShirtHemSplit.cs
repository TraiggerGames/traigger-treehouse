using System;
using UnityEngine;
namespace Traigger {
    // Comparte deformación con el torso: sólo cambia qué píxeles van detrás de los pantalones.
    [ExecuteAlways,DefaultExecutionOrder(600)]
    public class ShirtHemSplit:MonoBehaviour {
        public KemonoBodyRig body;
        public SkinnedMeshRenderer front,rear;
        public void Sync(){
            if(!front||!rear||!body)return;
            rear.sharedMesh=front.sharedMesh;rear.bones=front.bones;rear.rootBone=front.rootBone;
            rear.sortingLayerID=front.sortingLayerID;
            int order=front.sortingOrder-1;
            foreach(var part in body.parts)if(part&&(part.name.Contains("Pelvis")||part.name.Contains("Thigh")))order=Mathf.Min(order,part.sortingOrder-1);
            rear.sortingOrder=order;rear.enabled=front.enabled;
            rear.sharedMaterial.SetFloat("_Volume",front.sharedMaterial.GetFloat("_Volume"));
        }
        void LateUpdate(){Sync();}
        public static ShirtHemSplit Install(KemonoBodyRig body){
            if(body.GetComponent<ShirtHemSplit>())throw new ArgumentException("El bajo ya está separado");
            var split=body.gameObject.AddComponent<ShirtHemSplit>();split.body=body;split.front=body.torsoRenderer;
            var front=split.front;front.sharedMaterial=new Material(front.sharedMaterial){name="ShirtFront"};front.sharedMaterial.SetFloat("_HemSplit",-1);
            var go=new GameObject("Kemono_ShirtRearHem");go.transform.SetParent(front.transform,false);
            var rear=go.AddComponent<SkinnedMeshRenderer>();split.rear=rear;
            rear.quality=front.quality;rear.localBounds=front.localBounds;rear.updateWhenOffscreen=true;
            rear.sharedMaterial=new Material(front.sharedMaterial){name="ShirtRearHem"};rear.sharedMaterial.SetFloat("_HemSplit",1);
            split.Sync();return split;
        }
    }
}
