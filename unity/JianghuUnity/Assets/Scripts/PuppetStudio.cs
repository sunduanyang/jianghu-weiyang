using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    string puppetHead="traveller",puppetUpper="traveller",puppetLower="traveller",puppetWaist="traveller",puppetFacing="right",puppetExpression="calm",puppetPose="idle";
    float puppetStep=100,puppetWear;
    bool puppetJoints,puppetAnimate,puppetLoaded;
    J puppetIndex=new J();
    readonly Dictionary<string,Texture2D> puppetTextures=new Dictionary<string,Texture2D>();
    readonly Dictionary<string,Color32[]> puppetPixels=new Dictionary<string,Color32[]>();
    Color32[] puppetCanvas;
    Texture2D puppetComposite;
    string puppetRenderKey="";
    readonly string[] puppetTypes={"traveller","daoist","escort"};
    readonly Dictionary<string,string> puppetNames=new Dictionary<string,string>{{"traveller","行路侠客"},{"daoist","山中老道"},{"escort","女镖师"},{"idle","立身"},{"walk","迈步"},{"lunge","弓步出剑"},{"left","朝左"},{"right","朝右"},{"calm","平静"},{"angry","怒"},{"sad","悲"}};
    class RigPose {public Dictionary<string,Vector2> p=new Dictionary<string,Vector2>();public float lean,front,back,sword;}
    struct Piece {public string slot;public Vector2 point;public float angle;public Piece(string s,Vector2 p,float a){slot=s;point=p;angle=a;}}
    string PuppetCycle(string label,string current,string[] options){GUILayout.BeginHorizontal();T(label,small);GUILayout.FlexibleSpace();if(GUILayout.Button(puppetNames[current]+"  ›",chip,GUILayout.Width(150))){int i=Array.IndexOf(options,current);current=options[(i+1)%options.Length];}GUILayout.EndHorizontal();return current;}
    void DrawPuppets(){
        if(!puppetIndex.Has){puppetIndex=J.Parse(Resources.Load<TextAsset>("Puppets/index").text);LoadPuppet();}
        H("皮影换装台");T("平面皮革 · 固定铰点 · 头茬与身段分开 · 前后裙片独立旋转",small);
        GUILayout.BeginHorizontal();GUILayout.BeginVertical(card,GUILayout.Width(245));
        foreach(string type in puppetTypes)if(Btn(puppetNames[type]))puppetHead=puppetUpper=puppetLower=puppetWaist=type;
        puppetHead=PuppetCycle("头茬",puppetHead,puppetTypes);puppetUpper=PuppetCycle("上身双臂",puppetUpper,puppetTypes);puppetLower=PuppetCycle("下身双腿",puppetLower,puppetTypes);puppetWaist=PuppetCycle("腰饰",puppetWaist,puppetTypes);
        puppetFacing=PuppetCycle("朝向",puppetFacing,new[]{"right","left"});puppetExpression=PuppetCycle("神情",puppetExpression,new[]{"calm","angry","sad"});puppetPose=PuppetCycle("动作",puppetPose,new[]{"idle","walk","lunge"});
        T("动作进度 "+Mathf.RoundToInt(puppetStep)+"%",small);puppetStep=GUILayout.HorizontalSlider(puppetStep,0,100);T("磨损覆盖 "+Mathf.RoundToInt(puppetWear)+"%",small);puppetWear=GUILayout.HorizontalSlider(puppetWear,0,100);puppetJoints=GUILayout.Toggle(puppetJoints,"标出关节点");puppetAnimate=GUILayout.Toggle(puppetAnimate,"循环演示迈步");if(Btn("保存人物搭配"))SavePuppet();if(Btn("导出当前皮影 SVG"))ExportPuppet();GUILayout.EndVertical();
        GUILayout.BeginVertical();Rect r=GUILayoutUtility.GetRect(690,470,GUILayout.ExpandWidth(true));var lit=UI("screen");if(lit!=null)GUI.DrawTexture(r,lit,ScaleMode.StretchToFill);else Fill(r,new Color(.93f,.87f,.73f));Outline(r,new Color(.17f,.14f,.11f,.45f));float step=puppetAnimate?(Time.realtimeSinceStartup*.3f%1)*100:puppetStep;DrawPuppet(r,step);T("皮影沿用原版分件轮廓，左右衣襟独立绘制。动作由 Unity 原生关节变换驱动。",small);T("搭配可独立保存；皮影试样尚未接到 NPC 换装。",small);GUILayout.EndVertical();GUILayout.EndHorizontal();
    }
    static Vector2 Rot(Vector2 p,float d){float a=d*Mathf.Deg2Rad;return new Vector2(p.x*Mathf.Cos(a)-p.y*Mathf.Sin(a),p.x*Mathf.Sin(a)+p.y*Mathf.Cos(a));}
    static float RigAngle(Vector2 a,Vector2 b)=>Mathf.Atan2(a.x-b.x,b.y-a.y)*Mathf.Rad2Deg;
    static Vector2 Knee(Vector2 hip,Vector2 ankle){Vector2 delta=ankle-hip;float d=delta.magnitude,along=(110*110-112*112+d*d)/(2*d),height=Mathf.Sqrt(Mathf.Max(0,110*110-along*along));Vector2 center=hip+delta*(along/d);return center+new Vector2(delta.y,-delta.x)*(height/d);}
    RigPose Geometry(float step){
        float t=step/100,f=puppetFacing=="left"?-1:1;Vector2 hip=Vector2.zero;float lean=0,farX=-32,nearX=37,un=-17,en=-21,uf=11,ef=8,sword=51;
        string pose=puppetAnimate?"walk":puppetPose;
        if(pose=="lunge"){hip=new Vector2(12*t,62*t);lean=17*t;farX=Mathf.Lerp(-32,-131,t);nearX=Mathf.Lerp(37,156,t);un=Mathf.Lerp(-17,-88,t);en=Mathf.Lerp(-21,-2,t);uf=Mathf.Lerp(11,83,t);ef=Mathf.Lerp(8,4,t);sword=Mathf.Lerp(51,0,t);}
        else if(pose=="walk"){float wave=Mathf.Sin(t*Mathf.PI*2),stride=wave*77;hip=new Vector2(0,16+10*Mathf.Abs(wave));lean=3;farX=-18-stride;nearX=18+stride;un=-17+wave*22;uf=11-wave*22;sword=54;}
        var g=new RigPose();var p=g.p;p["hip"]=hip;p["neck"]=hip+Rot(new Vector2(0,-160),lean);p["shoulderFar"]=hip+Rot(new Vector2(-7,-137),lean);p["shoulderNear"]=hip+Rot(new Vector2(6,-137),lean);
        p["elbowNear"]=p["shoulderNear"]+Rot(new Vector2(0,88),un+lean);p["wristNear"]=p["elbowNear"]+Rot(new Vector2(0,80),un+en+lean);p["elbowFar"]=p["shoulderFar"]+Rot(new Vector2(0,88),uf+lean);p["wristFar"]=p["elbowFar"]+Rot(new Vector2(0,80),uf+ef+lean);
        p["ankleNear"]=new Vector2(nearX,213);p["ankleFar"]=new Vector2(farX,213);p["kneeNear"]=Knee(hip,p["ankleNear"]);p["kneeFar"]=Knee(hip,p["ankleFar"]);p["skirtFront"]=hip+Rot(new Vector2(7,0),lean);p["skirtBack"]=hip+Rot(new Vector2(-7,0),lean);
        foreach(string key in p.Keys.ToArray())p[key]=new Vector2(p[key].x*f,p[key].y);g.lean=lean*f;g.front=RigAngle(p["hip"],p["kneeNear"])*.72f;g.back=RigAngle(p["hip"],p["kneeFar"])*.72f;g.sword=f>0?sword:180-sword;return g;
    }
    List<Piece> Pieces(RigPose g){var p=g.p;return new List<Piece>{new Piece("skirtBack",p["skirtBack"],g.back),new Piece("thighFar",p["hip"],RigAngle(p["hip"],p["kneeFar"])),new Piece("shinFar",p["kneeFar"],RigAngle(p["kneeFar"],p["ankleFar"])),new Piece("shoeFar",p["ankleFar"],0),new Piece("upperArmFar",p["shoulderFar"],RigAngle(p["shoulderFar"],p["elbowFar"])),new Piece("forearmFar",p["elbowFar"],RigAngle(p["elbowFar"],p["wristFar"])),new Piece("thighNear",p["hip"],RigAngle(p["hip"],p["kneeNear"])),new Piece("shinNear",p["kneeNear"],RigAngle(p["kneeNear"],p["ankleNear"])),new Piece("shoeNear",p["ankleNear"],0),new Piece("skirtFront",p["skirtFront"],g.front),new Piece("torso",p["hip"],g.lean),new Piece("head",p["neck"],g.lean),new Piece("waist",p["hip"],g.lean),new Piece("upperArmNear",p["shoulderNear"],RigAngle(p["shoulderNear"],p["elbowNear"])),new Piece("weapon",p["wristNear"],g.sword),new Piece("forearmNear",p["elbowNear"],RigAngle(p["elbowNear"],p["wristNear"]))};}
    J Part(string slot){string type=slot=="head"?puppetHead:slot=="waist"?puppetWaist:slot.StartsWith("thigh")||slot.StartsWith("shin")||slot.StartsWith("shoe")||slot.StartsWith("skirt")?puppetLower:puppetUpper;return puppetIndex["states"][type+"-"+puppetFacing+"-"+puppetExpression].A.First(p=>p["slot"].S==slot);}
    Texture2D PuppetTexture(string key){if(string.IsNullOrEmpty(key))return null;if(!puppetTextures.TryGetValue(key,out var texture)){texture=Resources.Load<Texture2D>("Puppets/"+key);puppetTextures[key]=texture;}return texture;}
    void DrawPuppet(Rect r,float step){
        string key=puppetHead+puppetUpper+puppetLower+puppetWaist+puppetFacing+puppetExpression+puppetPose+puppetAnimate+Mathf.RoundToInt(step)+":"+Mathf.RoundToInt(puppetWear)+puppetJoints;
        if(Event.current.type==EventType.Repaint&&key!=puppetRenderKey){
            if(puppetComposite==null){puppetComposite=new Texture2D(900,620,TextureFormat.RGBA32,false);puppetComposite.filterMode=FilterMode.Bilinear;puppetCanvas=new Color32[900*620];}
            Array.Clear(puppetCanvas,0,puppetCanvas.Length);var g=Geometry(step);
            foreach(var piece in Pieces(g)){var part=Part(piece.slot);RasterPart(piece,part,part["base"].S,1);if(puppetWear>0)RasterPart(piece,part,part[puppetWear>35?"high":"low"].S,puppetWear/100);}
            for(int x=120;x<780;x++)puppetCanvas[(620-563)*900+x]=new Color32(160,140,100,80);
            if(puppetJoints)foreach(var p in g.p.Values){int x=Mathf.RoundToInt(p.x+450),y=619-Mathf.RoundToInt(p.y+330);for(int dy=-3;dy<=3;dy++)for(int dx=-3;dx<=3;dx++)if(x+dx>=0&&x+dx<900&&y+dy>=0&&y+dy<620)puppetCanvas[(y+dy)*900+x+dx]=new Color32(170,45,30,255);}
            puppetComposite.SetPixels32(puppetCanvas);puppetComposite.Apply(false);puppetRenderKey=key;
        }
        if(puppetComposite!=null)GUI.DrawTexture(r,puppetComposite,ScaleMode.ScaleToFit);
    }
    void RasterPart(Piece piece,J part,string textureKey,float opacity){
        var texture=PuppetTexture(textureKey);if(texture==null)return;if(!puppetPixels.TryGetValue(textureKey,out var pixels)){pixels=texture.GetPixels32();puppetPixels[textureKey]=pixels;}
        var box=part["box"];float bx=(float)box[0].N,by=(float)box[1].N,bw=(float)box[2].N,bh=(float)box[3].N;float angle=piece.angle*Mathf.Deg2Rad,cos=Mathf.Cos(angle),sin=Mathf.Sin(angle);
        var corners=new[]{new Vector2(bx,by),new Vector2(bx+bw,by),new Vector2(bx,by+bh),new Vector2(bx+bw,by+bh)}.Select(p=>Rot(p,piece.angle)+piece.point).ToArray();
        int x0=Mathf.Max(0,Mathf.FloorToInt(corners.Min(p=>p.x)+450)),x1=Mathf.Min(899,Mathf.CeilToInt(corners.Max(p=>p.x)+450)),y0=Mathf.Max(0,Mathf.FloorToInt(corners.Min(p=>p.y)+330)),y1=Mathf.Min(619,Mathf.CeilToInt(corners.Max(p=>p.y)+330));
        for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++){
            float wx=x-450-piece.point.x,wy=y-330-piece.point.y,lx=wx*cos+wy*sin,ly=-wx*sin+wy*cos;if(lx<bx||lx>=bx+bw||ly<by||ly>=by+bh)continue;
            int sx=Mathf.Clamp((int)((lx-bx)/bw*texture.width),0,texture.width-1),sy=Mathf.Clamp(texture.height-1-(int)((ly-by)/bh*texture.height),0,texture.height-1);Color32 s=pixels[sy*texture.width+sx];if(s.a==0)continue;int at=(619-y)*900+x;Color32 d=puppetCanvas[at];float sa=s.a/255f*opacity,da=d.a/255f,oa=sa+da*(1-sa);if(oa<=0)continue;float tail=da*(1-sa);puppetCanvas[at]=new Color32((byte)((s.r*sa+d.r*tail)/oa),(byte)((s.g*sa+d.g*tail)/oa),(byte)((s.b*sa+d.b*tail)/oa),(byte)(oa*255));
        }
    }
    void SavePuppet(){var j=J.O("head",puppetHead,"upper",puppetUpper,"lower",puppetLower,"waist",puppetWaist,"facing",puppetFacing,"expression",puppetExpression,"pose",puppetPose,"step",puppetStep,"wear",puppetWear,"joints",puppetJoints);PlayerPrefs.SetString("jianghu.puppet.v3",j.ToString());PlayerPrefs.Save();Client.Status="皮影搭配已保存";}
    void LoadPuppet(){if(puppetLoaded)return;puppetLoaded=true;try{var j=J.Parse(PlayerPrefs.GetString("jianghu.puppet.v3","{}"));if(!j["head"].Has)return;puppetHead=j["head"].S;puppetUpper=j["upper"].S;puppetLower=j["lower"].S;puppetWaist=j["waist"].S;puppetFacing=j["facing"].S;puppetExpression=j["expression"].S;puppetPose=j["pose"].S;puppetStep=(float)j["step"].N;puppetWear=(float)j["wear"].N;puppetJoints=j["joints"].B;}catch{}}
    void ExportPuppet(){var b=new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"-450 -330 900 620\">");foreach(var p in Pieces(Geometry(puppetStep))){J part=Part(p.slot);b.Append("<g transform=\"translate(").Append(p.point.x.ToString("0.#####",CultureInfo.InvariantCulture)).Append(' ').Append(p.point.y.ToString("0.#####",CultureInfo.InvariantCulture)).Append(") rotate(").Append(p.angle.ToString("0.#####",CultureInfo.InvariantCulture)).Append(")\">").Append(part["svg"].S);if(puppetWear>0)b.Append("<g opacity=\"").Append((puppetWear/100).ToString("0.###",CultureInfo.InvariantCulture)).Append("\">").Append(part[puppetWear>35?"wearHigh":"wearLow"].S).Append("</g>");b.Append("</g>");}b.Append("</svg>");string path=Path.Combine(Client.SaveDir,"puppet-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".svg");File.WriteAllText(path,b.ToString());Client.Status="皮影已导出："+path;}
    public bool ValidateSourceRig(out double maxError){maxError=0;string pose=puppetPose,facing=puppetFacing;bool animate=puppetAnimate;puppetAnimate=false;foreach(var source in J.Parse(Resources.Load<TextAsset>("Puppets/source-poses").text).A){puppetPose=source["pose"].S;puppetFacing=source["facing"].S;var g=Geometry(source["step"].I);foreach(var p in g.p){var expected=source["geometry"][p.Key];maxError=Math.Max(maxError,Vector2.Distance(p.Value,new Vector2((float)expected["x"].N,(float)expected["y"].N)));}foreach(var part in Pieces(g)){var data=Part(part.slot);if(PuppetTexture(data["base"].S)==null)return false;}}puppetPose=pose;puppetFacing=facing;puppetAnimate=animate;return maxError<.001;}
    public void PuppetSmokeVariant(){puppetHead="daoist";puppetUpper="escort";puppetLower="traveller";puppetWaist="daoist";puppetPose="lunge";puppetFacing="left";puppetExpression="angry";puppetWear=65;puppetStep=100;puppetJoints=true;ExportPuppet();}
}
}
