using System;using UnityEngine;
namespace Traigger {
 // Pesos de tela específicos del tubo de calabaza. Coordenadas normalizadas del dibujo original.
 // La raíz acompaña al hombro, el panel se curva con los cinco ayudantes y el puño sigue al brazo.
 public static class OutfitSleeveBinding {
  public static void Bind(SkinnedMeshRenderer source,string side,Vector2[] uv,BoneWeight[] result){
   int Find(string name)=>Array.FindIndex(source.bones,b=>b&&b.name==name);
   var chain=new int[7];chain[0]=Find(side+"Shoulder");chain[6]=Find(side+"UpperArm");
   for(int n=1;n<6;n++)chain[n]=Find(side+"ShoulderBlend"+n);
   if(Array.Exists(chain,n=>n<0))throw new ArgumentException("Faltan ayudantes del hombro "+side);
   int spring=Find(side+"SleeveSway");
   Vector2 root=new Vector2(.84f,.16f),axis=new Vector2(.34f,.78f)-root;
   for(int i=0;i<uv.Length;i++){
    var p=new Vector2(side=="Right"?uv[i].x:1-uv[i].x,1-uv[i].y);
    float along=Vector2.Dot(p-root,axis)/axis.sqrMagnitude;
    float follow=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.08f,.66f,along));
    float step=follow*6;int a=Mathf.Min(5,Mathf.FloorToInt(step));float f=step-a;
    // Un poco de inercia al final; raíz anclada, frente/forro idénticos. El muelle ya pertenece al rig del cuerpo.
    float secondary=spring<0?0:Mathf.SmoothStep(0,1,Mathf.InverseLerp(.68f,.95f,along))*.07f;
    result[i]=new BoneWeight{boneIndex0=chain[a],weight0=(1-f)*(1-secondary),boneIndex1=chain[a+1],weight1=f*(1-secondary),boneIndex2=Mathf.Max(0,spring),weight2=secondary};
   }
  }
 }
}
