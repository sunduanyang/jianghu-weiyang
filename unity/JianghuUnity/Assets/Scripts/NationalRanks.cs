using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    int nationalGrade;
    int nationalRevision=-1,nationalCountry=-1;
    string nationalQuery="";

    void DrawNationalRanks(J ranks){
        if(!ranks.Has)return;
        int country=ranks["country"].I;
        if(nationalRevision!=Client.Revision||nationalCountry!=country){
            nationalRevision=Client.Revision;nationalCountry=country;
            Client.Fetch("/api/country/"+country,j=>{if(Institution["government"].Has&&Institution["id"].I==country)Institution=j;});
        }
        H("社会九品");
        T(ranks["description"].S,small);
        T("非门派人士 "+ranks["population"].S+"人 · 门派人士 "+ranks["sect_population"].S+"人另列",sub);
        var rankRow=new GUIStyle(card){padding=new RectOffset(8,8,3,3),margin=new RectOffset(0,0,2,2)};
        foreach(var rank in ranks["grades"].A){
            GUILayout.BeginHorizontal(rankRow);
            if(GUILayout.Button(rank["grade_label"].S+" · "+rank["class_label"].S,nationalGrade==rank["grade"].I?selected:chip,GUILayout.Width(145)))nationalGrade=nationalGrade==rank["grade"].I?0:rank["grade"].I;
            GUILayout.Label(string.Join("、",rank["examples"].A.Take(3).Select(x=>x.S)),small);
            GUILayout.FlexibleSpace();
            GUILayout.Label(rank["population"].S+"人",small,GUILayout.Width(65));
            GUILayout.EndHorizontal();
        }
        T(ranks["rules"].S,small);
        GUILayout.BeginHorizontal();T("在籍名册",sub);if(Btn("全部品级"))nationalGrade=0;nationalQuery=GUILayout.TextField(nationalQuery);GUILayout.EndHorizontal();
        var people=ranks["roster"].A.Where(n=>(nationalGrade==0||n["grade"].I==nationalGrade)&&n["name"].S.Contains(nationalQuery)).ToList();
        T((nationalGrade==0?"全部":"一二三四五六七八九"[nationalGrade-1]+"品")+" · "+people.Count+"人",small);
        foreach(var n in people)if(GUILayout.Button(n["grade_label"].S+" · "+n["name"].S+" · "+n["title"].S,rowStyle)){SelectPerson(n["id"].I);OpenPersonScreen();}
        if(people.Count==0)T("此品暂无符合条件的人物。",small);
    }

    void DrawPersonRank(){
        var rank=Person["identity"];
        H(rank["system_label"].S+" · "+rank["grade_label"].S);
        if(rank["class_label"].Has)T(rank["class_label"].S,sub);
        T(rank["title"].S,sub);
        if(rank["system"].S=="nation"&&rank["country"].Has){
            int country=rank["country"].I;
            if(Btn("查看"+Country(country)["name"].S+"九品名册"))OpenInstitution(true,country);
        }
        Details("兼有身份",rank["roles"]);
        if(Btn("查看品阶升降"))personTab=10;
    }
}
}
