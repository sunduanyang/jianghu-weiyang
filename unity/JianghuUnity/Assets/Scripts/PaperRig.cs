using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
// One forward-kinematics skeleton and data-defined clips, independent of texture,
// profession and facing. Skin rest shoulders vary; topology and clips do not.
public sealed class PaperPose {
    public readonly Dictionary<string,Vector2> Joints=new Dictionary<string,Vector2>();
    public readonly Dictionary<string,float> Angles=new Dictionary<string,float>();
}
public static class PaperRig {
    public static Vector2 Rotate(Vector2 p,float degrees){float a=degrees*Mathf.Deg2Rad;return new Vector2(p.x*Mathf.Cos(a)-p.y*Mathf.Sin(a),p.x*Mathf.Sin(a)+p.y*Mathf.Cos(a));}
    public static Vector2 Point(J array)=>new Vector2((float)array[0].N,(float)array[1].N);
    public static Dictionary<string,float> Parameters(J rig,string clip,float phase){
        if(!rig["clips"][clip].Has)clip="idle";
        phase=Mathf.Clamp01(phase);
        var keys=rig["clips"][clip]["keys"].A;J a=keys[0],b=keys[keys.Count-1];
        for(int i=1;i<keys.Count;i++)if(phase<=keys[i]["t"].N){a=keys[i-1];b=keys[i];break;}
        float t=Mathf.InverseLerp((float)a["t"].N,(float)b["t"].N,phase);t=t*t*(3-2*t);
        var values=new Dictionary<string,float>();
        foreach(var pair in rig["rest"].D){float av=(float)(a[pair.Key].Has?a[pair.Key].N:pair.Value.N),bv=(float)(b[pair.Key].Has?b[pair.Key].N:pair.Value.N);values[pair.Key]=Mathf.Lerp(av,bv,t);}
        return values;
    }
    public static PaperPose Sample(J rig,J skin,string clip,float phase)=>Evaluate(rig,skin,Parameters(rig,clip,phase));
    public static PaperPose Evaluate(J rig,J skin,Dictionary<string,float> values){
        float lean=values["lean"];var pose=new PaperPose();var p=pose.Joints;
        p["hip"]=new Vector2(values["root_x"],values["root_y"]);pose.Angles["body"]=lean;
        p["neck"]=p["hip"]+Rotate(new Vector2(0,-(float)rig["lengths"]["spine"].N),lean);
        foreach(string side in new[]{"Far","Near"}){
            string prefix=side=="Near"?"near":"far";
            p["shoulder"+side]=p["hip"]+Rotate(Point(skin["shoulders"][side]),lean);
            float upper=values[prefix+"_upper"]+lean,forearm=upper+values[prefix+"_elbow"];
            p["elbow"+side]=p["shoulder"+side]+Rotate(new Vector2(0,(float)rig["lengths"]["upper_arm"].N),upper);
            p["wrist"+side]=p["elbow"+side]+Rotate(new Vector2(0,(float)rig["lengths"]["forearm"].N),forearm);
            pose.Angles["forearm"+side]=forearm;
            p["hip"+side]=p["hip"]+new Vector2(side=="Near"?9:-9,0);
            float thigh=values[prefix+"_thigh"],shin=thigh+values[prefix+"_knee"];
            p["knee"+side]=p["hip"+side]+Rotate(new Vector2(0,(float)rig["lengths"]["thigh"].N),thigh);
            p["ankle"+side]=p["knee"+side]+Rotate(new Vector2(0,(float)rig["lengths"]["shin"].N),shin);
            pose.Angles["skirt"+side]=thigh*.28f;
        }
        foreach(var pair in rig["attachments"].D){var attach=pair.Value;p[pair.Key]=p[attach["parent"].S]+Rotate(Point(attach["offset"]),pose.Angles[attach["follow"].S]);}
        return pose;
    }
    public static float Phase(J rig,string clip,float elapsed,bool repeat=false){var data=rig["clips"][clip];float t=elapsed/Mathf.Max(.1f,(float)data["seconds"].N);return repeat||data["loop"].B?Mathf.Repeat(t,1):Mathf.Clamp01(t);}
}
// Blend joint parameters before FK, preserving bone lengths through transitions.
public sealed class PaperAnimator {
    string clip;
    float blendStart;
    Dictionary<string,float> previous,last;
    public PaperPose Sample(J rig,J skin,string target,float phase,float now){
        var values=PaperRig.Parameters(rig,target,phase);
        if(clip!=target){previous=last;clip=target;blendStart=now;}
        float t=Mathf.Clamp01((now-blendStart)/.18f);t=t*t*(3-2*t);
        if(previous!=null&&t<1)foreach(string key in new List<string>(values.Keys))values[key]=Mathf.Lerp(previous[key],values[key],t);
        last=values;return PaperRig.Evaluate(rig,skin,values);
    }
}
}
