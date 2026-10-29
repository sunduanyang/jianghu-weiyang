using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
// 一个部位此刻在世界中的骨骼两端（米，y 向上）。Sole=true 表示脚：沿脚底方向摆放，起点是脚踝。
public struct RigBoneSample {
    public Vector2 A,B;public bool Sole;
    public RigBoneSample(Vector2 a,Vector2 b,bool sole=false){A=a;B=b;Sole=sole;}
}
// 把绑定好的拆件画在任意骨架上。只需要每个部位的骨骼两端，不依赖哪套物理引擎，
// 所以切磋（现在的 Rigidbody2D，或以后的 Physics Core 2D）、绑定台的切磋预览都用它。
public static class RigPoseRenderer {
    // 用来求统一比例、也允许接缝补偿的骨骼。
    public static readonly string[] FitBones={"torso","upperNear","upperFar","forearmNear","forearmFar","thighNear","thighFar","shinNear","shinFar"};
    // 子部位 -> 它的起点接在上一级的哪个接口。用于测量接缝。
    static readonly (string child,string parent,string key)[] Seams={
        ("head","torso","tip"),("upperNear","torso","shoulderNear"),("upperFar","torso","shoulderFar"),
        ("forearmNear","upperNear","tip"),("forearmFar","upperFar","tip"),("handNear","forearmNear","tip"),("handFar","forearmFar","tip"),
        ("thighNear","torso","hipNear"),("thighFar","torso","hipFar"),("shinNear","thighNear","tip"),("shinFar","thighFar","tip"),
        ("footNear","shinNear","tip"),("footFar","shinFar","tip")};

    // 米 / 原画像素：最小二乘，使原画骨长整体最接近骨架骨长。
    public static float FitScale(RigBinding rig,Func<string,RigBoneSample?> bone){
        double num=0,den=0;
        foreach(var id in FitBones){
            if(!rig.Usable(id))continue;var s=bone(id);if(!s.HasValue)continue;
            double art=rig.BoneLength(id),phys=Vector2.Distance(s.Value.A,s.Value.B);if(art<1||phys<=0)continue;
            num+=art*phys;den+=art*art;
        }
        return den>0?(float)(num/den):0;
    }

    sealed class Placed {public Vector2 Anchor;public float Turn,Scale;public bool Ok;}
    // 计算每块的位置、转角和缩放（屏幕像素 / 原画像素）。
    static Dictionary<string,Placed> Place(RigBinding rig,Func<string,RigBoneSample?> bone,Func<Vector2,Vector2> toScreen,float metersPerPixel,float screenPerMeter,bool facingLeft){
        var result=new Dictionary<string,Placed>();bool mirror=facingLeft!=rig.ArtFacesLeft;float basePx=metersPerPixel*screenPerMeter;
        Placed torso=null;
        foreach(var def in RigSlots.All){
            string id=def.Id;if(!rig.Usable(id)||rig.SlotPiece(id)?.Tex==null)continue;
            if(id=="skirtFront"||id=="skirtBack")continue;
            var s=bone(id);if(!s.HasValue)continue;
            Vector2 a=toScreen(s.Value.A),b=toScreen(s.Value.B);
            float screen=Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg;
            float drawn=s.Value.Sole?(rig.ArtFacesLeft?180:0):rig.DrawnAngle(id);if(mirror)drawn=180-drawn;
            float scale=basePx;
            if(rig.FitTolerance>0&&Array.IndexOf(FitBones,id)>=0){
                float art=rig.BoneLength(id)*metersPerPixel,phys=Vector2.Distance(s.Value.A,s.Value.B);
                if(art>0)scale*=Mathf.Clamp(phys/art,1-rig.FitTolerance,1+rig.FitTolerance);
            }
            var p=new Placed{Anchor=a,Turn=screen-drawn,Scale=scale,Ok=true};result[id]=p;if(id=="torso")torso=p;
        }
        if(torso!=null)foreach(var id in new[]{"skirtFront","skirtBack"})
            if(rig.Usable(id)&&rig.SlotPiece(id)?.Tex!=null)result[id]=new Placed{Anchor=torso.Anchor,Turn=torso.Turn,Scale=basePx,Ok=true};
        return result;
    }
    // 原画上的一点在屏幕上的位置（与 RigPuppet.DrawPiece 的翻转、旋转一致）。
    static Vector2 ArtToScreen(RigBinding rig,string id,Placed p,Vector2 art,bool mirror){
        Vector2 v=(art-rig.Point(id,"pivot"))*p.Scale;if(mirror)v.x=-v.x;
        float r=p.Turn*Mathf.Deg2Rad,c=Mathf.Cos(r),s=Mathf.Sin(r);
        return p.Anchor+new Vector2(v.x*c-v.y*s,v.x*s+v.y*c);
    }

