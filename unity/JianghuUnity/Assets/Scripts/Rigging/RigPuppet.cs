using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// 由绑定拼出的木偶：原样拼合的姿势、绑定台里的提线 / 落地布娃娃，以及在 IMGUI 中绘制一块不拉伸的拆件。
// 关节坐标与拆件同为像素、y 向下。
public sealed class RigPuppet {
    public sealed class Bone {public string Id,A,B;public float Length,Drawn;}
    sealed class Link {public string A,B;public float Length;}
    sealed class JointLimit {public Bone Child,Parent;public float Rest,Min,Max;}
    sealed class Particle {public Vector2 P,Prev;}
    public enum Mode {Bind,Hang,Fall}

    public readonly RigBinding Rig;
    public readonly List<string> Ids=new List<string>();
    public readonly Dictionary<string,Vector2> Origin=new Dictionary<string,Vector2>();   // 原样拼合时每块左上角
    public readonly Dictionary<string,Vector2> BindJoints=new Dictionary<string,Vector2>();
    public readonly List<Bone> Bones=new List<Bone>();
    public readonly Dictionary<string,Bone> BoneOf=new Dictionary<string,Bone>();
    public Rect Bounds;
    readonly List<Link> links=new List<Link>();
    readonly List<JointLimit> limits=new List<JointLimit>();
    readonly HashSet<string> torsoJoints=new HashSet<string>();
    readonly Dictionary<string,Particle> particles=new Dictionary<string,Particle>();
    Vector2? anchor;

    public RigPuppet(RigBinding rig){
        Rig=rig;
        foreach(var s in RigSlots.All)if(rig.Usable(s.Id))Ids.Add(s.Id);
        foreach(var id in Ids)Place(id,0);
        foreach(var id in Ids)foreach(var p in RigSlots.ById[id].Points){
            if(p.Key=="grip")continue;   // 握点只决定手的朝向和兵器位置，不是关节
            if(!rig.TrySocket(id,p.Key,out var v))continue;string j=JointId(id,p.Key);
            if(!BindJoints.ContainsKey(j))BindJoints[j]=Origin[id]+v;
        }
        foreach(var id in Ids){
            var b=new Bone{Id=id,A=JointId(id,"pivot"),B=JointId(id,"tip"),Length=rig.BoneLength(id),Drawn=Angle(rig.Point(id,"pivot"),rig.Point(id,"tip"))};
            Bones.Add(b);BoneOf[id]=b;if(b.Length>1)links.Add(new Link{A=b.A,B=b.B,Length=b.Length});
        }
        if(Ids.Contains("torso")){
            foreach(var p in RigSlots.ById["torso"].Points){string j=JointId("torso",p.Key);if(BindJoints.ContainsKey(j))torsoJoints.Add(j);}
            var list=torsoJoints.ToList();
            for(int i=0;i<list.Count;i++)for(int k=i+1;k<list.Count;k++){float d=Vector2.Distance(BindJoints[list[i]],BindJoints[list[k]]);if(d>.5f)links.Add(new Link{A=list[i],B=list[k],Length=d});}
        }
        foreach(var b in Bones){
            if(!rig.Limits.TryGetValue(b.Id,out var l))continue;
            string parentId=l.TorsoRelative?"torso":rig.ParentRef(b.Id).Split('.')[0];
            if(!BoneOf.TryGetValue(parentId,out var pb))continue;
            float rest=Mathf.DeltaAngle(BindAngle(pb),BindAngle(b));
            // 限位按面朝右定义（负值向前、向上）；原画面朝左时左右相反。
            limits.Add(new JointLimit{Child=b,Parent=pb,Rest=rest,Min=rig.ArtFacesLeft?-l.Max:l.Min,Max=rig.ArtFacesLeft?-l.Min:l.Max});
        }
        if(Ids.Count>0){
            float x0=float.MaxValue,y0=float.MaxValue,x1=float.MinValue,y1=float.MinValue;
            foreach(var id in Ids){var o=Origin[id];var p=rig.SlotPiece(id);x0=Mathf.Min(x0,o.x);y0=Mathf.Min(y0,o.y);x1=Mathf.Max(x1,o.x+p.Width);y1=Mathf.Max(y1,o.y+p.Height);}
            Bounds=Rect.MinMaxRect(x0,y0,x1,y1);
        }
        Reset(Mode.Bind);
    }
    void Place(string id,int depth){
        if(Origin.ContainsKey(id)||depth>20)return;
        Vector2 at=Vector2.zero;string pr=Rig.ParentRef(id);
        if(pr!=null){var parts=pr.Split('.');Place(parts[0],depth+1);Rig.TrySocket(parts[0],parts[1],out var socket);at=Origin[parts[0]]+socket;}
        Origin[id]=at-Rig.Point(id,"pivot");
    }
    // 子块的起点就是上一级的接口，二者共用一个关节。
    public string JointId(string id,string key){
        if(key=="pivot"){string pr=Rig.ParentRef(id);if(pr!=null){var parts=pr.Split('.');return JointId(parts[0],parts[1]);}}
        if(id=="torso"&&(key=="hipNear"||key=="hipFar")&&!Rig.HasPoint("torso",key))return "torso.pivot";
        return id+"."+key;
    }
    static float Angle(Vector2 a,Vector2 b)=>Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg;
    float BindAngle(Bone b)=>Angle(BindJoints[b.A],BindJoints[b.B]);
    public bool Has(string id)=>BoneOf.ContainsKey(id);

