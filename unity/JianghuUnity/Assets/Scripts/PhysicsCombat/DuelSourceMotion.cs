using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

// Numeric motion dependencies extracted from the supplied 5.0.3 APK.
// Uses Unity's curve evaluator with the original keys and tangents.
public static class DuelSourceMotion {
    public sealed class Motion {
        public string name;
        public float duration;
        public readonly Dictionary<string,AnimationCurve[]> positions=new Dictionary<string,AnimationCurve[]>();
        public readonly Dictionary<string,AnimationCurve[]> rotations=new Dictionary<string,AnimationCurve[]>();
        public Vector2 Position(string path,float time){
            if(!positions.TryGetValue(path,out var curves))throw new ArgumentException("Missing source motion path: "+name+" / "+path);
            return new Vector2(curves[0].Evaluate(time),curves[1].Evaluate(time));
        }
        public float Rotation(string path,float time){
            if(!rotations.TryGetValue(path,out var curves))return 0;
            var q=new Quaternion(curves[0].Evaluate(time),curves[1].Evaluate(time),curves[2].Evaluate(time),curves[3].Evaluate(time));
            return q.normalized.eulerAngles.z;
        }
    }
    static readonly Dictionary<string,Motion> clips=new Dictionary<string,Motion>();
    static J source;
    public static int ClipCount {get{Load();return clips.Count;}}
    public static int BundleCount {get{Load();return source["bundles"].D.Count;}}
    static void Load(){
        if(source!=null)return;
        var asset=Resources.Load<TextAsset>("CombatSource/motion");
        if(asset==null)throw new InvalidOperationException("Missing recovered combat motion data");
        var data=J.Parse(asset.text);
        if(data["schema"].I!=1)throw new InvalidOperationException("Unsupported recovered motion schema");
        foreach(var pair in data["clips"].D){
            var m=new Motion{name=pair.Value["name"].S,duration=(float)pair.Value["duration"].N};
            foreach(var channel in pair.Value["channels"].D)m.positions[channel.Key]=Curves(channel.Value,2);
            foreach(var channel in pair.Value["rotations"].D)m.rotations[channel.Key]=Curves(channel.Value,4);
            clips.Add(pair.Key,m);
        }
        source=data;
    }
    static AnimationCurve[] Curves(J keys,int dimensions){
        var curves=new AnimationCurve[dimensions];
        for(int axis=0;axis<dimensions;axis++){
            var frames=new Keyframe[keys.A.Count];
            for(int i=0;i<frames.Length;i++){
                var k=keys[i];frames[i]=new Keyframe((float)k[0].N,(float)k[1+axis].N,(float)k[1+dimensions+axis].N,(float)k[1+dimensions*2+axis].N);
            }
            curves[axis]=new AnimationCurve(frames){preWrapMode=WrapMode.ClampForever,postWrapMode=WrapMode.ClampForever};
        }
        return curves;
    }
    public static Motion Kick {get{Load();return clips[source["kick"].S];}}
    public static Motion Named(string name){Load();foreach(var m in clips.Values)if(m.name==name)return m;throw new ArgumentException(name);}
    public static Motion Choose(string itemType,bool twoHands,bool up,System.Random random){
        Load();var ids=source["bundles"][itemType+(twoHands?"_2H":"_1H")][up?"up":"down"].A;
        if(ids.Count==0)throw new ArgumentException("Missing source AI bundle: "+itemType);
        return clips[ids[random.Next(ids.Count)].S];
    }
}
