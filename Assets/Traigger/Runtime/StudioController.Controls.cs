using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Traigger {
    // Controles que Unity describe para la interfaz nativa (SwiftUI los dibuja solos): grupo, nombre, tipo, rango y
    // valor. Así todo lo del antiguo panel OnGUI está también fuera del editor, y lo que se añada aquí aparece sin
    // tocar Swift. La clave es la ruta del campo en StudioController («mic.sensitivity», «handTargetLeft.x»…) o una
    // acción («do:…»).
    public partial class StudioController {
        [Serializable]public class BridgeControl{public string group,key,label,kind;public float min,max,value;}
        struct Spec{public string group,key,label,kind;public float min,max;public Spec(string g,string k,string l,string kind,float a=0,float b=1){group=g;key=k;label=l;this.kind=kind;min=a;max=b;}}
        static Spec S(string g,string k,string l,float a,float b)=>new Spec(g,k,l,"slider",a,b);
        static Spec T(string g,string k,string l)=>new Spec(g,k,l,"toggle");
        static Spec B(string g,string k,string l)=>new Spec(g,k,l,"button");
        static readonly Spec[] Specs={
            B("Encuadre","do:frame.body","Cuerpo entero"),B("Encuadre","do:frame.face","Cara · videollamada"),
            T("Encuadre","do:gesturesLocked","Bloquear arrastre y pellizco"),
            B("Fondo","do:bg.dark","Oscuro"),B("Fondo","do:bg.green","Verde (croma)"),B("Fondo","do:bg.transparent","Transparente"),
            S("Fondo","volume","Sombras suaves",0,.35f),
            B("Pose","do:pose.wave","Probar saludo"),B("Pose","do:pose.step","Probar paso"),
            S("Cabeza","head","Inclinar cabeza",-20,20),S("Cabeza","turn","Mirar a los lados",-18,18),S("Cabeza","nod","Mirar arriba / abajo",-12,12),
            S("Cabeza","neck","Cuello",-15,15),S("Cabeza","mouth","Boca",0,1),S("Cabeza","smile","Expresión alegre",0,1),
            S("Cabeza","blinkLeft","Ojo izquierdo",0,1),S("Cabeza","blinkRight","Ojo derecho",0,1),
            S("Orejas y cola","earMood","Orejas: bajas / atentas",-1,1),S("Orejas y cola","earLeft","Oreja izquierda",-25,45),S("Orejas y cola","earRight","Oreja derecha",-45,25),
            S("Orejas y cola","tail","Cola",-35,35),
            S("Torso","torso","Torso",-15,15),S("Torso","hips","Cadera",-15,15),S("Torso","lateral","Moverse de lado a lado",-1,1),
            S("Brazos","shoulderLeft","Hombro izquierdo",-25,25),S("Brazos","shoulderRight","Hombro derecho",-25,25),
            S("Brazos","upperArmLeft","Brazo izquierdo",-110,110),S("Brazos","upperArmRight","Brazo derecho",-110,110),
            S("Brazos","armLeft","Codo izquierdo",-135,135),S("Brazos","armRight","Codo derecho",-135,135),
            S("Brazos","wristLeft","Muñeca izquierda",-85,85),S("Brazos","wristRight","Muñeca derecha",-85,85),
            T("Brazos","guideHandLeft","Guiar brazo izquierdo por la mano"),S("Brazos","handTargetLeft.x","Mano izquierda · horizontal",-1.5f,-.15f),S("Brazos","handTargetLeft.y","Mano izquierda · altura",1.75f,3.6f),
            T("Brazos","guideHandRight","Guiar brazo derecho por la mano"),S("Brazos","handTargetRight.x","Mano derecha · horizontal",.15f,1.5f),S("Brazos","handTargetRight.y","Mano derecha · altura",1.75f,3.6f),
            S("Piernas","thighLeft","Muslo izquierdo",-65,65),S("Piernas","thighRight","Muslo derecho",-65,65),
            S("Piernas","legLeft","Rodilla izquierda",-100,100),S("Piernas","legRight","Rodilla derecha",-100,100),
            S("Piernas","ankleLeft","Tobillo izquierdo",-60,60),S("Piernas","ankleRight","Tobillo derecho",-60,60),
            S("Piernas","toesLeft","Punta pie izquierdo",-35,35),S("Piernas","toesRight","Punta pie derecho",-35,35),
            S("Física","response","Suavidad de transición",.08f,.5f),S("Física","lateralAmount","Amplitud del balanceo",0,2),
            S("Física","physics","Inercia de pelo, orejas y cola",0,1.3f),S("Física","hairFreedom","Pelo suelto / recorrido",.2f,2.5f),
            S("Física","earFlexibility","Flexibilidad de las orejas",.2f,2),S("Física","earFeedback","Rebote de las orejas",0,2),
            S("Física","earArc","Arco de las orejas",0,1.3f),S("Física","tailWag","Cola: meneo",0,1.5f),S("Física","tailSpeed","Cola: velocidad del meneo",.3f,2.5f),
            // Calibración de los mechones del flequillo (se guarda en el aparato).
            S("Pelo","hairDetails.fringeFreedom","Flequillo: recorrido",0,2),S("Pelo","hairDetails.crownFreedom","Pelillos de arriba: recorrido",0,2),
            S("Pelo","hairDetails.tipFreedom","Curva de las puntas",0,2),S("Pelo","hairDetails.stiffness","Rigidez (vuelve antes)",.3f,2.5f),
            S("Pelo","hairDetails.damping","Amortiguación (rebota menos)",.3f,2.5f),S("Pelo","hairDetails.variation","Variedad entre mechones",0,1),
            S("Pelo","hairDetails.turnResponse","Reacción al girar la cabeza",0,2),S("Pelo","hairDetails.tiltResponse","Reacción al inclinarla",0,2),
            S("Pelo","hairDetails.nodResponse","Reacción al asentir",0,2),S("Pelo","hairDetails.swayResponse","Reacción al moverte de lado",0,2),
            B("Pelo","do:hair.reset","Restablecer el pelo"),
            S("Micrófono","mic.sensitivity","Sensibilidad del micrófono",.5f,4),S("Micrófono","vision.voiceSensitivity","Sensibilidad de la voz (Vision Pro)",.5f,4),
            S("Micrófono","mouthSmoothing","Suavidad de la boca",.01f,.2f),
            T("Cámara de cara","deviceFace.mirror","Modo espejo"),T("Cámara de cara","deviceFace.invertYaw","Invertir giro"),
            T("Cámara de cara","deviceFace.invertRoll","Invertir inclinación"),T("Cámara de cara","deviceFace.invertPitch","Invertir arriba/abajo"),T("Cámara de cara","deviceFace.swapEyes","Intercambiar ojos"),
            B("Cara","do:face.preset.0","Neutral"),B("Cara","do:face.preset.1","Sorpresa"),B("Cara","do:face.preset.2","Enfado"),B("Cara","do:face.preset.3","Pícaro"),
            B("Cara","do:face.preset.4","Carrillos"),B("Cara","do:face.preset.5","Tristeza"),B("Cara","do:face.preset.6","Alegría"),B("Cara","do:face.preset.7","Lengua + guiño"),
            B("Cara","do:face.follow","Seguir mi cara"),T("Cara","facialExpressions.manual","Controles manuales de cara"),
            S("Cara","facialExpressions.intensity","Intensidad de las muecas",0,1.5f),S("Cara","facialExpressions.tongueSize","Tamaño de lengua",.75f,2),
            S("Cara manual","facialExpressions.manualFrame.jawOpen","Abrir boca",0,1),S("Cara manual","facialExpressions.manualFrame.mouthPucker","Fruncir boca",0,1),
            S("Cara manual","facialExpressions.manualFrame.mouthSmileLeft","Sonrisa izquierda",0,1),S("Cara manual","facialExpressions.manualFrame.mouthSmileRight","Sonrisa derecha",0,1),
            S("Cara manual","facialExpressions.manualFrame.eyeBlinkLeft","Guiño izquierdo",0,1),S("Cara manual","facialExpressions.manualFrame.eyeBlinkRight","Guiño derecho",0,1),
            S("Cara manual","facialExpressions.manualFrame.tongueOut","Sacar lengua",0,1),S("Cara manual","facialExpressions.manualFrame.browInnerUp","Centro de las cejas",0,1),
            S("Cara manual","facialExpressions.manualFrame.browDownLeft","Ceño izquierdo",0,1),S("Cara manual","facialExpressions.manualFrame.browDownRight","Ceño derecho",0,1),
            S("Cara manual","facialExpressions.manualFrame.browOuterUpLeft","Ceja izquierda arriba",0,1),S("Cara manual","facialExpressions.manualFrame.browOuterUpRight","Ceja derecha arriba",0,1),
            S("Cara manual","facialExpressions.manualFrame.eyeWideLeft","Ojo izquierdo abierto",0,1),S("Cara manual","facialExpressions.manualFrame.eyeWideRight","Ojo derecho abierto",0,1),
            S("Cara manual","facialExpressions.manualFrame.eyeSquintLeft","Ojo izquierdo entrecerrado",0,1),S("Cara manual","facialExpressions.manualFrame.eyeSquintRight","Ojo derecho entrecerrado",0,1),
            S("Cara manual","facialExpressions.manualFrame.cheekPuff","Inflar carrillos",0,1),
            S("Cara manual","facialExpressions.manualFrame.mouthFrownLeft","Comisura izquierda abajo",0,1),S("Cara manual","facialExpressions.manualFrame.mouthFrownRight","Comisura derecha abajo",0,1),
        };

        // Sigue la ruta («a.b.c») desde StudioController; los Vector2 se leen y escriben por x / y.
        bool Resolve(string path,out object owner,out MemberInfo member,out string vectorPart){
            owner=this;member=null;vectorPart=null;
            var parts=path.Split('.');
            for(int i=0;i<parts.Length;i++){
                if(owner==null)return false;
                var type=owner.GetType();const BindingFlags all=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
                MemberInfo found=(MemberInfo)type.GetField(parts[i],all)??type.GetProperty(parts[i],all);
                if(found==null)return false;
                object value=found is FieldInfo f?f.GetValue(owner):((PropertyInfo)found).GetValue(owner);
                if(value is Vector2&&i==parts.Length-2){member=found;vectorPart=parts[i+1];return true;}
                if(i==parts.Length-1){member=found;return true;}
                owner=value;
            }
            return false;
        }
        bool TryGet(string path,out float value){
            value=0;
            if(!Resolve(path,out var owner,out var member,out var part))return false;
            object raw=member is FieldInfo f?f.GetValue(owner):((PropertyInfo)member).GetValue(owner);
            if(part!=null){var v=(Vector2)raw;value=part=="x"?v.x:v.y;return true;}
            if(raw is float x){value=x;return true;}if(raw is bool b){value=b?1:0;return true;}
            return false;
        }
        // Los ajustes del pelo y la física se recuerdan entre sesiones.
        static bool SavedControl(string key)=>key.StartsWith("hairDetails.")||key=="physics"||key=="hairFreedom";
        void LoadSavedControls(){foreach(var spec in Specs)if(SavedControl(spec.key)&&PlayerPrefs.HasKey("traigger.ctl."+spec.key))TrySet(spec.key,PlayerPrefs.GetFloat("traigger.ctl."+spec.key));}
        void TrySet(string path,float value){
            if(!Resolve(path,out var owner,out var member,out var part))return;
            void Write(object v){if(member is FieldInfo f)f.SetValue(owner,v);else((PropertyInfo)member).SetValue(owner,v);}
            object raw=member is FieldInfo fi?fi.GetValue(owner):((PropertyInfo)member).GetValue(owner);
            if(part!=null){var v=(Vector2)raw;if(part=="x")v.x=value;else v.y=value;Write(v);}
            else if(raw is float)Write(value);
            else if(raw is bool)Write(value>.5f);
        }

        BridgeControl[] GetControls(){
            var list=new List<BridgeControl>();
            foreach(var spec in Specs){
                float value=0;
                if(spec.key=="do:gesturesLocked"){if(!manipulator)continue;value=manipulator.gesturesLocked?1:0;}
                else if(spec.key.StartsWith("do:")){
                    if(spec.key.StartsWith("do:frame")&&!framing)continue;
                    if(spec.key.StartsWith("do:face")&&!facialExpressions)continue;
                }
                else if(!TryGet(spec.key,out value))continue; // componente que no existe en esta plataforma
                list.Add(new BridgeControl{group=spec.group,key=spec.key,label=spec.label,kind=spec.kind,min=spec.min,max=spec.max,value=value});
            }
            return list.ToArray();
        }

        void ApplyControl(string key,float value){
            switch(key){
            case "do:frame.body":if(framing)framing.Center(false);return;
            case "do:frame.face":if(framing)framing.Center(true);return;
            case "do:gesturesLocked":if(manipulator)manipulator.SetGesturesLocked(value>.5f);return;
            case "do:bg.dark":if(avatarCamera)avatarCamera.backgroundColor=new Color(.07f,.08f,.12f,1);return;
            case "do:bg.green":if(avatarCamera)avatarCamera.backgroundColor=Color.green;return;
            case "do:bg.transparent":if(avatarCamera)avatarCamera.backgroundColor=new Color(.07f,.08f,.12f,0);return;
            case "do:pose.wave":ResetBodyPose();demo=false;upperArmLeft=-60;armLeft=-75;wristLeft=15;return;
            case "do:pose.step":ResetBodyPose();demo=false;thighLeft=-18;legLeft=25;ankleLeft=-12;return;
            case "do:face.follow":if(facialExpressions){facialExpressions.manual=false;tracking=true;}return;
            }
            if(key.StartsWith("do:face.preset.")&&facialExpressions&&int.TryParse(key.Substring(15),out int preset)){facialExpressions.Preset(preset);return;}
            if(key=="do:hair.reset"){
                foreach(var spec in Specs)if(spec.key.StartsWith("hairDetails."))PlayerPrefs.DeleteKey("traigger.ctl."+spec.key);
                if(hairDetails){hairDetails.fringeFreedom=hairDetails.crownFreedom=hairDetails.tipFreedom=hairDetails.stiffness=hairDetails.damping=1;
                    hairDetails.turnResponse=hairDetails.tiltResponse=hairDetails.nodResponse=hairDetails.swayResponse=1;hairDetails.variation=.5f;}
                return;
            }
            if(!key.StartsWith("do:")){TrySet(key,value);if(SavedControl(key))PlayerPrefs.SetFloat("traigger.ctl."+key,value);}
        }
    }
}