    // ---------------------------------------------------------------- ragdoll
    public void Reset(Mode mode){
        particles.Clear();anchor=null;
        foreach(var j in BindJoints)particles[j.Key]=new Particle{P=j.Value,Prev=j.Value};
        string pin=PinJoint;if(mode==Mode.Hang&&pin!=null)particles[pin].Prev+=new Vector2(-6,0);
    }
    public string PinJoint=>particles.ContainsKey("head.tip")?"head.tip":particles.ContainsKey("torso.tip")?"torso.tip":null;
    public Vector2 Joint(string j)=>particles.TryGetValue(j,out var p)?p.P:BindJoints.TryGetValue(j,out var b)?b:Vector2.zero;
    public IEnumerable<string> JointIds=>particles.Keys;
    public Vector2? Anchor=>anchor;
    public float Ground=>Bounds.yMax+Bounds.height*.02f;
    public void Step(Mode mode,string drag,Vector2 dragTo){
        if(mode==Mode.Bind||particles.Count==0)return;
        float H=Mathf.Max(1,Bounds.height),g=H*.0009f;string pin=PinJoint;
        if(pin!=null&&anchor==null)anchor=BindJoints[pin]+new Vector2(0,-H*.06f);
        foreach(var pair in particles){var p=pair.Value;float keep=torsoJoints.Contains(pair.Key)?.97f:.985f;Vector2 v=(p.P-p.Prev)*keep;p.Prev=p.P;p.P+=v+new Vector2(0,g);}
        float left=Bounds.xMin-Bounds.width*.9f,right=Bounds.xMax+Bounds.width*.9f;
        for(int it=0;it<18;it++){
            foreach(var l in links){
                var a=particles[l.A];var b=particles[l.B];Vector2 d=b.P-a.P;float len=Mathf.Max(1e-4f,d.magnitude),diff=(len-l.Length)/len;
                float wa=torsoJoints.Contains(l.A)?.35f:1,wb=torsoJoints.Contains(l.B)?.35f:1,sum=wa+wb;
                a.P+=d*diff*wa/sum;b.P-=d*diff*wb/sum;
            }
            foreach(var L in limits){
                var pa=particles[L.Parent.A];var pb=particles[L.Parent.B];var ca=particles[L.Child.A];var cb=particles[L.Child.B];
                float parent=Angle(pa.P,pb.P),child=Angle(ca.P,cb.P),rel=Mathf.DeltaAngle(parent+L.Rest,child);
                if(rel<L.Min||rel>L.Max){
                    float target=(parent+L.Rest+Mathf.Clamp(rel,L.Min,L.Max))*Mathf.Deg2Rad;
                    Vector2 goal=ca.P+new Vector2(Mathf.Cos(target),Mathf.Sin(target))*L.Child.Length;cb.P+=(goal-cb.P)*.5f;
                }
            }
            if(mode==Mode.Hang&&pin!=null&&anchor.HasValue&&drag!=pin){
                var p=particles[pin];Vector2 d=p.P-anchor.Value;float rope=H*.06f;if(d.magnitude>rope)p.P=anchor.Value+d.normalized*rope;
            }
            if(mode==Mode.Fall)foreach(var p in particles.Values){
                if(p.P.y>Ground){p.P.y=Ground;p.Prev.x=p.P.x-(p.P.x-p.Prev.x)*.5f;}
                if(p.P.x<left){p.P.x=left;p.Prev.x=left;}if(p.P.x>right){p.P.x=right;p.Prev.x=right;}
            }
            if(drag!=null&&particles.TryGetValue(drag,out var dp))dp.P=dragTo;
        }
    }
    public string Nearest(Vector2 at){
        string best=null;float bestD=float.MaxValue;
        foreach(var p in particles){float d=Vector2.Distance(p.Value.P,at);if(d<bestD){bestD=d;best=p.Key;}}
        foreach(var b in Bones){
            Vector2 A=Joint(b.A),B=Joint(b.B),ab=B-A;float t=Mathf.Clamp01(Vector2.Dot(at-A,ab)/Mathf.Max(1e-4f,ab.sqrMagnitude));
            float d=Vector2.Distance(A+ab*t,at);if(d<bestD*.6f){bestD=d;best=t>.5f?b.B:b.A;}
        }
        return best;
    }
    public float MaxStretch(){float worst=0;foreach(var b in Bones)if(b.Length>1)worst=Mathf.Max(worst,Mathf.Abs(Vector2.Distance(Joint(b.A),Joint(b.B))-b.Length)/b.Length);return worst;}
    public bool Finite=>particles.Values.All(p=>!float.IsNaN(p.P.x)&&!float.IsNaN(p.P.y)&&!float.IsInfinity(p.P.x)&&!float.IsInfinity(p.P.y));

