using UnityEngine;
using UnityEditor;
namespace Traigger.Editor {
    public class FacialExpressionWindow:EditorWindow {
        [MenuItem("Traigger/Cara/Muecas y seguimiento facial")]
        static void Open()=>GetWindow<FacialExpressionWindow>(false,"Traigger · Muecas");
        static readonly string[] Labels={"Neutral","Sorpresa","Enfado","Pícaro","Carrillos","Tristeza","Alegría","Lengua + guiño"};
        static readonly string[] Names={"Centro de las cejas","Ceño izquierdo","Ceño derecho","Ceja izquierda arriba","Ceja derecha arriba","Ojo izquierdo abierto","Ojo derecho abierto","Ojo izquierdo entrecerrado","Ojo derecho entrecerrado","Inflar carrillos","Mejilla izquierda arriba","Mejilla derecha arriba","Comisura izquierda abajo","Comisura derecha abajo","Boca a la izquierda","Boca a la derecha","Presionar labio izquierdo","Presionar labio derecho","Arrugar nariz izquierda","Arrugar nariz derecha"};
        Vector2 scroll;
        void OnGUI(){
            var studio=Object.FindAnyObjectByType<StudioController>();
            if(!Application.isPlaying||!studio||!studio.facialExpressions){EditorGUILayout.HelpBox("Pulsa Play en la escena de Traigger. Las pruebas manuales no necesitan iPhone.",MessageType.Info);return;}
            var rig=studio.facialExpressions;
            rig.manual=EditorGUILayout.Toggle("Mueca manual",rig.manual);
            rig.intensity=EditorGUILayout.Slider("Intensidad",rig.intensity,0,1.5f);
            rig.tongueSize=EditorGUILayout.Slider("Tamaño de lengua",rig.tongueSize,.75f,2);
            if(GUILayout.Button("Seguir mi cara · iPhone")){rig.manual=false;studio.tracking=true;}
            for(int i=0;i<Labels.Length;i++)if(GUILayout.Button(Labels[i]))rig.Preset(i);
            EditorGUILayout.Space();
            string[] extraFields={"jawOpen","mouthPucker","mouthSmileLeft","mouthSmileRight","eyeBlinkLeft","eyeBlinkRight","tongueOut"};
            string[] extraLabels={"Abrir boca","Fruncir boca","Sonrisa izquierda","Sonrisa derecha","Guiño izquierdo","Guiño derecho","Sacar lengua"};
            for(int i=0;i<extraFields.Length;i++){var f=typeof(FaceFrame).GetField(extraFields[i]);float before=(float)f.GetValue(rig.manualFrame);float value=EditorGUILayout.Slider(extraLabels[i],before,0,1);if(value!=before){rig.manual=true;f.SetValue(rig.manualFrame,value);}}
            scroll=EditorGUILayout.BeginScrollView(scroll);
            for(int i=0;i<FacialExpressionRig.Channels.Length;i++){string channel=FacialExpressionRig.Channels[i];var field=typeof(FaceFrame).GetField(channel);float old=(float)field.GetValue(rig.manualFrame);float value=EditorGUILayout.Slider(Names[i],old,0,1);if(value!=old){rig.manual=true;field.SetValue(rig.manualFrame,value);}}
            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox("Conserva las texturas kemono. Usa ARKit/TrueDepth del iPhone. Necesita recompilar la app emisora para recibir los nuevos canales. No usa el LiDAR trasero ni los datos internos de Face ID.",MessageType.Info);
        }
    }
}
