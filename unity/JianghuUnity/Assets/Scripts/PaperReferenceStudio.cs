using System;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    bool paperReferenceView,paperDemoAuto=true,paperDemoBody,paperDemoBones=true,paperDemoOverlay;
    float paperDemoClock,paperDemoSpeed=1;
    int paperDemoIndex;
    string paperReferenceSkin=GameClient.Arg("-paperSkin","reference-jade");
    readonly string[] paperDemoClips={"idle","walk","talk","attack","guard","hurt","defeated"};
    void UpdatePaperReference(){
        if(Page!=5||!paperReferenceView)return;
        if(Input.GetKeyDown(KeyCode.Space))paperPreviewAnimate=!paperPreviewAnimate;
        if(Input.GetKeyDown(KeyCode.B))paperDemoBones=!paperDemoBones;
        if(Input.GetKeyDown(KeyCode.L))paperPreviewLeft=!paperPreviewLeft;
        for(int i=0;i<7;i++)if(Input.GetKeyDown(KeyCode.Alpha1+i))SelectPaperDemo(i);
        if(!paperPreviewAnimate)return;
        paperDemoClock+=Time.unscaledDeltaTime*paperDemoSpeed;
        if(paperDemoAuto){float duration=(float)PaperCast["rig"]["clips"][paperDemoClips[paperDemoIndex]]["seconds"].N;
            if(paperDemoClock>duration+1.2f){paperDemoClock=0;paperDemoIndex=(paperDemoIndex+1)%paperDemoClips.Length;}}
    }
    void SelectPaperDemo(int i){paperDemoIndex=i;paperDemoClock=0;paperDemoAuto=false;paperPreviewAnimate=true;}
    void DrawPaperReference(){
        GUILayout.BeginHorizontal();if(GUILayout.Button("青玉女侠",chip))paperReferenceSkin="reference-jade";if(GUILayout.Button("竹纹剑客",chip)){paperReferenceSkin="reference-bamboo";paperPreviewLeft=true;paperDemoOverlay=false;paperDemoBody=false;}if(GUILayout.Button("素发女侠 · 十五件",chip)){PaperReferenceSkin("reference-plain");PaperSmokeReference("idle",0);paperDemoOverlay=true;}GUILayout.FlexibleSpace();GUILayout.EndHorizontal();
        var currentSkin=PaperCast["skins"][paperReferenceSkin];bool trial=currentSkin["source_sheet"].Has,hasAssembly=!trial||currentSkin["reference_image"].Has;
        string referenceImage=currentSkin["reference_image"].Has?currentSkin["reference_image"].S:trial?currentSkin["source_sheet"].S:"reference-assembled";
        GUILayout.Space(10);GUILayout.BeginHorizontal();
        for(int i=0;i<paperDemoClips.Length;i++){string name=PaperCast["rig"]["clips"][paperDemoClips[i]]["label"].S;if(GUILayout.Button((i+1)+"  "+name,i==paperDemoIndex?selected:chip))SelectPaperDemo(i);}
        GUILayout.EndHorizontal();
        string clip=paperDemoClips[paperDemoIndex];
        float phase=paperPreviewAnimate?PaperRig.Phase(PaperCast["rig"],clip,paperDemoClock):paperPreviewPhase;
        Rect stage=GUILayoutUtility.GetRect(1200,510,GUILayout.ExpandWidth(true));
        float width=(stage.width-24)/3;
        for(int i=0;i<3;i++){
            Rect panel=new Rect(stage.x+i*(width+12),stage.y,width,stage.height);StagePanel(panel);
            GUI.Label(new Rect(panel.x+12,panel.y+12,panel.width-24,28),i==0?(hasAssembly?"原始成像参考":"原始拆件图"):i==1?(currentSkin["assembly_label"].Has?currentSkin["assembly_label"].S:trial?"十六件身体 + 独立佩剑":"十六件 · 关节装配"):paperDemoBody?"素身 · 关节校准":trial?"骨架 · 连接点":"骨架 · 铆钉对齐",new GUIStyle(centerSmall){normal={textColor=new Color(.25f,.17f,.09f)}});
            Rect figure=new Rect(panel.x+12,panel.y+45,panel.width-24,panel.height-65);
            if(i==0||i==1&&paperDemoOverlay&&hasAssembly){var texture=Resources.Load<Texture2D>("PaperCast/"+referenceImage);if(texture!=null){var color=GUI.color;if(i==1)GUI.color=new Color(1,.35f,.35f,.4f);float imageFit=Mathf.Min(figure.width/texture.width,figure.height/texture.height);Rect imageRect=new Rect(figure.center.x-texture.width*imageFit/2,figure.center.y-texture.height*imageFit/2,texture.width*imageFit,texture.height*imageFit);bool mirror=hasAssembly&&paperPreviewLeft;GUI.DrawTextureWithTexCoords(imageRect,texture,new Rect(mirror?1:0,0,mirror?-1:1,1));GUI.color=color;}}
            if(i>0)DrawPaperFigure(figure,i==2&&paperDemoBody&&!trial?"reference-body":paperReferenceSkin,clip,phase,paperPreviewLeft,i==2&&paperDemoBones);
            Fill(new Rect(panel.x+24,panel.yMax-32,panel.width-48,1),new Color(.3f,.2f,.1f,.25f));
        }
        GUILayout.Space(8);GUILayout.BeginHorizontal();
        paperPreviewAnimate=GUILayout.Toggle(paperPreviewAnimate,"播放  Space");
        bool auto=GUILayout.Toggle(paperDemoAuto,"自动巡演");if(auto!=paperDemoAuto){paperDemoAuto=auto;paperDemoClock=0;paperPreviewAnimate=true;}
        paperPreviewLeft=GUILayout.Toggle(paperPreviewLeft,"朝左  L");
        paperDemoBones=GUILayout.Toggle(paperDemoBones,"骨骼  B");
        bool enabled=GUI.enabled;GUI.enabled=enabled&&!trial;paperDemoBody=GUILayout.Toggle(paperDemoBody,"素身对照");
        GUI.enabled=enabled&&hasAssembly;paperDemoOverlay=GUILayout.Toggle(paperDemoOverlay,"原图叠加");GUI.enabled=enabled;
        if(GUILayout.Button("重播",chip,GUILayout.Width(70))){paperDemoClock=0;paperPreviewAnimate=true;}
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();T("慢放",small);paperDemoSpeed=GUILayout.HorizontalSlider(paperDemoSpeed,.25f,1.5f,GUILayout.Width(130));T(paperDemoSpeed.ToString("0.00")+"×",small);GUILayout.Space(25);T("动作进度 "+Mathf.RoundToInt(phase*100)+"%",small);
        float scrub=GUILayout.HorizontalSlider(phase,0,1);if(Mathf.Abs(scrub-phase)>.002f){paperPreviewPhase=scrub;paperPreviewAnimate=false;paperDemoAuto=false;}GUILayout.EndHorizontal();
        T(currentSkin["notes"].Has?currentSkin["notes"].S:trial?"身体部件保持原图长宽比与统一像素比例；肘腕藏在宽袖内部，垂布随骨段旋转。原图仅有拆件，当前为装配试样。":"三栏采用同一原图坐标与缩放。静立进度0%为比例标定姿态；叠图显示拆件轮廓与整身原画的差异。",small);
    }
    public void PaperSmokeReference(string clip,float phase,bool left=false,bool body=false){
        paperReferenceView=true;paperPreviewGallery=false;paperDemoIndex=Array.IndexOf(paperDemoClips,clip);paperPreviewPhase=phase;paperPreviewAnimate=false;paperDemoAuto=false;paperPreviewLeft=left;paperDemoBody=body;
    }
    public void PaperReferenceOverlay(bool overlay){paperDemoOverlay=overlay;}
    public void PaperReferenceSkin(string id){paperReferenceSkin=id;paperDemoOverlay=false;paperDemoBody=false;}
}
}
