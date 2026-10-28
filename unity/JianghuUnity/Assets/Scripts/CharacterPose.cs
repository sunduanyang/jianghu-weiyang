using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
public sealed class CharacterPose {
    public sealed class Bone {
        public Vector2 A,B;
        public Matrix4x4 Frame;
        public Bone(Vector2 a,Vector2 b){A=a;B=b;float length=Mathf.Max(.001f,(b-a).magnitude);Frame=Matrix4x4.TRS((a+b)*.5f,Quaternion.Euler(0,0,Mathf.Atan2((b-a).y,(b-a).x)*Mathf.Rad2Deg-90),new Vector3(length,length,1));}
    }
    public readonly Dictionary<string,Bone> Bones=new Dictionary<string,Bone>();
    public readonly HashSet<string> Detached=new HashSet<string>();
    public Vector2 FootNear,FootFar;
    public bool Left;
    public void Add(string name,Vector2 a,Vector2 b){Bones[name]=new Bone(a,b);}
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
    public static CharacterPose Sample(string clip,float phase,bool left=false){
        var p=Bind(left);float f=left?-1:1,w=clip=="walk"?1:0,t=Mathf.Repeat(phase,1),cycle=t*Mathf.PI*2;
        float waist=.985f-.006f*(1-Mathf.Cos(cycle*2))*w;
        p.Add("pelvis",new Vector2(0,waist-.15f),new Vector2(0,waist));p.Add("belly",new Vector2(0,waist),new Vector2(0,waist+.15f));p.Add("torso",new Vector2(0,waist+.15f),new Vector2(0,waist+.35f));p.Add("neck",new Vector2(0,waist+.35f),new Vector2(0,waist+.44f));p.Add("head",new Vector2(0,waist+.44f),new Vector2(0,waist+.74f));
        foreach(string side in new[]{"Far","Near"}){
            bool near=side=="Near";float x=(near?.06f:-.06f)*f;
            float arm=.12f+(near?-1:1)*Mathf.Sin(cycle)*.26f*w,fore=arm+.33f;
            Vector2 a=new Vector2(x,waist+.31f),b=a+new Vector2(f*Mathf.Sin(arm),-Mathf.Cos(arm))*.2828427f,c=b+new Vector2(f*Mathf.Sin(fore),-Mathf.Cos(fore))*.2570992f;
            p.Add("upper"+side,a,b);p.Add("forearm"+side,b,c);p.Add("hand"+side,c,c+new Vector2(f*Mathf.Sin(fore),-Mathf.Cos(fore))*.098234f);
            float q=Mathf.Repeat(t+(near?0:.5f),1),sx,sy;
            if(q<.65f){float v=q/.65f;v=v*v*(3-2*v);sx=Mathf.Lerp(.1f,-.1f,v);sy=0;}
            else {float v=(q-.65f)/.35f;sy=Mathf.Sin(v*Mathf.PI)*.045f;v=v*v*(3-2*v);sx=Mathf.Lerp(-.1f,.1f,v);}
            a=new Vector2(x,waist-.12f);c=Vector2.Lerp(new Vector2(f*(near?.075f:-.075f),.13f),new Vector2(f*sx,.13f+sy),w);
            Vector2 d=c-a;float length=d.magnitude,upper=.3929377f,lower=.3939543f,along=(upper*upper-lower*lower+length*length)/(2*length),bend=Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along));Vector2 axis=d/length;
            b=a+axis*along+new Vector2(-axis.y,axis.x)*bend*f;
            p.Add("thigh"+side,a,b);p.Add("shin"+side,b,c);p.Add("foot"+side,c+new Vector2(-f*.08f,-.055f),c+new Vector2(f*.16f,-.055f));if(near)p.FootNear=c;else p.FootFar=c;
        }return p;
    }
}
}
