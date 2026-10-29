using System.Linq;
using UnityEngine;

namespace Jianghu {
// 江湖卷·市井·技艺：造诣、锻造、题字、制药与调治。所有结算由后端 /api/crafts 与 /api/items 完成。
public partial class JianghuApp {
    public J Crafts=new J();
    string craftSection="造诣",forgeTemper="balanced",forgeTemplate="",composeTitle="";
    static readonly string[] CraftSections={"造诣","锻造","题字","制药","调治"};
    public void LoadCrafts(){Client.Fetch("/api/crafts",j=>Crafts=j);}
    void CraftAction(J body){Client.Act("/api/crafts",body,j=>{if(j["crafts"].Has)Crafts=j["crafts"];LoadInventory();});}
    void DrawCraftsPanel(J bag){
        GUILayout.BeginHorizontal();
        foreach(string section in CraftSections)if(GUILayout.Button(section,craftSection==section?selected:skin.button,GUILayout.Width(96))){craftSection=section;LoadCrafts();}
        GUILayout.FlexibleSpace();
        if(Crafts.Has)T("行动力 "+Crafts["points"].S+" · 现银 "+Num(Crafts["wealth"])+"两",small);
        GUILayout.EndHorizontal();
        if(craftSection=="制药"){DrawCrafting(bag);return;}
        if(!Crafts.Has){T("正在整理技艺…");LoadCrafts();return;}
        mainScroll=GUILayout.BeginScrollView(mainScroll);
        if(craftSection=="造诣")DrawCraftLevels();
        else if(craftSection=="锻造")DrawForging();
        else if(craftSection=="题字")DrawComposing();
        else DrawSelfCare();
        GUILayout.EndScrollView();
    }
    void DrawCraftLevels(){
        T("技艺造诣与营生熟练度是同一个数：匠人即锻造，药师即医术，书生即诗书。实践、学艺都会增长，越精越慢。",small);
        foreach(var c in Crafts["crafts"].A){
            GUILayout.BeginVertical(card);
            H(c["name"].S+" · "+c["level"].S);T(c["description"].S,small);
            double next=c["next_level"].Has?c["next_level"]["experience"].N:100;
            Bar("造诣",c["value"].N,next);
            if(c["next_level"].Has)T("下一境界："+c["next_level"]["title"].S,small);
            GUILayout.EndVertical();
        }
        T("向造诣高于你的人物「学艺」（人物交互 · 修习），或请懂锻造的人「请人修补」（人物交互 · 互动）。",small);
    }
    void DrawForging(){
        var forge=Crafts["forge"];
        T("耗用铁料或木料与炉火工费，每次消耗"+forge["points"].S+"行动力。行囊料不足时，自动向本地市集购入散料。修补："+(Crafts["repair"]["cap"].I>=100?"可自行修满。":"自行修补只能修到"+Crafts["repair"]["cap"].S+"耐久，锻造造诣达"+Crafts["repair"]["skilled"].S+"后可修满。"),small);
        GUILayout.BeginHorizontal();T("取向",sub);
        foreach(var temper in forge["tempers"].A)if(GUILayout.Button(temper["name"].S,forgeTemper==temper["id"].S?selected:skin.button,GUILayout.Width(96)))forgeTemper=temper["id"].S;
        GUILayout.EndHorizontal();
        var chosen=forge["tempers"].A.FirstOrDefault(t=>t["id"].S==forgeTemper);
        if(chosen!=null)T(chosen["description"].S,small);
        foreach(var row in forge["templates"].A){
            GUILayout.BeginVertical(card);
            var qualityStyle=new GUIStyle(sub);TextColor(qualityStyle,Hex(row["grade_color"].S));
            GUILayout.BeginHorizontal();T(row["grade_name"].S+" · "+row["name"].S,qualityStyle);GUILayout.FlexibleSpace();
            T(row["material"].S+" "+row["units"].S+"份 · 工费"+row["fee"].S+"银 · 难度"+row["difficulty"].S,small);GUILayout.EndHorizontal();
            string why=row["reason"].S;
            if(why.Length>0)T(why,small);
            if(Btn("起炉锻造",why.Length==0&&!W["battle_active"].B)){forgeTemplate=row["template"].S;CraftAction(J.O("operation","forge","template",forgeTemplate,"temper",forgeTemper));}
            GUILayout.EndVertical();
        }
    }
    void DrawComposing(){
        var compose=Crafts["compose"];
        T("研墨挥毫，写成一件墨宝，耗"+Num(compose["fee"])+"银纸墨与"+compose["points"].S+"行动力。品阶随诗书造诣；赠给懂行或尊奉传承的人，更能打动对方。",small);
        GUILayout.BeginHorizontal();GUILayout.Label("题目",small,GUILayout.Width(48));
        composeTitle=GUILayout.TextField(composeTitle,12,field,GUILayout.Width(260));
        if(GUILayout.Button("随意",chip,GUILayout.Width(64)))composeTitle="";
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();foreach(var title in compose["titles"].A)if(GUILayout.Button(title.S,chip,GUILayout.Width(64)))composeTitle=title.S;GUILayout.EndHorizontal();
        T(composeTitle.Length==0?"留空则由笔下自拟题目。":"将题为《"+composeTitle+"》。",small);
        string why=compose["reason"].S;if(why.Length>0)T(why,small);
        if(Btn("挥毫成篇",why.Length==0&&!W["battle_active"].B)){var body=J.O("operation","compose");if(composeTitle.Trim().Length>0)body["title"]=new J(composeTitle.Trim());CraftAction(body);}
        T("写成的墨宝在行囊「墨宝」类中，可选为赠物。",small);
    }
    void DrawSelfCare(){
        var wound=Crafts["wound"];var treat=Crafts["treat"];
        if(!wound["wounded"].B)T("身上没有伤势。",sub);
        else{T("伤势尚需约"+wound["remaining"].S+"个月痊愈",sub);foreach(var p in wound["parts"].A)Metric(p["name"].S+" · "+p["kind_name"].S,(p["severity"].N*100).ToString("0")+"%");}
        if(wound["illness"].B)T("身染疾病。",small);
        T("以自己的医术调治最重的伤处，耗"+treat["points"].S+"行动力；行囊有金疮药时一并敷用，疗效更佳。接骨须医术"+treat["set_bone"].S+"。",small);
        string why=treat["reason"].S;if(why.Length>0)T(why,small);
        if(Btn("自行调治",why.Length==0&&!W["battle_active"].B))CraftAction(J.O("operation","treat_self"));
        T("也可以在人物交互「诊治」中为他人疗伤，增进交情并精进医术。",small);
    }
}
}
