using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// 系统 · 关节绑定：导入拆件图、分配部位、点关节、拖动木偶检查、保存并设为切磋外观。
public partial class JianghuApp {
    RigBinding rbRig;RigPuppet rbPuppet;string rbFolder,rbSel="torso",rbPt="pivot",rbStatus="",rbDragPt,rbDragJoint,rbName="";
    RigPuppet.Mode rbMode=RigPuppet.Mode.Hang;bool rbSheets,rbShowJoints=true,rbOpened;
    Vector2 rbDragTo,rbSlotScroll,rbTrayScroll,rbOrderScroll,rbSheetScroll;float rbClock;
    readonly Stack<string> rbUndo=new Stack<string>();
    static Texture2D rbDisc;
    // 切磋骨架的基准长度（米），见 DuelReference.BuildReference。用于比例检查。
    static readonly Dictionary<string,float> RbDuelLengths=new Dictionary<string,float>{
        {"torso",.35f},{"upperNear",.283f},{"upperFar",.283f},{"forearmNear",.257f},{"forearmFar",.257f},{"thighNear",.393f},{"thighFar",.393f},{"shinNear",.394f},{"shinFar",.394f}};

    public RigBinding RigBinderRig=>rbRig;
    public string RigBinderStatus=>rbStatus;

    void DrawRigBinder(){
        if(!rbOpened){rbOpened=true;RbOpenInitial();}
        GUILayoutUtility.GetRect(1280,760);   // 占住布局区域；下面全部按区域内坐标直接绘制
        RbKeys();
        RbTopBar(new Rect(0,0,1280,64));
        if(rbSheets||rbRig==null){RbSheetPicker(new Rect(0,72,1280,688));return;}
        RbSlots(new Rect(0,72,200,488));
        RbEditor(new Rect(210,72,510,488));
        RbPreview(new Rect(730,72,550,488));
        RbTray(new Rect(0,570,900,190));
        RbOrder(new Rect(910,570,370,190));
    }
    void RbOpenInitial(){
        var all=RigLibrary.List();string pick=all.ContainsKey(RigLibrary.Active)?RigLibrary.Active:all.Keys.FirstOrDefault();
        if(pick!=null)RbLoad(pick);else rbSheets=true;
    }
    void RbLoad(string folder){
        RigLibrary.Invalidate(folder);var all=RigLibrary.List();
        if(!all.TryGetValue(folder,out var dir)){rbStatus="找不到绑定："+folder;return;}
        try{rbRig=RigBinding.Load(dir);rbFolder=folder;rbName=rbRig.Name;rbUndo.Clear();rbSel="torso";rbPt="pivot";RbRebuild();rbStatus="已打开「"+rbRig.Name+"」。";}
        catch(Exception e){rbStatus="读取失败："+e.Message;}
    }
    void RbRebuild(){rbPuppet=rbRig==null?null:new RigPuppet(rbRig);rbPuppet?.Reset(rbMode);}
    void RbChange(){if(rbRig==null)return;rbUndo.Push(rbRig.SlotsSnapshot());while(rbUndo.Count>60){var keep=rbUndo.ToArray().Take(60).Reverse().ToArray();rbUndo.Clear();foreach(var s in keep)rbUndo.Push(s);}}
    void RbUndo(){if(rbRig==null||rbUndo.Count==0){rbStatus="没有可撤销的改动。";return;}rbRig.RestoreSlots(rbUndo.Pop());RbRebuild();rbStatus="已撤销。";}
    void RbKeys(){var e=Event.current;if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Z&&(e.control||e.command)){RbUndo();e.Use();}}

    // ---------------------------------------------------------------- top bar
    void RbTopBar(Rect r){
        float x=r.x;GUI.Label(new Rect(x,r.y+4,48,26),"角色",small);x+=44;
        foreach(var pair in RigLibrary.List()){
            string label=pair.Key+(pair.Key==RigLibrary.Active?" · 切磋中":"");float w=Mathf.Min(200,24+label.Length*15);
            if(x+w>r.xMax-330)break;
            if(GUI.Button(new Rect(x,r.y,w,28),label,pair.Key==rbFolder&&!rbSheets?selected:chip)){RbLoad(pair.Key);rbSheets=false;}
            x+=w+6;
        }
        if(GUI.Button(new Rect(r.xMax-322,r.y,150,28),"从拆件图新建",rbSheets?selected:chip))rbSheets=!rbSheets;
        if(GUI.Button(new Rect(r.xMax-166,r.y,166,28),"打开拆件图文件夹",chip)){Directory.CreateDirectory(RigLibrary.PlayerSheets);Application.OpenURL(RigLibrary.PlayerSheets);}
        if(rbRig==null){GUI.Label(new Rect(r.x,r.y+36,r.width,24),rbStatus,small);return;}
        float y=r.y+34;GUI.Label(new Rect(r.x,y+3,48,24),"名称",small);
        rbName=GUI.TextField(new Rect(r.x+44,y,170,26),rbName??"",40,field);
        x=r.x+222;
        if(GUI.Button(new Rect(x,y,70,26),"保存",selected))RbSave();x+=76;
        if(GUI.Button(new Rect(x,y,96,26),"撤销 Ctrl+Z",chip))RbUndo();x+=102;
        bool canDuel=rbFolder!=null&&RigLibrary.Get(rbFolder)!=null&&RigLibrary.ForDuelCandidate(rbRig);
        bool isActive=rbFolder!=null&&RigLibrary.Active==rbFolder;
        GUI.enabled=canDuel||isActive;
        if(GUI.Button(new Rect(x,y,126,26),isActive?"取消切磋外观":"设为切磋外观",isActive?selected:chip)){
            if(isActive){RigLibrary.Active="";rbStatus="切磋改回原来的人物外观。";}
            else{RigLibrary.Active=rbFolder;rbStatus="下次进入切磋时，你的角色会用「"+rbRig.Name+"」。";}
        }
        GUI.enabled=true;x+=132;
        bool enemy=GUI.Toggle(new Rect(x,y+3,90,22),RigLibrary.EnemyToo,"对手也用");if(enemy!=RigLibrary.EnemyToo)RigLibrary.EnemyToo=enemy;x+=96;
        if(GUI.Button(new Rect(x,y,150,26),"读入网页版 JSON",chip))RbImportClipboard();x+=156;
        if(GUI.Button(new Rect(x,y,90,26),"存放位置",chip)){Directory.CreateDirectory(RigLibrary.SaveRoot);Application.OpenURL(RigLibrary.SaveRoot);}x+=96;
        GUI.Label(new Rect(x,y+3,r.xMax-x,24),rbStatus,small);
    }
    void RbSave(){
        if(rbRig==null)return;rbRig.Name=string.IsNullOrEmpty(rbName?.Trim())?"新角色":rbName.Trim();
        string folder=RigLibrary.SafeFolder(rbRig.Name);string dir=Path.Combine(RigLibrary.SaveRoot,folder);
        try{rbRig.Save(dir);rbFolder=folder;RigLibrary.Invalidate(folder);
            rbStatus="已保存到 "+dir+(rbRig.RequiredDone<rbRig.RequiredTotal?"（还有 "+(rbRig.RequiredTotal-rbRig.RequiredDone)+" 个部位没完成）":"。");}
        catch(Exception e){rbStatus="保存失败："+e.Message;}
    }
    void RbImportClipboard(){
        string text=GUIUtility.systemCopyBuffer;
        try{
            var j=J.Parse(text);var imported=RigBinding.FromJson(j,null);int matched=0;var missing=new List<string>();RbChange();
            foreach(var pair in imported.Slots){
                var b=pair.Value;if(string.IsNullOrEmpty(b.Piece))continue;
                var piece=rbRig.Piece(b.Piece);
                if(piece==null){var tex=RbDecodeDataUrl(j["images"][b.Piece]["url"].S);if(tex!=null){piece=new RigPiece{Name=b.Piece,Tex=tex};rbRig.Pieces.Add(piece);}}
                if(piece==null){missing.Add(b.Piece);continue;}
                rbRig.Slots[pair.Key]=b.Copy();matched++;
            }
            if(imported.Order.Count>0)rbRig.Order=imported.Order;
            RbRebuild();rbStatus="读入 "+matched+" 个部位"+(missing.Count>0?"；找不到拆件："+string.Join("、",missing.Distinct()):"。");
        }catch(Exception e){rbStatus="剪贴板里不是绑定 JSON（"+e.Message+"）。先在网页版点「复制 JSON」。";}
    }
    static Texture2D RbDecodeDataUrl(string url){
        if(string.IsNullOrEmpty(url)||!url.StartsWith("data:image/png;base64,"))return null;
        var t=new Texture2D(2,2,TextureFormat.RGBA32,true){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear};
        return t.LoadImage(Convert.FromBase64String(url.Substring(url.IndexOf(',')+1)),false)?t:null;
    }

    // ---------------------------------------------------------------- sheets
    void RbSheetPicker(Rect r){
        Fill(r,new Color(.12f,.09f,.06f,.06f));
        GUI.Label(new Rect(r.x+16,r.y+10,r.width-32,26),"选一张拆件图，自动切成单块，然后逐个分配部位、点关节。",text);
        GUI.Label(new Rect(r.x+16,r.y+38,r.width-32,40),"把 PNG 放进「打开拆件图文件夹」打开的目录即可出现在这里。透明背景最好；纯色背景也能切。每块之间至少留出几个像素的空隙。",small);
        var sheets=RigLibrary.Sheets();
        if(sheets.Count==0){GUI.Label(new Rect(r.x+16,r.y+90,r.width-32,26),"还没有拆件图。",text);return;}
        Rect view=new Rect(r.x+16,r.y+86,r.width-32,r.height-100);Rect content=new Rect(0,0,view.width-20,Mathf.Ceil(sheets.Count/4f)*60);
        rbSheetScroll=GUI.BeginScrollView(view,rbSheetScroll,content);
        for(int i=0;i<sheets.Count;i++){
            Rect b=new Rect((i%4)*(content.width/4),(i/4)*60,content.width/4-10,50);
            string from=sheets[i].StartsWith(RigLibrary.BuiltinSheets)?"随游戏附带":"玩家目录";
            if(GUI.Button(b,Path.GetFileName(sheets[i])+"\n"+from,chip))RbNewFromSheet(sheets[i]);
        }
        GUI.EndScrollView();
    }
    void RbNewFromSheet(string path){
        try{
            var sheet=RigTextures.LoadFile(path);if(sheet==null){rbStatus="图片读不出来："+path;return;}
            var cuts=RigSheetCutter.Run(sheet);Destroy(sheet);
            if(cuts.Count==0){rbStatus="没切出任何拆件，检查图片是否有透明背景。";return;}
            var rig=new RigBinding{Name=Path.GetFileNameWithoutExtension(path),SourceSheet=Path.GetFileName(path)};
            for(int i=0;i<cuts.Count;i++)rig.Pieces.Add(new RigPiece{Name="拆件"+(i+1).ToString("00"),Tex=cuts[i].Tex,Source=cuts[i].Source});
            rbRig=rig;rbFolder=null;rbName=rig.Name;rbUndo.Clear();rbSel="torso";rbPt="pivot";rbSheets=false;RbRebuild();
            rbStatus="切出 "+cuts.Count+" 块。先在左边选部位，再点下方拆件分配。记得保存。";
        }catch(Exception e){rbStatus="切图失败："+e.Message;}
    }

    // ---------------------------------------------------------------- slots
    void RbSlots(Rect r){
        GUI.Label(new Rect(r.x,r.y,r.width,22),"部位  "+rbRig.RequiredDone+" / "+rbRig.RequiredTotal+" 完成",small);
        Rect view=new Rect(r.x,r.y+24,r.width,r.height-24);
        int rows=RigSlots.All.Length+RigSlots.All.Select(s=>s.Group).Distinct().Count();
        Rect content=new Rect(0,0,r.width-18,rows*27);
        rbSlotScroll=GUI.BeginScrollView(view,rbSlotScroll,content);float y=0;string group="";
        foreach(var s in RigSlots.All){
            if(s.Group!=group){group=s.Group;GUI.Label(new Rect(4,y+4,content.width,20),group,small);y+=27;}
            var piece=rbRig.SlotPiece(s.Id);bool ok=rbRig.Usable(s.Id);
            string mark=piece==null?(s.Optional?"可不用":"未分配"):ok?"完成":rbRig.Complete(s.Id)?"上级未完成":"缺关节";
            if(GUI.Button(new Rect(0,y,content.width,25),"",s.Id==rbSel?selected:chip)){rbSel=s.Id;rbPt=RbNextPoint(s.Id)??"pivot";}
            GUI.Label(new Rect(8,y+3,100,20),s.Label,text);
            var old=GUI.color;GUI.color=ok?new Color(.25f,.55f,.42f):piece==null?new Color(.5f,.45f,.38f):new Color(.75f,.42f,.16f);
            GUI.Label(new Rect(content.width-84,y+4,80,20),mark,new GUIStyle(small){alignment=TextAnchor.MiddleRight});GUI.color=old;
            y+=27;
        }
        GUI.EndScrollView();
    }
    string RbNextPoint(string id){foreach(var p in RigSlots.ById[id].Points)if(!p.Optional&&!rbRig.HasPoint(id,p.Key))return p.Key;return null;}
    static Color RbPointColor(string key)=>key=="pivot"?new Color(.76f,.26f,.18f):key=="tip"?new Color(.24f,.6f,.48f):key=="grip"?new Color(.82f,.6f,.22f):new Color(.32f,.48f,.78f);

    // ---------------------------------------------------------------- point editor
    void RbEditor(Rect r){
        var def=RigSlots.ById[rbSel];var piece=rbRig.SlotPiece(rbSel);
        GUI.Label(new Rect(r.x,r.y,r.width,22),def.Label+(piece!=null?"  ·  "+piece.Name+"  "+piece.Width+"×"+piece.Height:"  ·  还没分配拆件"),small);
        float x=r.x,y=r.y+24;
        foreach(var p in def.Points){
            bool has=rbRig.HasPoint(rbSel,p.Key);string role=p.Key=="pivot"?"起点":p.Key=="tip"?"终点":p.Optional?"可选":"接口";
            string label=p.Label+" "+role+(has?"":" ·未放");float w=22+label.Length*13;
            if(x+w>r.xMax){x=r.x;y+=28;}
            if(GUI.Button(new Rect(x,y,w,25),label,p.Key==rbPt?selected:chip))rbPt=p.Key;
            Fill(new Rect(x+5,y+9,8,8),RbPointColor(p.Key));x+=w+5;
        }
        Rect img=new Rect(r.x,y+32,r.width,r.yMax-(y+32)-60);
        Fill(img,new Color(.93f,.86f,.72f));Fill(new Rect(img.x,img.y,img.width,1),new Color(.3f,.22f,.15f,.4f));
        if(piece?.Tex==null){GUI.Label(new Rect(img.x+20,img.center.y-12,img.width-40,24),"在下方拆件里点一块，分配给「"+def.Label+"」。",new GUIStyle(text){alignment=TextAnchor.MiddleCenter});}
        else RbEditImage(img,piece);
        float by=r.yMax-54;
        if(rbRig.Limits.TryGetValue(rbSel,out var lim)){
            GUI.Label(new Rect(r.x,by,150,22),"转动范围 "+Mathf.RoundToInt(lim.Min)+"° ~ "+Mathf.RoundToInt(lim.Max)+"°",small);
            float a=GUI.HorizontalSlider(new Rect(r.x+150,by+6,150,16),lim.Min,-180,0),b=GUI.HorizontalSlider(new Rect(r.x+310,by+6,150,16),lim.Max,0,180);
            if(Mathf.Abs(a-lim.Min)>.01f||Mathf.Abs(b-lim.Max)>.01f){lim.Min=Mathf.Round(a);lim.Max=Mathf.Round(b);RbRebuild();}
        }else GUI.Label(new Rect(r.x,by,r.width,22),rbSel=="torso"?"躯干是根，不限转动。":"大臂绕肩自由转动。",small);
        if(GUI.Button(new Rect(r.x,by+26,96,26),"取消分配",chip)&&piece!=null){RbChange();rbRig.Slots[rbSel]=new RigSlotBind();RbRebuild();}
        if(GUI.Button(new Rect(r.x+102,by+26,120,26),"清空本块关节",chip)&&piece!=null){RbChange();rbRig.Slots[rbSel].Points.Clear();rbPt="pivot";RbRebuild();}
        GUI.Label(new Rect(r.x+230,by+28,r.width-230,24),"起点接上一级，终点接下一级。手的握点决定兵器握在哪。",small);
    }
    void RbEditImage(Rect img,RigPiece piece){
        float s=Mathf.Min((img.width-40)/piece.Width,(img.height-40)/piece.Height,3f);
        Vector2 o=new Vector2(img.center.x-piece.Width*s/2,img.center.y-piece.Height*s/2);
        Rect pic=new Rect(o.x,o.y,piece.Width*s,piece.Height*s);
        Fill(pic,new Color(.88f,.8f,.64f));GUI.DrawTexture(pic,piece.Tex);
        var pts=rbRig.Slots[rbSel].Points;Func<Vector2,Vector2> toGui=v=>o+v*s;
        if(pts.TryGetValue("pivot",out var pv)){
            foreach(var k in new[]{"tip","grip","shoulderNear","shoulderFar","hipNear","hipFar"})if(pts.TryGetValue(k,out var q))RbLine(toGui(pv),toGui(q),k=="tip"?3:1.5f,new Color(.12f,.08f,.05f,k=="tip"?.8f:.45f));
        }
        foreach(var p in RigSlots.ById[rbSel].Points){
            if(!pts.TryGetValue(p.Key,out var v))continue;Vector2 g=toGui(v);bool act=p.Key==rbPt;float d=act?18:14;
            RbDot(g,d+4,new Color(.1f,.07f,.04f));RbDot(g,d,RbPointColor(p.Key));
            bool left=p.Key=="shoulderFar"||p.Key=="hipFar";
            GUI.Label(new Rect(left?g.x-130:g.x+12,left?g.y+6:g.y-24,118,22),p.Label,new GUIStyle(small){fontStyle=FontStyle.Bold,alignment=left?TextAnchor.MiddleRight:TextAnchor.MiddleLeft,normal={textColor=new Color(.12f,.08f,.05f)}});
        }
        var e=Event.current;Vector2 q2=(e.mousePosition-o)/s;
        if(e.type==EventType.MouseDown&&e.button==0&&img.Contains(e.mousePosition)){
            string hit=null;float best=14/s;
            foreach(var p in RigSlots.ById[rbSel].Points)if(pts.TryGetValue(p.Key,out var v)){float dd=Vector2.Distance(v,q2);if(dd<best){best=dd;hit=p.Key;}}
            RbChange();
            if(hit!=null){rbPt=hit;rbDragPt=hit;}else{pts[rbPt]=RbClampToPiece(q2,piece);rbDragPt=rbPt;}
            RbRebuild();e.Use();
        }else if(e.type==EventType.MouseDrag&&rbDragPt!=null){pts[rbDragPt]=RbClampToPiece(q2,piece);RbRebuild();e.Use();}
        else if(e.type==EventType.MouseUp&&rbDragPt!=null){rbDragPt=null;rbPt=RbNextPoint(rbSel)??rbPt;RbRebuild();e.Use();}
    }
    static Vector2 RbClampToPiece(Vector2 v,RigPiece p)=>new Vector2(Mathf.Clamp(v.x,-40,p.Width+40),Mathf.Clamp(v.y,-40,p.Height+40));

    // ---------------------------------------------------------------- puppet preview
    void RbPreview(Rect r){
        float x=r.x;
        foreach(var m in new[]{(RigPuppet.Mode.Bind,"原样拼合"),(RigPuppet.Mode.Hang,"提线悬挂"),(RigPuppet.Mode.Fall,"落地")}){
            if(GUI.Button(new Rect(x,r.y,96,26),m.Item2,rbMode==m.Item1?selected:chip)){rbMode=m.Item1;rbPuppet?.Reset(rbMode);}x+=100;
        }
        if(GUI.Button(new Rect(x,r.y,60,26),"复位",chip))rbPuppet?.Reset(rbMode);x+=66;
        rbShowJoints=GUI.Toggle(new Rect(x,r.y+3,100,22),rbShowJoints,"显示关节");
        Rect stage=new Rect(r.x,r.y+32,r.width,330);StagePanel(stage);
        if(rbPuppet==null||!rbPuppet.Has("torso")){GUI.Label(new Rect(stage.x,stage.center.y-12,stage.width,24),"先完成躯干的四个关节，木偶才会出现。",new GUIStyle(text){alignment=TextAnchor.MiddleCenter});RbProportions(new Rect(r.x,stage.yMax+6,r.width,r.yMax-stage.yMax-6));return;}
        var e=Event.current;
        if(e.type==EventType.Repaint){rbClock+=Mathf.Min(.1f,Time.unscaledDeltaTime);int steps=0;while(rbClock>=1f/60&&steps<4){rbPuppet.Step(rbMode,rbDragJoint,rbDragTo);rbClock-=1f/60;steps++;}}
        Rect B=rbPuppet.Bounds;float sc=Mathf.Min(stage.width/(B.width*1.5f),stage.height/(B.height*1.25f));
        Vector2 off=new Vector2(stage.width/2-B.center.x*sc,rbMode==RigPuppet.Mode.Fall?stage.height*.86f-rbPuppet.Ground*sc:stage.height*.1f-B.yMin*sc);
        Func<Vector2,Vector2> toLocal=v=>off+v*sc;
        GUI.BeginClip(stage);
        try{
            Vector2 clip=paperDrawOrigin+stage.position;
            if(rbMode==RigPuppet.Mode.Fall){float gy=rbPuppet.Ground*sc+off.y;Fill(new Rect(0,gy,stage.width,stage.height-gy),new Color(.35f,.25f,.15f,.18f));Fill(new Rect(0,gy-1,stage.width,2),new Color(.2f,.13f,.08f,.6f));}
            string pin=rbPuppet.PinJoint;
            if(rbMode==RigPuppet.Mode.Hang&&pin!=null&&rbPuppet.Anchor.HasValue){Vector2 a=toLocal(rbPuppet.Anchor.Value),p=toLocal(rbPuppet.Joint(pin));Fill(new Rect(a.x-1,0,2,a.y),new Color(.2f,.13f,.08f,.7f));RbLine(a,p,2,new Color(.2f,.13f,.08f,.7f),clip);}
            rbPuppet.Draw(rbMode,toLocal,sc,clip,Color.white);
            if(rbShowJoints){
                foreach(var b in rbPuppet.Bones){Vector2 a=rbMode==RigPuppet.Mode.Bind?rbPuppet.BindJoints[b.A]:rbPuppet.Joint(b.A),c=rbMode==RigPuppet.Mode.Bind?rbPuppet.BindJoints[b.B]:rbPuppet.Joint(b.B);RbLine(toLocal(a),toLocal(c),2,new Color(.1f,.07f,.04f,.55f),clip);}
                foreach(var j in rbPuppet.BindJoints.Keys){Vector2 p=toLocal(rbMode==RigPuppet.Mode.Bind?rbPuppet.BindJoints[j]:rbPuppet.Joint(j));string key=j.Substring(j.IndexOf('.')+1);RbDot(p,9,new Color(.1f,.07f,.04f));RbDot(p,7,RbPointColor(key=="pivot"||key=="tip"?key:"x"));}
            }
            if(rbMode!=RigPuppet.Mode.Bind){
                Vector2 local=e.mousePosition;Vector2 art=(local-off)/sc;
                if(e.type==EventType.MouseDown&&e.button==0&&new Rect(0,0,stage.width,stage.height).Contains(local)){rbDragJoint=rbPuppet.Nearest(art);rbDragTo=art;e.Use();}
                else if(e.type==EventType.MouseDrag&&rbDragJoint!=null){rbDragTo=art;e.Use();}
                else if(e.type==EventType.MouseUp&&rbDragJoint!=null){rbDragJoint=null;e.Use();}
            }
        }finally{GUI.EndClip();}
        RbProportions(new Rect(r.x,stage.yMax+6,r.width,r.yMax-stage.yMax-6));
    }
    // 与切磋骨架比较：用最小二乘求一个统一比例，列出各骨长差多少。差太多的地方在切磋里会露缝或重叠过多。
    void RbProportions(Rect r){
        var ids=RbDuelLengths.Keys.Where(id=>rbRig.Usable(id)).ToList();
        if(ids.Count<3){GUI.Label(new Rect(r.x,r.y,r.width,22),"完成躯干和四肢后，这里会比较原画与切磋骨架的比例。",small);return;}
        double num=0,den=0;foreach(var id in ids){double a=rbRig.BoneLength(id);num+=a*RbDuelLengths[id];den+=a*a;}
        float k=(float)(num/den);
        GUI.Label(new Rect(r.x,r.y,r.width,20),"与切磋骨架比例（1 米 ≈ "+Mathf.RoundToInt(1/k)+" 像素）。差 15% 以上标红，切磋里会露缝或叠得太多。",small);
        int i=0;
        foreach(var id in ids){
            float diff=(rbRig.BoneLength(id)*k/RbDuelLengths[id]-1)*100;
            var old=GUI.color;GUI.color=Mathf.Abs(diff)>15?new Color(.72f,.2f,.12f):new Color(.3f,.24f,.17f);
            GUI.Label(new Rect(r.x+(i%3)*(r.width/3),r.y+22+(i/3)*20,r.width/3,20),RigSlots.ById[id].Label+" "+(diff>=0?"+":"")+Mathf.RoundToInt(diff)+"%",small);
            GUI.color=old;i++;
        }
    }

    // ---------------------------------------------------------------- tray and order
    void RbTray(Rect r){
        GUI.Label(new Rect(r.x,r.y,r.width,22),"拆件 "+rbRig.Pieces.Count+" 块 · 先在左边选部位，再点这里的拆件分配。",small);
        var owner=new Dictionary<string,string>();foreach(var s in RigSlots.All){var p=rbRig.Slots[s.Id].Piece;if(!string.IsNullOrEmpty(p))owner[p]=s.Label;}
        Rect view=new Rect(r.x,r.y+24,r.width,r.height-24);const float cell=92;
        Rect content=new Rect(0,0,Mathf.Max(view.width,rbRig.Pieces.Count*cell),view.height-18);
        rbTrayScroll=GUI.BeginScrollView(view,rbTrayScroll,content);
        for(int i=0;i<rbRig.Pieces.Count;i++){
            var p=rbRig.Pieces[i];Rect c=new Rect(i*cell,0,cell-6,cell+30);bool mine=rbRig.Slots[rbSel].Piece==p.Name;
            Fill(c,mine?new Color(.66f,.24f,.16f,.55f):new Color(.9f,.82f,.66f));
            if(p.Tex!=null)GUI.DrawTexture(new Rect(c.x+4,c.y+4,c.width-8,cell-8),p.Tex,ScaleMode.ScaleToFit);
            GUI.Label(new Rect(c.x,c.y+cell-4,c.width,24),owner.TryGetValue(p.Name,out var o)?o:p.Name,centerSmall);
            if(GUI.Button(c,"",GUIStyle.none))RbAssign(p.Name);
        }
        GUI.EndScrollView();
    }
    void RbAssign(string piece){
        RbChange();
        foreach(var s in RigSlots.All)if(s.Id!=rbSel&&rbRig.Slots[s.Id].Piece==piece)rbRig.Slots[s.Id]=new RigSlotBind();
        if(rbRig.Slots[rbSel].Piece!=piece){rbRig.Slots[rbSel]=new RigSlotBind{Piece=piece};rbPt="pivot";}
        RbRebuild();rbStatus="「"+piece+"」分配给了"+RigSlots.ById[rbSel].Label+"，在中间图上点「"+RigSlots.ById[rbSel].Points[0].Label+"」。";
    }
    void RbOrder(Rect r){
        GUI.Label(new Rect(r.x,r.y,r.width,22),"绘制顺序 · 上面的先画，在最后面",small);
        Rect view=new Rect(r.x,r.y+24,r.width,r.height-24);Rect content=new Rect(0,0,r.width-18,rbRig.Order.Count*24);
        rbOrderScroll=GUI.BeginScrollView(view,rbOrderScroll,content);
        for(int i=0;i<rbRig.Order.Count;i++){
            string id=rbRig.Order[i];bool ok=rbRig.Usable(id);var old=GUI.color;if(!ok)GUI.color=new Color(1,1,1,.5f);
            GUI.Label(new Rect(4,i*24+2,200,22),RigSlots.ById[id].Label+(ok?"":"（未完成）"),small);GUI.color=old;
            if(i>0&&GUI.Button(new Rect(content.width-64,i*24,30,22),"↑",chip)){RbChange();var t=rbRig.Order[i-1];rbRig.Order[i-1]=id;rbRig.Order[i]=t;}
            if(i<rbRig.Order.Count-1&&GUI.Button(new Rect(content.width-32,i*24,30,22),"↓",chip)){RbChange();var t=rbRig.Order[i+1];rbRig.Order[i+1]=id;rbRig.Order[i]=t;}
        }
        GUI.EndScrollView();
    }

    // ---------------------------------------------------------------- small drawing helpers
    static Texture2D RbDiscTexture(){
        if(rbDisc!=null)return rbDisc;
        rbDisc=new Texture2D(32,32,TextureFormat.RGBA32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        var px=new Color[32*32];for(int y=0;y<32;y++)for(int x=0;x<32;x++){float d=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(16,16));px[y*32+x]=new Color(1,1,1,Mathf.Clamp01(15.5f-d));}
        rbDisc.SetPixels(px);rbDisc.Apply();return rbDisc;
    }
    void RbDot(Vector2 at,float size,Color c){var old=GUI.color;GUI.color=c;GUI.DrawTexture(new Rect(at.x-size/2,at.y-size/2,size,size),RbDiscTexture());GUI.color=old;}
    void RbLine(Vector2 a,Vector2 b,float width,Color c)=>RbLine(a,b,width,c,paperDrawOrigin);
    void RbLine(Vector2 a,Vector2 b,float width,Color c,Vector2 clipOrigin){
        Vector2 d=b-a;float len=d.magnitude;if(len<.5f)return;
        var old=GUI.matrix;var oldColor=GUI.color;
        try{Vector3 u=a+clipOrigin;GUI.matrix=old*Matrix4x4.Translate(u)*Matrix4x4.Rotate(Quaternion.Euler(0,0,Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg))*Matrix4x4.Translate(-u);
            GUI.color=c;GUI.DrawTexture(new Rect(a.x,a.y-width/2,len,width),Texture2D.whiteTexture);}
        finally{GUI.matrix=old;GUI.color=oldColor;}
    }
}
}
