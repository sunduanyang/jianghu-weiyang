using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
public sealed class PaperPose {
    public readonly Dictionary<string,Vector2> Joints=new Dictionary<string,Vector2>();
    public readonly Dictionary<string,float> Angles=new Dictionary<string,float>();
}
// Every split panel uses calibrated sockets on this same hierarchy. Only walk
// is animated; bind is the static assembly. Coordinates are x right, y down.
public static class PaperRig {
    public static Vector2 Point(J a)=>new Vector2((float)a[0].N,(float)a[1].N);
    public static Vector2 Rotate(Vector2 p,float degrees){float a=degrees*Mathf.Deg2Rad;return new Vector2(p.x*Mathf.Cos(a)-p.y*Mathf.Sin(a),p.x*Mathf.Sin(a)+p.y*Mathf.Cos(a));}
    public static float DownAngle(Vector2 v)=>Mathf.Atan2(-v.x,v.y)*Mathf.Rad2Deg;
    public static float Length(J rig,J skin,string key)=>(float)skin["lengths"][key].N;
    public static float Segment(J rig,J skin,string key,string side)=>(float)skin["lengths"][key+side].N;
    public static Vector2 Hip(J skin,string side)=>Point(skin["hips"][side]);
    public static float Sole(J skin,string side)=>(float)skin["sole"][side].N;
    public static float Ground(J rig,J skin)=>(float)skin["ground"].N;
    static float V(Dictionary<string,float> v,string key,float fallback=0)=>v.TryGetValue(key,out float x)?x:fallback;
    public static float Phase(J rig,string clip,float elapsed,bool repeat=false)=>clip=="walk"?Mathf.Repeat(elapsed/(float)rig["clips"]["walk"]["seconds"].N,1):0;
    public static Vector2 SolveKnee(Vector2 hip,Vector2 target,float upper,float lower,out Vector2 ankle,float pole=1){
        Vector2 delta=target-hip;float raw=delta.magnitude,d=Mathf.Clamp(raw,Mathf.Abs(upper-lower)+.0001f,upper+lower-.0001f);
        Vector2 axis=raw>.00001f?delta/raw:Vector2.up;ankle=hip+axis*d;
        float along=(upper*upper-lower*lower+d*d)/(2*d),bend=Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));
        return hip+axis*along+new Vector2(axis.y,-axis.x)*bend*pole;
    }
    public static Dictionary<string,float> Parameters(J rig,string clip,float phase){
        var v=new Dictionary<string,float>{{"walk",clip=="walk"?1:0},{"phase",Mathf.Repeat(phase,1)}};
        float stance=(float)rig["clips"]["walk"]["stance"].N;
        foreach(string side in new[]{"near","far"}){
            float q=Mathf.Repeat(phase+(side=="far"?.5f:0),1),x,lift;
            if(q<stance){x=1-2*q/stance;lift=0;}
            else{float t=(q-stance)/(1-stance);x=-Mathf.Cos(t*Mathf.PI);lift=Mathf.Sin(t*Mathf.PI);lift*=lift;}
            v[side+"_foot_x"]=x;v[side+"_foot_lift"]=lift;
        }
        return v;
    }
    public static Vector2 FootGoal(J rig,J skin,Dictionary<string,float> v,string side){
        string prefix=side=="Near"?"near":"far";Vector2 rest=Point(skin["feet"][side]);float weight=V(v,"walk");
        return rest+new Vector2(V(v,prefix+"_foot_x")*(float)skin["stride"].N,-V(v,prefix+"_foot_lift")*(float)skin["lift"].N)*weight;
    }
    public static PaperPose Sample(J rig,J skin,string clip,float phase)=>Evaluate(rig,skin,Parameters(rig,clip,phase));
    public static PaperPose Evaluate(J rig,J skin,Dictionary<string,float> v){
        var pose=new PaperPose();var p=pose.Joints;float weight=V(v,"walk"),phase=V(v,"phase"),cycle=phase*Mathf.PI*2;
        float lean=(float)skin["bind_lean"].N+weight*1.5f;
        p["hip"]=new Vector2(0,weight*(float)skin["bob"].N*(1-Mathf.Cos(cycle*2))*.5f);
        pose.Angles["body"]=lean;pose.Angles["head"]=lean-(float)skin["bind_lean"].N;
        foreach(string side in new[]{"Near","Far"}){
            Vector2 offset=Rotate(Hip(skin,side),lean),goal=FootGoal(rig,skin,v,side);
            float reach=Segment(rig,skin,"thigh",side)+Segment(rig,skin,"shin",side)-.001f,dx=goal.x-offset.x;
            p["hip"]=new Vector2(0,Mathf.Max(p["hip"].y,goal.y-offset.y-Mathf.Sqrt(Mathf.Max(.001f,reach*reach-dx*dx))));
        }
        p["neck"]=p["hip"]+Rotate(new Vector2(0,-Length(rig,skin,"spine")),lean);
        foreach(string side in new[]{"Far","Near"}){
            float opposite=side=="Near"?1:-1;
            p["shoulder"+side]=p["hip"]+Rotate(Point(skin["shoulders"][side]),lean);
            float upper=(float)skin["arm_rest"][side][0].N+lean+weight*opposite*18*Mathf.Cos(cycle);
            float fore=upper+(float)skin["arm_rest"][side][1].N-weight*(5+5*Mathf.Sin(cycle)*opposite);
            p["elbow"+side]=p["shoulder"+side]+Rotate(new Vector2(0,Segment(rig,skin,"upper_arm",side)),upper);
            p["wrist"+side]=p["elbow"+side]+Rotate(new Vector2(0,Segment(rig,skin,"forearm",side)),fore);
            pose.Angles["forearm"+side]=fore;pose.Angles["hand"+side]=fore;
            p["hip"+side]=p["hip"]+Rotate(Hip(skin,side),lean);
            p["knee"+side]=SolveKnee(p["hip"+side],FootGoal(rig,skin,v,side),Segment(rig,skin,"thigh",side),Segment(rig,skin,"shin",side),out Vector2 ankle);
            p["ankle"+side]=ankle;pose.Angles["foot"+side]=0;
        }
        pose.Angles["pelvis"]=lean-(float)skin["bind_lean"].N+weight*2*Mathf.Sin(cycle);
        foreach(var pair in rig["attachments"].D){var a=pair.Value;p[pair.Key]=p[a["parent"].S]+Rotate(Point(a["offset"]),pose.Angles[a["follow"].S]);}
        return pose;
    }
}
// Blend before FK/IK so a transition cannot stretch a limb or detach a hand.
public sealed class PaperAnimator {
    float weight,previousWeight,changed,walkStart,walkPhase;string target="bind";bool initialized;
    public PaperPose Sample(J rig,J skin,string requested,float phase,float now){
        string next=requested=="walk"?"walk":"bind";float seconds=(float)rig["clips"]["walk"]["seconds"].N;
        if(!initialized){initialized=true;target=next;weight=next=="walk"?1:0;previousWeight=weight;changed=now;walkStart=now;walkPhase=phase;}
        float current=target=="walk"?Mathf.Repeat(walkPhase+(now-walkStart)/seconds,1):walkPhase;
        if(next!=target){previousWeight=weight;walkPhase=current;walkStart=now;target=next;changed=now;}
        float t=Mathf.SmoothStep(0,1,Mathf.Clamp01((now-changed)/.2f));weight=Mathf.Lerp(previousWeight,target=="walk"?1:0,t);
        var values=PaperRig.Parameters(rig,"walk",current);values["walk"]=weight;
        return PaperRig.Evaluate(rig,skin,values);
    }
}
}
