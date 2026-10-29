using System;
using System.IO;
using Jianghu;
using UnityEditor;
using UnityEditor.SceneManagement;

// Runs the saved delivery scene in the matching editor without rebuilding
// the player for each diagnosis. A final player run verifies the shipped fix.
[InitializeOnLoad]
public static class OriginalTimeEditorWatch {
    static readonly string directory;
    static OriginalTimeEditorWatch() {
        var args=Environment.GetCommandLineArgs();int marker=Array.IndexOf(args,"-timeEditorProbe"),at=Array.IndexOf(args,"-validateOriginalTime");
        if(marker<0||at<0||at+1>=args.Length)return;
        directory=Path.GetFullPath(args[at+1]);EditorApplication.update+=Poll;
    }
    static void Poll() {
        if(string.IsNullOrEmpty(directory))return;
        string report=Path.Combine(directory,"time-validation.json");
        if(File.Exists(report)) {
            try{EditorApplication.Exit(J.Parse(File.ReadAllText(report))["passed"].B?0:2);}
            catch(Exception){EditorApplication.Exit(3);}
            return;
        }
        string began=SessionState.GetString("OriginalTimeProbe.Started","");
        if(long.TryParse(began,out long ticks)&&DateTime.UtcNow.Ticks-ticks>TimeSpan.FromMinutes(3).Ticks)EditorApplication.Exit(4);
    }
}

public static partial class OriginalRecoveryBuild {
    public static void ValidateTime() {
        SessionState.SetString("OriginalTimeProbe.Started",DateTime.UtcNow.Ticks.ToString());
        EditorSceneManager.OpenScene("Assets/Recovery/Scenes/OriginalHierarchy.unity",OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
}
