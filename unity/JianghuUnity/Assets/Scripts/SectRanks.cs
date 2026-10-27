using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    void DrawSectRanks(J ranks){
        if(!ranks.Has)return;
        T("门派九品",sub);T(ranks["description"].S,small);
        foreach(var row in ranks["grades"].A)
            Metric(row["grade_label"].S+" · "+row["title"].S,row["population"].S+"人 · "+row["condition"].S);
    }
    public void ShowSectRanksForSmoke(){mainScroll=Vector2.zero;}
}
}
