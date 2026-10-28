using System;
using System.Globalization;
using UnityEngine;
namespace Traigger {
    // Modo layout: cada capa (Traigger, película, capturadora) tiene un rectángulo en pantalla (0–1, origen arriba a la
    // izquierda) que el lienzo del menú mueve y escala. Se guarda en PlayerPrefs y en las escenas.
    public partial class StudioController {
        static readonly Rect Full=new Rect(0,0,1,1);
        public Rect MovieRect=>LoadRect("movie",Full);
        public Rect CaptureRect=>LoadRect("capture",Full);
        public Rect AvatarRect=>manipulator?manipulator.ViewportRect():Rect.zero;
        static Rect LoadRect(string key,Rect fallback){
            var s=PlayerPrefs.GetString("traigger.layer."+key,"");
            return TryParse(s,out var r)?r:fallback;
        }
        static string Format(Rect r)=>string.Format(CultureInfo.InvariantCulture,"{0:0.####},{1:0.####},{2:0.####},{3:0.####}",r.x,r.y,r.width,r.height);
        static bool TryParse(string s,out Rect r){
            r=default;var p=(s??"").Split(',');
            if(p.Length!=4)return false;
            var v=new float[4];
            for(int i=0;i<4;i++)if(!float.TryParse(p[i],NumberStyles.Float,CultureInfo.InvariantCulture,out v[i]))return false;
            r=new Rect(v[0],v[1],v[2],v[3]);return r.width>0&&r.height>0;
        }
        // De arriba-abajo (interfaz) a abajo-arriba (MoviePlayer.area).
        static Rect FlipY(Rect r)=>new Rect(r.x,1-r.y-r.height,r.width,r.height);

        public void SetLayerRect(string layer,Rect r){
            r.width=Mathf.Clamp(r.width,.05f,1.5f);r.height=Mathf.Clamp(r.height,.05f,1.5f);
            switch(layer){
            case "avatar":MoveAvatarTo(r);break;
            case "movie":PlayerPrefs.SetString("traigger.layer.movie",Format(r));ApplyLayerRects();break;
            case "capture":PlayerPrefs.SetString("traigger.layer.capture",Format(r));ApplyLayerRects();break;
            }
            layout="custom";currentScene="";
        }
        // Lleva a Traigger al rectángulo pedido: escala por el alto y mueve el centro (converge en cada arrastre).
        void MoveAvatarTo(Rect r){
            if(!manipulator||!avatarCamera)return;
            var current=manipulator.ViewportRect();if(current.height<=0)return;
            float height=avatarCamera.orthographicSize*2,width=height*avatarCamera.aspect;
            float newScale=Mathf.Clamp(manipulator.scale*r.height/current.height,manipulator.minScale,manipulator.maxScale);
            var offset=manipulator.offset+new Vector2((r.center.x-current.center.x)*width,-(r.center.y-current.center.y)*height);
            manipulator.offset=offset;manipulator.scale=newScale;manipulator.MoveTo(offset,newScale);
        }
        // Aplica los rectángulos guardados a la película y a la capturadora.
        public void ApplyLayerRects(){
            if(movie)movie.area=FlipY(MovieRect);
            VisionCapture.Area=FlipY(CaptureRect);
        }
        bool ApplyLayoutCommand(BridgeCommand c){
            if(c.cmd=="layer.reset"){ResetLayerRects();SetLayout("center");return true;}
            if(c.cmd!="layer.rect")return false;
            // «capa|x,y,ancho,alto»
            var parts=(c.s??"").Split('|');
            if(parts.Length==2&&TryParse(parts[1],out var r))SetLayerRect(parts[0],r);
            return true;
        }
        public void ResetLayerRects(){PlayerPrefs.DeleteKey("traigger.layer.movie");PlayerPrefs.DeleteKey("traigger.layer.capture");ApplyLayerRects();}
    }
}
