using UnityEngine;
namespace Traigger {
    // Correctivo sobre la malla existente: conserva huesos, pesos, UV y reposo del usuario.
    // La sección inferior queda fija; la curvatura crece hacia la punta como un cartílago.
    public sealed class EarFlex {
        readonly SkinnedMeshRenderer renderer;
        readonly Mesh original,working;
        readonly int positive,negative;
        public EarFlex(SkinnedMeshRenderer target){
            renderer=target;original=target.sharedMesh;
            working=Object.Instantiate(original);working.name=original.name+"_earFlex_runtime";
            var vertices=original.vertices;var bounds=original.bounds;
            float baseY=bounds.min.y+ bounds.size.y*.12f;
            float height=Mathf.Max(.001f,bounds.max.y-baseY);
            float centerX=bounds.center.x;
            foreach(float sign in new[]{1f,-1f}){
                var delta=new Vector3[vertices.Length];
                for(int i=0;i<vertices.Length;i++)delta[i]=Bend(vertices[i],baseY,height,centerX,sign*30)-vertices[i];
                working.AddBlendShapeFrame(sign>0?"CartilagePositive":"CartilageNegative",100,delta,new Vector3[vertices.Length],new Vector3[vertices.Length]);
            }
            positive=working.GetBlendShapeIndex("CartilagePositive");negative=working.GetBlendShapeIndex("CartilageNegative");
            renderer.sharedMesh=working;
        }
        public static Vector3 Bend(Vector3 vertex,float baseY,float height,float centerX,float degrees){
            if(vertex.y<=baseY)return vertex;
            float t=Mathf.Clamp01((vertex.y-baseY)/height);
            // Curvatura nula en la base: no se mueve toda la oreja alrededor de un pivote lejano.
            float angle=degrees*Mathf.Deg2Rad*t*t;
            float length=vertex.y-baseY;
            // Integración de la línea central mediante pequeños tramos, suave cerca de cero.
            float dx=0,dy=0;const int steps=16;
            for(int j=0;j<steps;j++){
                float u=(j+.5f)/steps*t,a=degrees*Mathf.Deg2Rad*u*u;
                dx-=Mathf.Sin(a)*length/steps;dy+=Mathf.Cos(a)*length/steps;
            }
            float width=vertex.x-centerX;
            return new Vector3(centerX+dx+width*Mathf.Cos(angle),baseY+dy+width*Mathf.Sin(angle),vertex.z);
        }
        public void Apply(float degrees){
            if(!renderer)return;
            renderer.SetBlendShapeWeight(positive,Mathf.Clamp01(degrees/30)*100);
            renderer.SetBlendShapeWeight(negative,Mathf.Clamp01(-degrees/30)*100);
        }
        public void Dispose(){
            if(renderer&&renderer.sharedMesh==working)renderer.sharedMesh=original;
            if(working){if(Application.isPlaying)Object.Destroy(working);else Object.DestroyImmediate(working);}
        }
    }
}
