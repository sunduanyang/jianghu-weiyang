using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    bool appearanceGallery=true;
    List<J> appearanceSamples;
    int appearanceSex,appearanceAge=28;
    bool appearanceLeft,appearanceWalk;
    string appearanceJob="镖师";
    List<J> AppearanceSamples(){
        if(appearanceSamples==null)appearanceSamples=W["npcs"].A.Where(n=>n["appearance"].Has).Take(24).Select(n=>J.Parse(n.ToString())).ToList();
        return appearanceSamples;
    }
    public void AppearancePreview(int sex=0,int age=28,bool left=false,bool walk=false,string job="镖师"){
        OpenSystem(SystemTab.Puppets);appearanceGallery=true;appearanceSex=sex;appearanceAge=age;appearanceLeft=left;appearanceWalk=walk;appearanceJob=job;
    }
    public J AppearancePreviewPerson(J original){
        var n=J.Parse(original.ToString());n["sex"]=new J(appearanceSex==0?"男":"女");n["age_months"]=new J(appearanceAge*12);n["job"]=new J(appearanceJob);n["faction"]=new J();n["identity"]=J.O();
        // Preview uses fixed samples; changing controls never writes back to a person.
        var heads=appearanceSex==0?new[]{"m-youth-farmer","m-middle-farmer","m-scholar","m-merchant","m-escort","m-hunter","m-artisan","m-performer"}:new[]{"f-youth-farmer","f-middle-farmer","f-scholar","f-merchant","f-escort","f-swordswoman"};
        n["appearance"]["head"]=new J(heads[n["id"].I%heads.Length]);n["appearance_view"]=J.O("grey",Mathf.Clamp((appearanceAge-42)/7,0,5));return n;
    }
    void DrawAppearanceGallery(){
        T("同龄同行的人，也有各自的头型、眉眼、唇形、肤色、发色与衣色。此处为预览，不改动存档。",small);
        GUILayout.BeginHorizontal();
        if(Btn(appearanceSex==0?"男子":"女子"))appearanceSex=1-appearanceSex;
        foreach(int age in new[]{12,28,49,70})if(GUILayout.Button(age+"岁",appearanceAge==age?selected:chip))appearanceAge=age;
        foreach(string job in new[]{"镖师","书生","农人"})if(GUILayout.Button(job,appearanceJob==job?selected:chip))appearanceJob=job;
        appearanceLeft=GUILayout.Toggle(appearanceLeft,"面朝左");appearanceWalk=GUILayout.Toggle(appearanceWalk,"行走");GUILayout.EndHorizontal();
        Rect gallery=GUILayoutUtility.GetRect(1200,600,GUILayout.ExpandWidth(true));StagePanel(gallery);
        var people=AppearanceSamples();
        for(int i=0;i<people.Count;i++){
            Rect cell=new Rect(gallery.x+12+(i%8)*(gallery.width-24)/8,gallery.y+8+(i/8)*(gallery.height-16)/3,(gallery.width-24)/8,(gallery.height-16)/3);
            var person=AppearancePreviewPerson(people[i]);
            DrawPaperPortrait(new Rect(cell.x+6,cell.y+3,38,44),person,backdrop:false);
            DrawPaperPerson(new Rect(cell.x+25,cell.y+8,cell.width-28,cell.height-32),person,false,appearanceWalk?"walk":"bind",appearanceLeft);
            GUI.Label(new Rect(cell.x,cell.yMax-23,cell.width,22),"人物 "+people[i]["id"].S,new GUIStyle(centerSmall){normal={textColor=new Color(.23f,.16f,.09f)}});
        }
    }
}
}
