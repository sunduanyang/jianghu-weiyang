using System.IO;
using Jianghu;
using UnityEngine;

public static class DuelTuning {
    static J settings;
    public static float Get(string key){
        if(settings==null)settings=J.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Backend/jianghu/combat_tuning.json")));
        return (float)settings[key].N;
    }
}
