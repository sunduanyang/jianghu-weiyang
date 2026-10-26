using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    J paperCast;
    readonly Dictionary<string,Texture2D> paperAtlases=new Dictionary<string,Texture2D>();
    readonly Dictionary<string,List<J>> paperLayers=new Dictionary<string,List<J>>();
    readonly Dictionary<string,PaperAnimator> paperActors=new Dictionary<string,PaperAnimator>();
    string paperPreviewSkin="m-youth-farmer",paperPreviewClip="idle";
    bool paperPreviewLeft,paperPreviewBones,paperPreviewGallery,paperPreviewAnimate=true;
    float paperPreviewPhase=.5f;
    Vector2 paperSkinScroll;
    float paperBattleStart;
    string paperBattleKey="";
    double paperGuiPivotDrift;
    int paperGuiPivotSamples;
    double paperGuiEndpointDrift;
    int paperGuiEndpointSamples;
    readonly bool paperMeasureGui=GameClient.Arg("-smokeDir").Length>0||GameClient.Arg("-paperRegressionDir").Length>0;
    sealed class PaperFigureDraw {
        public Rect PixelViewport;
        public string Skin,Clip;
        public float Phase;
        public bool Left,Bones;
        public PaperPose Pose;
    }
    readonly List<PaperFigureDraw> paperDraws=new List<PaperFigureDraw>();
    Vector2 paperDrawOrigin,paperPixelOrigin;
    J PaperCast {get{if(paperCast==null){var textAsset=Resources.Load<TextAsset>("PaperCast/index");if(textAsset==null)throw new InvalidOperationException("Missing PaperCast/index; run python unity/bake_paper_cast.py");paperCast=J.Parse(textAsset.text);}return paperCast;}}
    Texture2D PaperAtlas(string id){if(!paperAtlases.TryGetValue(id,out var atlas)){atlas=Resources.Load<Texture2D>("PaperCast/"+id);if(atlas==null)throw new InvalidOperationException("Missing paper puppet atlas: "+id);paperAtlases[id]=atlas;}return atlas;}

    public string PaperSkinFor(J person,bool player=false){
        string sex=person["sex"].Has?person["sex"].S:"男",prefix=sex=="女"?"f-":"m-";
        int age=person["age_months"].Has?person["age_months"].I/12:18;
        if(age<16)return prefix+"child";
        if(age>=60)return prefix+"elder";
        string job=person["job"].S,identity=person["identity"]["title"].S,faction="";
        if(person["faction"].Has)faction=W["factions"].A.FirstOrDefault(f=>f["id"].I==person["faction"].I)?["name"].S??"";
        if(prefix=="m-"&&(faction.Contains("少林")||faction.Contains("僧")))return "m-monk";
        if(prefix=="m-"&&(faction.Contains("武当")||faction.Contains("全真")||faction.Contains("道")))return "m-daoist";
        if(prefix=="m-"&&faction.Contains("丐"))return "m-beggar";
        if(identity.Contains("县令")||identity.Contains("长官")||identity.Contains("太守")||identity.Contains("官员"))return prefix=="m-"?"m-official":"f-scholar";
        if(job.Contains("商"))return prefix+"merchant";
        if(job.Contains("书")||job.Contains("学"))return prefix+"scholar";
        if(job.Contains("镖"))return prefix+"escort";
        if(job.Contains("药")||job.Contains("医"))return prefix=="f-"?"f-healer":"m-scholar";
        if(prefix=="m-"){
            if(job.Contains("渔"))return "m-fisher";if(job.Contains("猎"))return "m-hunter";
            if(job.Contains("匠"))return "m-artisan";if(job.Contains("艺"))return "m-performer";
        }
        bool martial=new[]{"派","剑","刀","武","镖","拳","寺","帮","宗","门"}.Any(word=>faction.Contains(word));
        if(player||martial)return prefix=="f-"?"f-swordswoman":"m-escort";
        return prefix+(age>=35?"middle-farmer":"youth-farmer");
    }
    J PaperPerson(int id,string sex,int months){if(id<0)return Hero["created"].B?Hero:J.O("sex",sex,"age_months",months);var p=Npc(id);return p.Has?p:J.O("sex",sex,"age_months",months);}
    static Rect PaperUV(J part)=>new Rect((float)part["uv"][0].N,(float)part["uv"][1].N,(float)part["uv"][2].N,(float)part["uv"][3].N);
    void DrawPaperPortrait(Rect r,J person,bool player=false,bool deceased=false){
        string id=PaperSkinFor(person,player);J part=PaperCast["skins"][id]["parts"]["head-neck"];
        Fill(r,new Color(.15f,.17f,.16f,.72f));
        float aspect=(float)(part["size"][0].N/part["size"][1].N),h=Mathf.Min(r.height,r.width/aspect),w=h*aspect;
        Rect target=new Rect(r.center.x-w/2,r.center.y-h/2,w,h);var old=GUI.color;
        if(deceased)GUI.color=new Color(.64f,.63f,.59f,1);
        GUI.DrawTextureWithTexCoords(target,PaperAtlas(id),PaperUV(part));GUI.color=old;
    }

    void PaperPart(Texture2D atlas,J part,Vector2 pivot,float angle,float size,bool left,Vector2? endpoint){
        Vector2 source=PaperRig.Point(part["pivot"]),dimensions=PaperRig.Point(part["size"]);
        Matrix4x4 old=GUI.matrix;Rect uv=PaperUV(part);
        Vector2 tip=endpoint.HasValue?PaperRig.Point(part["tip"]):Vector2.zero;
        Vector2 expected=paperMeasureGui&&endpoint.HasValue?(Vector2)old.MultiplyPoint3x4(endpoint.Value+paperPixelOrigin):Vector2.zero;
        if(part["mirror"].B){source.x=dimensions.x-source.x;tip.x=dimensions.x-tip.x;uv.x+=uv.width;uv.width=-uv.width;}
        if(left){source.x=dimensions.x-source.x;tip.x=dimensions.x-tip.x;uv.x+=uv.width;uv.width=-uv.width;angle=-angle;}
        try{
            // Unity's pivot helper preserves nested GUI clipping offsets. Scaling
            // is applied to coordinates, so the same renderer also works in areas.
            PaperRotateAroundPivot(angle,pivot);
            float breadth=part["breadth"].Has?(float)part["breadth"].N:1,shear=(float)part["shear"].N*(left?-1:1);
            if(Mathf.Abs(breadth-1)>.00001f||Mathf.Abs(shear)>.00001f){
                Vector2 e=endpoint.HasValue?(tip-source).normalized:Vector2.up,q=new Vector2(e.y,-e.x),center=pivot+paperPixelOrigin;
                Matrix4x4 bind=Matrix4x4.identity;
                bind.m00=e.x*e.x+breadth*q.x*q.x+shear*e.x*q.x;bind.m01=e.x*e.y+breadth*q.x*q.y+shear*e.x*q.y;
                bind.m10=e.y*e.x+breadth*q.y*q.x+shear*e.y*q.x;bind.m11=e.y*e.y+breadth*q.y*q.y+shear*e.y*q.y;
                GUI.matrix=GUI.matrix*Matrix4x4.Translate(center)*bind*Matrix4x4.Translate(-center);
            }
            if(paperMeasureGui&&endpoint.HasValue){Vector2 actual=GUI.matrix.MultiplyPoint3x4(pivot+(tip-source)*size+paperPixelOrigin);paperGuiEndpointDrift=Math.Max(paperGuiEndpointDrift,Vector2.Distance(expected,actual));paperGuiEndpointSamples++;}
            GUI.DrawTextureWithTexCoords(new Rect(pivot.x-source.x*size,pivot.y-source.y*size,dimensions.x*size,dimensions.y*size),atlas,uv);
        }finally{GUI.matrix=old;}
    }
    void PaperRotateAroundPivot(float angle,Vector2 pivot){
        Vector2 unclipped=pivot+paperPixelOrigin;
        Vector2 before=paperMeasureGui?(Vector2)GUI.matrix.MultiplyPoint3x4(unclipped):Vector2.zero;
        // The dedicated paper pass has unit GUI scale. Native rotation and its
        // clipping offsets now operate in the same pixel coordinate system.
        GUIUtility.RotateAroundPivot(angle,pivot);
        if(paperMeasureGui){paperGuiPivotDrift=Math.Max(paperGuiPivotDrift,Vector2.Distance(before,GUI.matrix.MultiplyPoint3x4(unclipped)));paperGuiPivotSamples++;}
    }
    static Vector2 PaperScreenPoint(Vector2 origin,float fit,bool left,Vector2 point)=>origin+new Vector2((left?-1:1)*fit*point.x,fit*point.y);
    public void DrawPaperFigure(Rect r,string id,string clip,float phase,bool left=false,bool bones=false,PaperPose sampled=null){
        if(Event.current.type!=EventType.Repaint)return;
        // The virtual canvas has one explicit area origin and one root matrix.
        // Project once here; do not mix GUIClip's window and screen conversions.
        Vector2 a=GUI.matrix.MultiplyPoint3x4(r.min+paperDrawOrigin),b=GUI.matrix.MultiplyPoint3x4(r.max+paperDrawOrigin);
        paperDraws.Add(new PaperFigureDraw{PixelViewport=Rect.MinMaxRect(a.x,a.y,b.x,b.y),Skin=id,Clip=clip,Phase=phase,Left=left,Bones=bones,Pose=sampled});
    }
    void FlushPaperFigures(){
        if(Event.current.type!=EventType.Repaint||paperDraws.Count==0)return;
        Matrix4x4 original=GUI.matrix;Color color=GUI.color;
        try{
            GUI.matrix=Matrix4x4.identity;
            foreach(var draw in paperDraws){
                Rect viewport=draw.PixelViewport;paperPixelOrigin=viewport.position;
                GUI.BeginGroup(viewport);
                try{RenderPaperFigure(new Rect(0,0,viewport.width,viewport.height),draw.Skin,draw.Clip,draw.Phase,draw.Left,draw.Bones,draw.Pose);}
                finally{GUI.EndGroup();}
            }
        }finally{GUI.matrix=original;GUI.color=color;paperDraws.Clear();}
    }
    void RenderPaperFigure(Rect r,string id,string clip,float phase,bool left,bool bones,PaperPose sampled){
        var skin=PaperCast["skins"][id];if(!skin.Has)throw new InvalidOperationException("Unknown paper skin: "+id);
        var rig=PaperCast["rig"];var pose=sampled??PaperRig.Sample(rig,skin,clip,phase);var p=pose.Joints;
        if(!paperLayers.TryGetValue(id,out var layers)){layers=skin["parts"].D.Values.OrderBy(v=>v["layer"].N).ToList();paperLayers[id]=layers;}
        float ground=PaperRig.Ground(rig,skin),frame=ground+PaperRig.Length(rig,skin,"spine")+(float)skin["parts"]["head-neck"]["height"].N+80;
        float fit=Mathf.Min(r.width/430f,r.height/frame)*(float)skin["scale"].N;
        Vector2 screenOrigin=new Vector2(r.center.x,r.yMax-24*fit-fit*ground);
        if(skin["canvas"].Has){Vector2 canvas=PaperRig.Point(skin["canvas"]),anchor=PaperRig.Point(skin["canvas_origin"]);fit=Mathf.Min(r.width/canvas.x,r.height/canvas.y);screenOrigin=r.center+new Vector2((left?-1:1)*(anchor.x-canvas.x/2)*fit,(anchor.y-canvas.y/2)*fit);}
        Matrix4x4 old=GUI.matrix;Color color=GUI.color;
        try{
            GUI.color=Color.white;var atlas=PaperAtlas(id);
            foreach(var part in layers){
                Vector2 pivot;float angle,scale;Vector2? endpoint=null;
                if(part["bone"].Has){
                    var bone=rig["bones"][part["bone"].S];Vector2 a=p[bone[0].S],b=p[bone[1].S],delta=b-a;
                    Vector2 local=PaperRig.Point(part["tip"])-PaperRig.Point(part["pivot"]);
                    if(part["mirror"].B)local.x=-local.x;
                    pivot=a;angle=Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg-Mathf.Atan2(local.y,local.x)*Mathf.Rad2Deg;scale=delta.magnitude/local.magnitude;
                    endpoint=PaperScreenPoint(screenOrigin,fit,left,b);
                }else{
                    pivot=p[part["joint"].S];angle=(float)part["angle"].N;
                    string follow=part["follow"].S;
                    string attachment=part==skin["parts"]["head-neck"]?"head":part==skin["parts"]["front-skirt"]?"skirtNear":part==skin["parts"]["back-skirt"]?"skirtFar":part==skin["parts"]["near-hand"]?"handNear":part==skin["parts"]["far-hand"]?"handFar":part==skin["parts"]["near-foot"]?"footNear":part==skin["parts"]["far-foot"]?"footFar":follow;
                    if(pose.Angles.TryGetValue(attachment,out float arm))angle+=arm;
                    else if(part["joint"].S=="neck"||part["joint"].S=="hip")angle+=pose.Angles["body"];
                    scale=part["width"].Has?(float)(part["width"].N/part["size"][0].N):(float)(part["height"].N/part["size"][1].N);
                    if(part==skin["parts"]["head-neck"])scale=(float)part["height"].N/Mathf.Max(1,PaperRig.Point(part["pivot"]).y);
                }
                PaperPart(atlas,part,PaperScreenPoint(screenOrigin,fit,left,pivot),angle,scale*fit,left,endpoint);
            }
            if(bones&&Event.current.type==EventType.Repaint){
                foreach(var bone in rig["bones"].D.Values){Vector2 a=PaperScreenPoint(screenOrigin,fit,left,p[bone[0].S]),b=PaperScreenPoint(screenOrigin,fit,left,p[bone[1].S]);Matrix4x4 m=GUI.matrix;PaperRotateAroundPivot(Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg,a);Fill(new Rect(a.x,a.y-1,(b-a).magnitude,2),new Color(.2f,.8f,.9f,.85f));GUI.matrix=m;}
                foreach(var joint in p.Values){var point=PaperScreenPoint(screenOrigin,fit,left,joint);Fill(new Rect(point.x-3,point.y-3,6,6),new Color(.9f,.2f,.15f,1));}
            }
        }finally{GUI.matrix=old;GUI.color=color;}
    }
    void DrawPaperPerson(Rect r,J person,bool player,string clip="idle",bool left=false,float phase=-1){
        string id=PaperSkinFor(person,player);float now=Time.realtimeSinceStartup;
        if(phase<0)phase=PaperRig.Phase(PaperCast["rig"],clip,now);
        string key=W["database"].S+":"+(player?"player":person["id"].S);
        if(!paperActors.TryGetValue(key,out var animator)){if(paperActors.Count>2048)paperActors.Clear();animator=new PaperAnimator();paperActors[key]=animator;}
        DrawPaperFigure(r,id,clip,phase,left,false,animator.Sample(PaperCast["rig"],PaperCast["skins"][id],clip,phase,now));
    }

    string PaperBattleAnimation(J battle,int side,float elapsed,out float phase){
        var rig=PaperCast["rig"];string clip="idle";float local=elapsed;
        if(battle["status"].S=="finished"){
            if(battle["winner"].Has&&battle["winner"].I!=side)clip="defeated";
        }else if(battle["log"].A.Count>0&&elapsed<1.8f){
            int round=battle["log"].A.Last()["round"].I;
            int first=battle["log"].A.First(r=>r["round"].I==round)["side"].I;
            int actor=elapsed<.9f?first:1-first;local=elapsed-(elapsed<.9f?0:.9f);
            var row=battle["log"].A.LastOrDefault(r=>r["round"].I==round&&r["side"].I==actor);
            if(row!=null){
                if(side==actor){string action=row["action"].S;clip=row["outcome"].S=="windup"||action=="guard"||action=="parry"||action=="continue"?"guard":action=="advance"||action=="retreat"?"walk":action=="breathe"||action=="wait"||action=="cancel"||action.StartsWith("mode_")?"idle":action=="surrender"?"defeated":row["outcome"].S=="interrupted"?"hurt":"attack";}
                else if(row["damage"].N>0)clip="hurt";
                else if(battle["fighters"][side]["guarding"].B)clip="guard";
            }
        }else if(battle["fighters"][side]["guarding"].B)clip="guard";
        phase=PaperRig.Phase(rig,clip,local);return clip;
    }

    void DrawPuppets(){
        GUILayout.BeginHorizontal();H("皮影衣橱");GUILayout.FlexibleSpace();if(Btn(paperPreviewGallery?"返回单人":"全部皮肤"))paperPreviewGallery=!paperPreviewGallery;GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();T("铆钉定轴 · 双腿逆运动学 · 七种通用动作",small);GUILayout.FlexibleSpace();if(GUILayout.Button(paperReferenceView?"返回衣橱":"参考素材演示",chip))paperReferenceView=!paperReferenceView;GUILayout.EndHorizontal();
        if(paperReferenceView&&!paperPreviewGallery){DrawPaperReference();return;}
        if(paperPreviewGallery){
            var skins=PaperCast["skins"].D.ToArray();Rect gallery=GUILayoutUtility.GetRect(1200,620,GUILayout.ExpandWidth(true));StagePanel(gallery);gallery=new Rect(gallery.x+16,gallery.y+16,gallery.width-32,gallery.height-32);
            int rows=Mathf.CeilToInt(skins.Length/6f);
            for(int i=0;i<skins.Length;i++){
                Rect cell=new Rect(gallery.x+(i%6)*gallery.width/6,gallery.y+(i/6)*gallery.height/rows,gallery.width/6,gallery.height/rows);
                DrawPaperFigure(new Rect(cell.x,cell.y,cell.width,cell.height-19),skins[i].Key,paperPreviewClip,.55f,paperPreviewLeft,paperPreviewBones);
                string label=skins[i].Value["description"].S.Split('，')[0];GUI.Label(new Rect(cell.x,cell.yMax-20,cell.width,20),label,new GUIStyle(centerSmall){normal={textColor=new Color(.23f,.16f,.09f)}});
            }
            return;
        }
        GUILayout.BeginHorizontal();GUILayout.BeginVertical(card,GUILayout.Width(250));
        paperSkinScroll=GUILayout.BeginScrollView(paperSkinScroll,GUILayout.Height(470));
        foreach(var pair in PaperCast["skins"].D){string label=pair.Value["description"].S;int comma=label.IndexOf('，');if(comma>0)label=label.Substring(0,comma);if(GUILayout.Button(label,paperPreviewSkin==pair.Key?selected:chip)){paperPreviewSkin=pair.Key;}}
        GUILayout.EndScrollView();GUILayout.EndVertical();GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();foreach(var pair in PaperCast["rig"]["clips"].D)if(GUILayout.Button(pair.Value["label"].S,paperPreviewClip==pair.Key?selected:chip))paperPreviewClip=pair.Key;GUILayout.EndHorizontal();
        Rect r=GUILayoutUtility.GetRect(660,480,GUILayout.ExpandWidth(true));StagePanel(r);
        float phase=paperPreviewAnimate?PaperRig.Phase(PaperCast["rig"],paperPreviewClip,Time.realtimeSinceStartup,true):paperPreviewPhase;
        DrawPaperFigure(r,paperPreviewSkin,paperPreviewClip,phase,paperPreviewLeft,paperPreviewBones);
        GUILayout.BeginHorizontal();paperPreviewAnimate=GUILayout.Toggle(paperPreviewAnimate,"播放动作");paperPreviewLeft=GUILayout.Toggle(paperPreviewLeft,"面朝左");paperPreviewBones=GUILayout.Toggle(paperPreviewBones,"显示骨骼与关节");GUILayout.EndHorizontal();
        float scrub=GUILayout.HorizontalSlider(paperPreviewPhase,0,1);if(Mathf.Abs(scrub-paperPreviewPhase)>.0001f){paperPreviewPhase=scrub;paperPreviewAnimate=false;}T(PaperCast["skins"][paperPreviewSkin]["description"].S,small);
        GUILayout.EndVertical();GUILayout.EndHorizontal();
    }

    public bool ValidatePaperCast(out J facts){
        double error=0,transitionJump=0,groundError=0,loopError=0,supportError=0,reverseKnee=0,referenceError=0;int samples=0,parts=0;
        foreach(var pair in PaperCast["skins"].D){var skin=pair.Value;PaperAtlas(pair.Key);parts+=skin["parts"].D.Count;
            var animator=new PaperAnimator();var before=animator.Sample(PaperCast["rig"],skin,"idle",.4f,0);var switched=animator.Sample(PaperCast["rig"],skin,"attack",.55f,.1f);
            foreach(var joint in before.Joints)transitionJump=Math.Max(transitionJump,Vector2.Distance(joint.Value,switched.Joints[joint.Key]));
            foreach(string clip in PaperCast["rig"]["clips"].D.Keys)for(int step=0;step<=100;step++){
                var parameters=PaperRig.Parameters(PaperCast["rig"],clip,step/100f);var pose=PaperRig.Evaluate(PaperCast["rig"],skin,parameters);
                foreach(var bone in PaperCast["rig"]["bones"].D){string key=bone.Key.StartsWith("upper")?"upper_arm":bone.Key.StartsWith("forearm")?"forearm":bone.Key.StartsWith("thigh")?"thigh":bone.Key.StartsWith("shin")?"shin":"spine",side=bone.Key.EndsWith("Near")?"Near":"Far";double length=Vector2.Distance(pose.Joints[bone.Value[0].S],pose.Joints[bone.Value[1].S]);error=Math.Max(error,Math.Abs(length-(key=="spine"?PaperRig.Length(PaperCast["rig"],skin,key):PaperRig.Segment(PaperCast["rig"],skin,key,side))));}
                foreach(string side in new[]{"Near","Far"})if(Mathf.Abs(pose.Angles["foot"+side])<.001f){float sole=pose.Joints["ankle"+side].y+PaperRig.Sole(skin,side);groundError=Math.Max(groundError,Math.Max(0,sole-PaperRig.Ground(PaperCast["rig"],skin)));}
                foreach(string side in new[]{"Near","Far"}){
                    string prefix=side=="Near"?"near":"far";
                    if(parameters[prefix+"_foot_lift"]<.001f&&Mathf.Abs(pose.Angles["foot"+side])<.001f)supportError=Math.Max(supportError,Vector2.Distance(pose.Joints["ankle"+side],PaperRig.FootGoal(PaperCast["rig"],skin,parameters,side)));
                    Vector2 axis=(pose.Joints["ankle"+side]-pose.Joints["hip"+side]).normalized;
                    float pole=skin["knee_poles"][side].Has?(float)skin["knee_poles"][side].N:1;reverseKnee=Math.Max(reverseKnee,Math.Max(0,-Vector2.Dot(pose.Joints["knee"+side]-pose.Joints["hip"+side],new Vector2(axis.y,-axis.x))*pole));
                }
                samples++;
            }
            foreach(var clip in PaperCast["rig"]["clips"].D)if(clip.Value["loop"].B){var start=PaperRig.Sample(PaperCast["rig"],skin,clip.Key,0);var end=PaperRig.Sample(PaperCast["rig"],skin,clip.Key,1);foreach(var joint in start.Joints)loopError=Math.Max(loopError,Vector2.Distance(joint.Value,end.Joints[joint.Key]));}
            if(skin["reference_landmarks"].Has){var pose=PaperRig.Sample(PaperCast["rig"],skin,"idle",0);foreach(var joint in skin["reference_landmarks"].D)referenceError=Math.Max(referenceError,Vector2.Distance(pose.Joints[joint.Key],PaperRig.Point(joint.Value)));}
        }
        bool mapping=PaperSkinFor(J.O("sex","女","age_months",240,"job","行商"))=="f-merchant"&&PaperSkinFor(J.O("sex","男","age_months",96,"job","行商"))=="m-child"&&PaperSkinFor(J.O("sex","女","age_months",840,"job","镖师"))=="f-elder";
        facts=J.O("skins",PaperCast["skins"].D.Count,"parts",parts,"clips",PaperCast["rig"]["clips"].D.Count,"pose_samples",samples,"max_bone_length_error",error,"transition_joint_jump",transitionJump,"max_ground_penetration",groundError,"max_support_foot_gap",supportError,"max_reverse_knee",reverseKnee,"max_loop_seam",loopError,"reference_landmark_error_pixels",referenceError,"age_sex_profession_controls",mapping);
        return PaperCast["skins"].D.Count==PaperCast["expected_skins"].I&&parts==PaperCast["expected_parts"].I&&error<.001&&transitionJump<.001&&groundError<.001&&supportError<.001&&reverseKnee<.001&&loopError<.001&&referenceError<.1&&mapping;
    }
    public void PaperSmokePerson(){personScreen=true;dialogueOpen=false;}
    public void PaperSmokeDialogue(){personScreen=false;dialogueOpen=true;dialogueText="同一身皮影，从此处相逢，也到演武场见招。";}
    public void PaperSmokeStage(){personScreen=false;dialogueOpen=false;}
    public void PaperSmokeGallery(string clip,bool left){paperPreviewGallery=true;paperPreviewClip=clip;paperPreviewLeft=left;}
    public void PaperSmokeStudio(){paperPreviewGallery=false;paperPreviewClip="walk";paperPreviewLeft=false;paperPreviewBones=true;}
    public void PaperSmokePose(string skin,string clip,bool left,bool bones,float phase){paperReferenceView=false;paperPreviewGallery=false;paperPreviewSkin=skin;paperPreviewClip=clip;paperPreviewLeft=left;paperPreviewBones=bones;paperPreviewAnimate=false;paperPreviewPhase=phase;}
    public void PaperSmokePlay(bool playing){paperPreviewAnimate=playing;}
    public void ResetPaperGuiProbe(){paperGuiPivotDrift=0;paperGuiPivotSamples=0;paperGuiEndpointDrift=0;paperGuiEndpointSamples=0;}
    public bool PaperGuiProbe(out J facts){facts=J.O("width",Screen.width,"height",Screen.height,"pivot_samples",paperGuiPivotSamples,"maximum_pivot_drift_pixels",paperGuiPivotDrift,"endpoint_samples",paperGuiEndpointSamples,"maximum_attachment_error_pixels",paperGuiEndpointDrift);return paperGuiPivotSamples>0&&paperGuiEndpointSamples>0&&paperGuiPivotDrift<.5&&paperGuiEndpointDrift<.5;}
}
}
