using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    bool showMartialCatalog;
    int martialSchoolIndex;
    void DrawKnownMartial(J m) {
        H("已掌握功法");
        J c=m["curriculum"];
        if(c.Has&&c["origin_place"].Has){
            T("籍贯 · "+c["country_name"].S+" / "+c["origin_name"].S,small);
            T(c["system"].S=="sect"?"依门中品阶掌握本派本品至九品功法。":"依社会品阶掌握一门籍贯国功法，来源门派固定。",small);
        }
        T("已掌握功法的加成自动累加，计入上方能力；每项能力最高100。",small);
        if(m["arts"].A.Count==0)T("创建主角后，按籍贯与身份确定初始功法。",small);
        DrawRankArts(m["arts"]);
    }
    void DrawRankArts(J arts) {
        foreach(J art in arts.A.OrderBy(a=>a["grade"].N)){
            GUILayout.BeginVertical(card);
            T(art["grade_label"].S+" · "+art["name"].S,sub);
            T(art["school_name"].S+" · "+art["kind"].S,small);
            T(art["description"].S);
            GUILayout.EndVertical();
        }
    }
    void DrawMartialCatalog() {
        if(Btn(showMartialCatalog?"收起各派九品武学":"查看各派九品武学"))showMartialCatalog=!showMartialCatalog;
        if(!showMartialCatalog)return;
        var schools=Martial["catalog"].A;
        if(schools.Count==0)return;
        martialSchoolIndex=Mathf.Clamp(martialSchoolIndex,0,schools.Count-1);
        martialSchoolIndex=GUILayout.SelectionGrid(martialSchoolIndex,schools.Select(s=>s["name"].S).ToArray(),4,chip);
        H(schools[martialSchoolIndex]["name"].S+" · 九品传承");
        T("一品最高，九品入门。每门功法只提升列出的能力。",small);
        DrawRankArts(schools[martialSchoolIndex]["arts"]);
    }
}
}
