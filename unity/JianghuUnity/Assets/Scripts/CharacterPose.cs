using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
public sealed class CharacterPose {
    public sealed class Bone {
        public Vector2 A,B;
        public Matrix4x4 Frame;
        public Bone(Vector2 a,Vector2 b){A=a;B=b;Frame=Matrix4x4.TRS(a,Quaternion.Euler(0,0,Mathf.Atan2((b-a).y,(b-a).x)*Mathf.Rad2Deg-90),Vector3.one);}
    }
    public readonly Dictionary<string,Bone> Bones=new Dictionary<string,Bone>();
    public readonly HashSet<string> Detached=new HashSet<string>();
    public Vector2 FootNear,FootFar;
    public bool Left;
    // Static character dimensions are captured before physics starts. A pose
    // carries motion, never a new scale for the painted pieces on every frame.
    public CharacterPose BindPose;
    public void Add(string name,Vector2 a,Vector2 b){Bones[name]=new Bone(a,b);}
    public Matrix4x4 RigidFrame(string name){
        var bone=Bones[name];Vector2 anchor=bone.A;
        if(name=="pelvis"||name=="torso"||name=="neck"||name.StartsWith("hand"))anchor=bone.B;
        else if(name.StartsWith("foot"))anchor=name.EndsWith("Near")?FootNear:FootFar;
        var frame=bone.Frame;frame.m03=anchor.x;frame.m13=anchor.y;return frame;
    }
    public CharacterPose Snapshot(bool? left=null){
        bool reflected=left.HasValue&&left.Value!=Left;
        Vector2 Copy(Vector2 value)=>new Vector2(reflected?-value.x:value.x,value.y);
        var result=new CharacterPose{Left=left??Left,FootNear=Copy(FootNear),FootFar=Copy(FootFar)};
        foreach(var pair in Bones)result.Add(pair.Key,Copy(pair.Value.A),Copy(pair.Value.B));
        result.Detached.UnionWith(Detached);return result;
    }
    public static CharacterPose Bind(bool left=false){
        var p=new CharacterPose{Left=left};float f=left?-1:1;
        p.Add("pelvis",new Vector2(0,.84f),new Vector2(0,.99f));p.Add("belly",new Vector2(0,.99f),new Vector2(0,1.14f));p.Add("torso",new Vector2(0,1.14f),new Vector2(0,1.34f));p.Add("neck",new Vector2(0,1.34f),new Vector2(0,1.43f));p.Add("head",new Vector2(0,1.43f),new Vector2(0,1.73f));
        foreach(string side in new[]{"Far","Near"}){
            float x=(side=="Near"?.06f:-.06f)*f;Vector2 a=new Vector2(x,1.3f),b=a+new Vector2(f*.2f,-.2f),c=b+new Vector2(f*.25f,.06f);
            p.Add("upper"+side,a,b);p.Add("forearm"+side,b,c);p.Add("hand"+side,c,c+new Vector2(f*.095f,.025f));
            a=new Vector2(x,.87f);b=new Vector2(x+f*.1f,.49f);c=new Vector2(x-f*.06f,.13f);
            p.Add("thigh"+side,a,b);p.Add("shin"+side,b,c);p.Add("foot"+side,c+new Vector2(-f*.08f,-.055f),c+new Vector2(f*.16f,-.055f));
            if(side=="Near")p.FootNear=c;else p.FootFar=c;
        }return p;
    }
    public static CharacterPose Sample(string clip,float phase,bool left=false,float walkBlend=1){
        var p=Bind(left);float f=left?-1:1,w=clip=="walk"?Mathf.Clamp01(walkBlend):0,t=Mathf.Repeat(phase,1),cycle=t*Mathf.PI*2;
        const float upperLeg=.3929377f,lowerLeg=.3939543f,neutralWaist=1.033f;
        var ankles=new Vector2[2];
        // Presentation starts from an upright, relaxed stance. Keep the bind
        // template above unchanged: it is an art/physical calibration pose.
        float waist=neutralWaist-.002f*(1-Mathf.Cos(cycle*2))*w;
        for(int i=0;i<2;i++){
            bool near=i==0;float x=(near?.06f:-.06f)*f;
            float q=Mathf.Repeat(t+(near?0:.5f),1),step,lift;
            if(q<.65f){float v=q/.65f;v=v*v*(3-2*v);step=Mathf.Lerp(.085f,-.085f,v);lift=0;}
            else {float v=(q-.65f)/.35f,sine=Mathf.Sin(v*Mathf.PI);lift=sine*sine*.035f;v=v*v*(3-2*v);step=Mathf.Lerp(-.085f,.085f,v);}
            ankles[i]=new Vector2(f*(near?.075f:-.075f)+f*step*w,.13f+lift*w);
            // A small pelvis adjustment preserves the actual two bone lengths
            // at the longest stride, rather than stretching or locking knees.
            float horizontal=ankles[i].x-x,reach=upperLeg+lowerLeg-.0015f;
            float ceiling=.12f+ankles[i].y+Mathf.Sqrt(Mathf.Max(0,reach*reach-horizontal*horizontal));
            waist=Mathf.Min(waist,ceiling);
        }
        p.Add("pelvis",new Vector2(0,waist-.15f),new Vector2(0,waist));p.Add("belly",new Vector2(0,waist),new Vector2(0,waist+.15f));p.Add("torso",new Vector2(0,waist+.15f),new Vector2(0,waist+.35f));p.Add("neck",new Vector2(0,waist+.35f),new Vector2(0,waist+.44f));p.Add("head",new Vector2(0,waist+.44f),new Vector2(0,waist+.74f));
        foreach(string side in new[]{"Far","Near"}){
            bool near=side=="Near";float x=(near?.06f:-.06f)*f;
            float arm=(near?3:-3)*Mathf.Deg2Rad+(near?-1:1)*Mathf.Sin(cycle)*.16f*w,fore=arm+9*Mathf.Deg2Rad+.035f*(1-Mathf.Cos(cycle))*w;
            Vector2 a=new Vector2(x,waist+.31f),b=a+new Vector2(f*Mathf.Sin(arm),-Mathf.Cos(arm))*.2828427f,c=b+new Vector2(f*Mathf.Sin(fore),-Mathf.Cos(fore))*.2570992f;
            p.Add("upper"+side,a,b);p.Add("forearm"+side,b,c);p.Add("hand"+side,c,c+new Vector2(f*Mathf.Sin(fore),-Mathf.Cos(fore))*.098234f);
            a=new Vector2(x,waist-.12f);c=ankles[near?0:1];
            Vector2 d=c-a;float length=d.magnitude,along=(upperLeg*upperLeg-lowerLeg*lowerLeg+length*length)/(2*length),bend=Mathf.Sqrt(Mathf.Max(0,upperLeg*upperLeg-along*along));Vector2 axis=d/length;
            b=a+axis*along+new Vector2(-axis.y,axis.x)*bend*f;
            p.Add("thigh"+side,a,b);p.Add("shin"+side,b,c);p.Add("foot"+side,c+new Vector2(-f*.08f,-.055f),c+new Vector2(f*.16f,-.055f));if(near)p.FootNear=c;else p.FootFar=c;
        }return p;
    }
}
}
