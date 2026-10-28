using UnityEngine;
namespace Traigger {
    // Tono de la voz (frecuencia fundamental) por autocorrelación normalizada. Se reduce la señal a 12 kHz para que
    // sea barato; cubre 70–400 Hz (voces graves y agudas). Devuelve 0 si el trozo no es voz con tono (silencio, «s»).
    public static class PitchTracker {
        public static float Estimate(float[] samples,int count,int sampleRate){
            int step=Mathf.Max(1,sampleRate/12000);float rate=sampleRate/(float)step;
            int n=count/step;if(n<256)return 0;
            var x=new float[n];float mean=0;
            for(int i=0;i<n;i++){float s=0;for(int k=0;k<step;k++)s+=samples[i*step+k];x[i]=s/step;mean+=x[i];}
            mean/=n;for(int i=0;i<n;i++)x[i]-=mean;
            int minLag=Mathf.FloorToInt(rate/400),maxLag=Mathf.Min(n/2,Mathf.CeilToInt(rate/70));
            float best=0;int bestLag=0;
            for(int lag=minLag;lag<=maxLag;lag++){
                float c=0,e0=0,e1=0;
                for(int i=0;i+lag<n;i++){c+=x[i]*x[i+lag];e0+=x[i]*x[i];e1+=x[i+lag]*x[i+lag];}
                float r=c/Mathf.Sqrt(e0*e1+1e-12f);
                if(r>best){best=r;bestLag=lag;}
            }
            return best>.55f&&bestLag>0?rate/bestLag:0;
        }
    }
}