    // 以当前姿势绘制。toScreen 把像素坐标映射到当前 GUI 坐标（缩放 scale）；clipOrigin 为外层 BeginArea/BeginClip 的累计偏移。
    public void Draw(Mode mode,System.Func<Vector2,Vector2> toScreen,float scale,Vector2 clipOrigin,Color tint){
        foreach(var id in Rig.Order){
            if(!BoneOf.TryGetValue(id,out var b))continue;var piece=Rig.SlotPiece(id);if(piece?.Tex==null)continue;
            if(mode==Mode.Bind){var o=toScreen(Origin[id]);var old=GUI.color;GUI.color=tint;GUI.DrawTexture(new Rect(o.x,o.y,piece.Width*scale,piece.Height*scale),piece.Tex);GUI.color=old;continue;}
            Vector2 A=Joint(b.A),B=Joint(b.B);
            DrawPiece(piece.Tex,toScreen(A),clipOrigin,Angle(A,B)-b.Drawn,Rig.Point(id,"pivot"),scale,false,tint);
        }
    }
    // 绘制一块不拉伸的拆件：pivot（拆件像素）放到 anchor，绕它旋转 angle 度。mirror 时左右翻转。
    public static void DrawPiece(Texture tex,Vector2 anchor,Vector2 clipOrigin,float angle,Vector2 pivot,float scale,bool mirror,Color tint){
        var old=GUI.matrix;var oldColor=GUI.color;
        try{
            Vector3 u=anchor+clipOrigin;
            GUI.matrix=old*Matrix4x4.Translate(u)*Matrix4x4.Rotate(Quaternion.Euler(0,0,angle))*Matrix4x4.Translate(-u);
            float px=mirror?tex.width-pivot.x:pivot.x;
            GUI.color=tint;
            GUI.DrawTextureWithTexCoords(new Rect(anchor.x-px*scale,anchor.y-pivot.y*scale,tex.width*scale,tex.height*scale),tex,mirror?new Rect(1,0,-1,1):new Rect(0,0,1,1),true);
        }finally{GUI.matrix=old;GUI.color=oldColor;}
    }
}
}
