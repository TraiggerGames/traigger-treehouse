using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Traigger.Editor {
    // Firma para todas las compilaciones de Apple (iPhone/iPad, Vision Pro): equipo de desarrollador y firma automática
    // de Xcode, para no elegir el equipo a mano cada vez. El equipo sale de TRAIGGER_TEAM (entorno) o de «signing.local»
    // en la raíz del proyecto, que no se sube al repositorio (ver signing.example y docs/COMPILAR.md).
    public static class Signing {
        public static string Team{get{
            var team=Environment.GetEnvironmentVariable("TRAIGGER_TEAM");
            if(!string.IsNullOrEmpty(team))return team;
            var local=Path.Combine(Path.GetDirectoryName(Application.dataPath)??".","signing.local");
            if(File.Exists(local))foreach(var line in File.ReadAllLines(local)){
                var l=line.Trim();if(!l.StartsWith("TRAIGGER_TEAM="))continue;
                return l.Substring("TRAIGGER_TEAM=".Length).Trim('"',' ');
            }
            return "";
        }}
        // El equipo no se guarda en ProjectSettings (que sí se sube): solo se pone en el proyecto de Xcode generado.
        public static void Apply(){
            PlayerSettings.iOS.appleEnableAutomaticSigning=true;
            if(Team.Length==0)Debug.LogWarning("Traigger: sin equipo de firma. Copia signing.example como signing.local y pon tu equipo (o define TRAIGGER_TEAM).");
        }
        // Todos los objetivos del proyecto de Xcode generado (app, UnityFramework, GameAssembly…).
        public static void Apply(UnityEditor.iOS.Xcode.PBXProject project,string guid){
            if(Team.Length>0)project.SetBuildProperty(guid,"DEVELOPMENT_TEAM",Team);
            project.SetBuildProperty(guid,"CODE_SIGN_STYLE","Automatic");
        }
    }
}
