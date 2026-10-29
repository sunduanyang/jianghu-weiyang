using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    public void ShowThinkingForSmoke(){ViewPerson(SelectedPerson,PersonTab.Overview);}
    void DrawThinking(){
        var view=Person["planning_view"];var current=view["current"];var decision=view["decision"];
        H("行事与打算");T(view["role"]["label"].S,sub);T("长远心愿："+Person["goal"]["title"].S);
        var focus=view["attention"];
        if(focus.Has){T("思考关注 · "+focus["label"].S,sub);if(focus["reasons"].A.Count>0)T(string.Join("、",focus["reasons"].A.Select(r=>r.S)),small);}
        var deep=view["deep_thought"];
        if(deep.Has){
            T("最近完成的深思 · 两轮评估",sub);var assessment=deep["assessment"];T(assessment["focus"].S);
            if(focus["contact_event"].I>deep["signature"]["contact_event"].I)T("新的玩家来往尚待重新评估。",small);
            T("对玩家态度 · "+assessment["player_stance"].S,small);T(assessment["player_consideration"].S,small);
            foreach(var option in assessment["alternatives"].A){T("备选 · "+option["action_label"].S,small);T("收益："+option["benefit"].S+"；风险："+option["risk"].S,small);}
            T("受阻预案 · "+assessment["fallback_label"].S,small);T(assessment["fallback_reason"].S,small);
        }
        var policy=view["life_policy"];
        if(policy.Has){
            T("模型生活方针 · "+(policy["active"].B?"生效中":"已到期或处境改变"),sub);T(policy["title"].S);
            foreach(var preference in policy["priorities"].A)T(preference["label"].S+" · "+(preference["weight"].N>0?"更愿意":"有所克制"),small);
        }
        if(current.Has){
            T(current["title"].Has?current["title"].S:current["reason"].S,sub);
            if(current["undertaking"].Has){
                T("长期筹划 · "+current["undertaking"].S+" · "+current["chosen_by"].S+" · 每月推进一步，条件不足时等待",small);
                if(current["ai_reason"].Has&&current["ai_reason"].S.Length>0)T("本人取舍："+current["ai_reason"].S,small);
                if(current["interim"].Has&&current["interim"].A.Count>0)T("其余周次另有"+current["interim"].A.Count+"项模型安排的日常事务",small);
                if(current["target_name"].Has&&current["target"].Has&&Btn("查看对象 · "+current["target_name"].S))ViewPerson(current["target"].I,PersonTab.Overview);
            }
            Metric("进展",current["status"].S);T("接下来："+current["next_description"].S);
            if(current["blocker"].S.Length>0)T("眼前阻碍："+current["blocker"].S,small);
            foreach(var step in current["completed"].A.TakeLast(3)){
                T("已做："+step["action"].S,small);
                if(step["event_id"].Has&&Btn("追溯这次行动"))OpenEvent(step["event_id"].I);
            }
        }else T("尚无待办计划，下一次行动时依处境安排。",small);
        if(decision.Has){T("最近取舍",sub);T(decision["reason"].S);if(decision["policy_influence"].B)T("遵循模型方针："+decision["policy_title"].S,small);T("采取："+decision["action"].S,small);}
        if(view["history"].A.Count>0){
            T("此前打算",sub);
            foreach(var prior in view["history"].A.TakeLast(3).Reverse()){
                T(prior["status"].S+" · "+prior["title"].S);T(prior["reason"].S,small);
                if(prior["event_id"].Has&&Btn("查看对应年谱"))OpenEvent(prior["event_id"].I);
            }
        }
        T("此时想起",sub);
        foreach(var memory in Person["recalled_memories"].A){
            T(memory["fact"].S);T(memory["source"].S+" · "+memory["interpretation"].S,small);
            if(Btn("查看这段经历"))OpenEvent(memory["event_id"].I);
        }
        T(view["description"].S,small);
    }
}
}