    // 按绑定的绘制顺序画出整个人。nearHandWeapon 在近侧手画完后调用（画兵器），随后再补画一次近侧手，让拳头盖住剑柄。
    public static void Draw(RigBinding rig,Func<string,RigBoneSample?> bone,Func<Vector2,Vector2> toScreen,float metersPerPixel,float screenPerMeter,bool facingLeft,Color tint,Vector2 clipOrigin,Action nearHandWeapon=null){
        var placed=Place(rig,bone,toScreen,metersPerPixel,screenPerMeter,facingLeft);bool mirror=facingLeft!=rig.ArtFacesLeft;bool weaponDone=false;
        foreach(var id in rig.Order){
            if(!placed.TryGetValue(id,out var p))continue;var piece=rig.SlotPiece(id);
            RigPuppet.DrawPiece(piece.Tex,p.Anchor,clipOrigin,p.Turn,rig.Point(id,"pivot"),p.Scale,mirror,tint);
            if(id=="handNear"&&nearHandWeapon!=null){nearHandWeapon();weaponDone=true;RigPuppet.DrawPiece(piece.Tex,p.Anchor,clipOrigin,p.Turn,rig.Point(id,"pivot"),p.Scale,mirror,tint);}
        }
        if(!weaponDone)nearHandWeapon?.Invoke();
    }
    // 每个关节的接缝：上一级拆件画出来的接口，与下一级拆件起点之间的距离，以原画像素计。
    public static Dictionary<string,float> MeasureSeams(RigBinding rig,Func<string,RigBoneSample?> bone,float metersPerPixel,bool facingLeft){
        Func<Vector2,Vector2> toScreen=w=>new Vector2(w.x,-w.y);   // 以米为屏幕单位，只取比例
        var placed=Place(rig,bone,toScreen,metersPerPixel,1,facingLeft);bool mirror=facingLeft!=rig.ArtFacesLeft;
        var gaps=new Dictionary<string,float>();
        foreach(var (child,parent,key) in Seams){
            if(!placed.TryGetValue(child,out var c)||!placed.TryGetValue(parent,out var p))continue;
            if(!rig.TrySocket(parent,key,out var socket))continue;
            gaps[child]=Vector2.Distance(ArtToScreen(rig,parent,p,socket,mirror),c.Anchor)/Mathf.Max(1e-6f,metersPerPixel);
        }
        return gaps;
    }
    // 接缝是否被别的部件挡住：远侧大臂的肩在身后；有前摆时髋被衣摆盖住。返回原因，看得见时返回 null。
    public static string SeamHidden(RigBinding rig,string child){
        if(child=="upperFar")return "身后";
        if((child=="thighNear"||child=="thighFar")&&(rig.Usable("skirtFront")||rig.Usable("skirtBack")))return "衣摆遮住";
        return null;
    }
    public static string SeamLabel(string child){
        switch(child){
            case "head":return "颈";case "upperNear":return "近侧肩";case "upperFar":return "远侧肩";
            case "forearmNear":return "近侧肘";case "forearmFar":return "远侧肘";case "handNear":return "近侧腕";case "handFar":return "远侧腕";
            case "thighNear":return "近侧髋";case "thighFar":return "远侧髋";case "shinNear":return "近侧膝";case "shinFar":return "远侧膝";
            case "footNear":return "近侧踝";case "footFar":return "远侧踝";default:return child;
        }
    }
}

// 切磋骨架出场时的姿势（与 DuelReference.BuildReference 的单手兵器初始姿势相同），供绑定台预览与验收使用。
public static class RigDuelPreview {
    public static Dictionary<string,RigBoneSample> SpawnPose(float x,float facing){
        var d=new Dictionary<string,RigBoneSample>();Vector2 p=new Vector2(x,0);
        Vector2 waist=p+new Vector2(0,.99f),neck=p+new Vector2(0,1.34f),headTop=p+new Vector2(0,1.73f);
        d["torso"]=new RigBoneSample(waist,neck);d["neck"]=new RigBoneSample(neck,p+new Vector2(0,1.43f));d["head"]=new RigBoneSample(neck,headTop);
        foreach(string side in new[]{"Far","Near"}){
            float offset=side=="Near"?.06f:-.06f;
            Vector2 shoulder=p+new Vector2(offset*facing,1.30f),elbow=shoulder+new Vector2(facing*.20f,-.20f),wrist=elbow+new Vector2(facing*.25f,.06f);
            d["upper"+side]=new RigBoneSample(shoulder,elbow);d["forearm"+side]=new RigBoneSample(elbow,wrist);d["hand"+side]=new RigBoneSample(wrist,wrist+new Vector2(facing*.095f,.025f));
            Vector2 hip=p+new Vector2(offset*facing,.87f),knee=p+new Vector2((offset+.10f)*facing,.49f),ankle=p+new Vector2((offset-.06f)*facing,.13f);
            d["thigh"+side]=new RigBoneSample(hip,knee);d["shin"+side]=new RigBoneSample(knee,ankle);d["foot"+side]=new RigBoneSample(ankle,ankle+new Vector2(facing*.24f,0),true);
        }
        return d;
    }
    public static Func<string,RigBoneSample?> Provider(Dictionary<string,RigBoneSample> pose,RigBinding rig)=>id=>{
        if(id=="head"&&rig.Usable("neck")&&pose.TryGetValue("neck",out var n))return new RigBoneSample(n.B,pose["head"].B);
        return pose.TryGetValue(id,out var s)?s:(RigBoneSample?)null;
    };
}
}
