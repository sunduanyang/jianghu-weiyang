using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
// One parent chain with FK arms, IK legs and data-defined clips. Skin bind
// proportions vary; topology and animation channels do not.
public sealed class PaperPose {
    public readonly Dictionary<string,Vector2> Joints=new Dictionary<string,Vector2>();
    public readonly Dictionary<string,float> Angles=new Dictionary<string,float>();
}
public static class PaperRig {
    public static Vector2 Rotate(Vector2 p,float degrees){float a=degrees*Mathf.Deg2Rad;return new Vector2(p.x*Mathf.Cos(a)-p.y*Mathf.Sin(a),p.x*Mathf.Sin(a)+p.y*Mathf.Cos(a));}
    public static Vector2 Point(J array)=>new Vector2((float)array[0].N,(float)array[1].N);
    static float Value(Dictionary<string,float> v,string key,float fallback=0)=>v.TryGetValue(key,out var value)?value:fallback;
    public static float Length(J rig,J skin,string key)=>(float)(skin["lengths"][key].Has?skin["lengths"][key].N:rig["lengths"][key].N);
    public static float Segment(J rig,J skin,string key,string side)=>skin["lengths"][key+side].Has?(float)skin["lengths"][key+side].N:Length(rig,skin,key);
    public static Vector2 Hip(J skin,string side)=>skin["hips"][side].Has?Point(skin["hips"][side]):new Vector2(side=="Near"?9:-9,0);
    public static float Sole(J skin,string side){var part=skin["parts"][(side=="Near"?"near":"far")+"-foot"];return (float)(part["height"].N*(part["size"][1].N-part["pivot"][1].N)/part["size"][1].N);}
    public static float Ground(J rig,J skin)=>skin["ground"].Has?(float)skin["ground"].N:Length(rig,skin,"thigh")+Length(rig,skin,"shin")+Mathf.Max(Hip(skin,"Near").y+Sole(skin,"Near"),Hip(skin,"Far").y+Sole(skin,"Far"))-18;
    public static Vector2 FootGoal(J rig,J skin,Dictionary<string,float> values,string side){
        string prefix=side=="Near"?"near":"far";float legScale=(Segment(rig,skin,"thigh",side)+Segment(rig,skin,"shin",side))/222f;
        Vector2 goal=new Vector2((side=="Near"?9:-9)+Value(values,prefix+"_foot_x")*legScale,Ground(rig,skin)-Sole(skin,side)-Value(values,prefix+"_foot_lift")*legScale);
        if(skin["feet"][side].Has){Vector2 rest=Point(skin["feet"][side])+new Vector2((Value(values,prefix+"_foot_x")-(float)rig["rest"][prefix+"_foot_x"].N)*legScale,-Value(values,prefix+"_foot_lift")*legScale);goal=Vector2.Lerp(rest,goal,Value(values,"gait"));}
        return goal;
    }
    public static float DownAngle(Vector2 v)=>Mathf.Atan2(-v.x,v.y)*Mathf.Rad2Deg;
    // The bend pole is always in front of the side profile. The two legs share
    // this rule, avoiding the old far knee bending backwards.
    public static Vector2 SolveKnee(Vector2 hip,Vector2 target,float upper,float lower,out Vector2 ankle,float pole=1){
        Vector2 delta=target-hip;float raw=delta.magnitude,d=Mathf.Clamp(raw,Mathf.Abs(upper-lower)+.001f,upper+lower-.001f);
        Vector2 direction=raw>.0001f?delta/raw:Vector2.down;ankle=hip+direction*d;
        float along=(upper*upper-lower*lower+d*d)/(2*d),height=Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
        return hip+direction*along+new Vector2(direction.y,-direction.x)*height*pole;
    }
    public static Dictionary<string,float> Parameters(J rig,string clip,float phase){
        if(!rig["clips"][clip].Has)clip="idle";
        phase=Mathf.Clamp01(phase);
        var keys=rig["clips"][clip]["keys"].A;J a=keys[0],b=keys[keys.Count-1];
        for(int i=1;i<keys.Count;i++)if(phase<=keys[i]["t"].N){a=keys[i-1];b=keys[i];break;}
        float t=Mathf.InverseLerp((float)a["t"].N,(float)b["t"].N,phase);t=t*t*(3-2*t);
        var values=new Dictionary<string,float>();
        foreach(var pair in rig["rest"].D){float av=(float)(a[pair.Key].Has?a[pair.Key].N:pair.Value.N),bv=(float)(b[pair.Key].Has?b[pair.Key].N:pair.Value.N);values[pair.Key]=Mathf.Lerp(av,bv,t);}
        if(clip=="walk"){values["gait"]=1;}
        if(clip=="walk")foreach(string side in new[]{"near","far"}){
            float q=Mathf.Repeat(phase+(side=="far"?.5f:0),1),stance=.6f;
            if(q<=stance){values[side+"_foot_x"]=Mathf.Lerp(32,-32,q/stance);values[side+"_foot_lift"]=0;values[side+"_foot_angle"]=0;}
            else{float swing=(q-stance)/(1-stance),smooth=swing*swing*(3-2*swing);values[side+"_foot_x"]=Mathf.Lerp(-32,32,smooth);values[side+"_foot_lift"]=30*Mathf.Pow(Mathf.Sin(swing*Mathf.PI),2);values[side+"_foot_angle"]=16*Mathf.Sin(swing*Mathf.PI*2);}
        }
        return values;
    }
    public static PaperPose Sample(J rig,J skin,string clip,float phase)=>Evaluate(rig,skin,Parameters(rig,clip,phase));
    public static PaperPose Evaluate(J rig,J skin,Dictionary<string,float> values){
        float lean=values["lean"]+(float)skin["bind_lean"].N;var pose=new PaperPose();var p=pose.Joints;
        p["hip"]=new Vector2(values["root_x"],values["root_y"]);pose.Angles["body"]=lean;
        // Lower the pelvis only when a planted ankle would be outside the two
        // segments' reach. This fixes support-foot hovering without stretching.
        foreach(string side in new[]{"Near","Far"}){
            Vector2 offset=Rotate(Hip(skin,side),lean),goal=FootGoal(rig,skin,values,side);float reach=Segment(rig,skin,"thigh",side)+Segment(rig,skin,"shin",side)-.002f;
            float dx=goal.x-p["hip"].x-offset.x;if(Mathf.Abs(dx)<reach)p["hip"]=new Vector2(p["hip"].x,Mathf.Max(p["hip"].y,goal.y-offset.y-Mathf.Sqrt(reach*reach-dx*dx)));
        }
        p["neck"]=p["hip"]+Rotate(new Vector2(0,-Length(rig,skin,"spine")),lean);
        pose.Angles["head"]=lean+Value(values,"head_angle")+(float)skin["head_angle_offset"].N;
        foreach(string side in new[]{"Far","Near"}){
            string prefix=side=="Near"?"near":"far";
            p["shoulder"+side]=p["hip"]+Rotate(Point(skin["shoulders"][side]),lean);
            float upper=Mathf.Clamp(values[prefix+"_upper"]+(float)skin["angle_offsets"][prefix+"_upper"].N,-155,85)+lean,forearm=upper+Mathf.Clamp(values[prefix+"_elbow"]+(float)skin["angle_offsets"][prefix+"_elbow"].N,-145,35);
            p["elbow"+side]=p["shoulder"+side]+Rotate(new Vector2(0,Segment(rig,skin,"upper_arm",side)),upper);
            p["wrist"+side]=p["elbow"+side]+Rotate(new Vector2(0,Segment(rig,skin,"forearm",side)),forearm);
            pose.Angles["forearm"+side]=forearm;
            pose.Angles["hand"+side]=forearm+Value(values,prefix+"_wrist")+(float)skin["hand_angle_offsets"][side].N;
            p["hip"+side]=p["hip"]+Rotate(Hip(skin,side),lean);
            float thigh=values[prefix+"_thigh"],shin=thigh+values[prefix+"_knee"];
            Vector2 fk=p["hip"+side]+Rotate(new Vector2(0,Segment(rig,skin,"thigh",side)),thigh)+Rotate(new Vector2(0,Segment(rig,skin,"shin",side)),shin);
            Vector2 goal=FootGoal(rig,skin,values,side);
            goal=Vector2.Lerp(fk,goal,Value(values,"foot_ik",1));
            float pole=skin["knee_poles"][side].Has?(float)skin["knee_poles"][side].N:1;
            p["knee"+side]=SolveKnee(p["hip"+side],goal,Segment(rig,skin,"thigh",side),Segment(rig,skin,"shin",side),out var ankle,pole);p["ankle"+side]=ankle;
            pose.Angles["foot"+side]=Value(values,prefix+"_foot_angle");
            pose.Angles["skirt"+side]=values["lean"]+Mathf.Clamp((DownAngle(p["knee"+side]-p["hip"+side])-(float)skin["leg_bind_angles"][side].N)*.2f,-12,12)+Value(values,prefix+"_skirt");
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
