using System.Linq;
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

    // 门派页「门中事务」：门规、两派往来、入门指引；本门弟子另见考核、师承、差事与记过。
    // 所有按钮都来自后端 player_options，界面只负责展示与转交差事编号。
    void DrawSectLife(J life,J options,J factionId){
        if(!life.Has)return;
        if(life["member"].B)DrawSectDuties(life["player"]);
        else{
            T("入门",sub);T(life["join_rule"].S,small);
            var recruiters=life["recruiters"].A;
            if(recruiters.Count==0)T("此门暂无可引收入门的三品以上人物。",small);
            foreach(var r in recruiters){int id=r["id"].I;
                if(GUILayout.Button("可引收 · "+r["name"].S+" · "+r["grade_label"].S+" "+r["title"].S+" · "+r["place_name"].S+(r["here"].B?"（与你同地）":""),chip))Later(()=>OpenInteraction(id));}
        }
        DrawSectOptions(options,factionId);
        T("门规",sub);
        foreach(var p in life["precepts"].A)T("「"+p["label"].S+"」"+p["text"].S,text);
        T("违犯门规会被记过、扣有效贡献并暂停晋升半年；未请罪的过失累计"+(life["member"].B?life["player"]["expel_at"].S:"6")+"点即逐出门墙。",small);
        T("两派往来",sub);
        foreach(var r in life["relations"].A.Take(4).Concat(life["relations"].A.Skip(4).Where(x=>x["value"].N<=-10)))
            Metric(r["name"].S+" · "+r["alignment_label"].S,r["label"].S+" · "+Num(r["value"])+(r["feud"].Has?" · "+r["feud"].S:""));
        foreach(var r in life["relations"].A.Where(x=>x["feud"].Has))T(r["feud"].S+" · "+r["name"].S+"："+r["feud_note"].S,small);
        if(life["relations"].A.Count>4)Details("全部门派往来",life["relations"]);
    }
    void DrawSectDuties(J me){
        if(!me.Has)return;
        T("门中事务",sub);
        var a=me["assessment"];
        if(a.Has){
            Metric("晋阶考核",a["current"].S+"品 → "+a["target"].S+"品 · 达标"+a["completed_months"].S+" / "+a["required_months"].S+"月");
            if(a["sponsor"].Has)T("师门举荐："+a["sponsor"]["name"].S+"，所需月数减一。",small);
        }
        if(me["assessment_hint"].S.Length>0)T(me["assessment_hint"].S,small);
        var master=me["master"];
        if(master.Has){int id=master["id"].I;
            if(GUILayout.Button("师父 · "+master["name"].S+" · "+master["grade_label"].S+" "+master["title"].S+" · "+master["place_name"].S+" · 情分"+Num(master["affinity"]),chip))Later(()=>OpenInteraction(id));}
        else{
            T("尚未拜师。可在交互「修习」栏向本门四品以上前辈「拜师入室」。",small);
            foreach(var s in me["seniors"].A){int id=s["id"].I;
                if(GUILayout.Button("前辈 · "+s["name"].S+" · "+s["grade_label"].S+" "+s["title"].S+" · "+s["place_name"].S,chip))Later(()=>OpenInteraction(id));}
        }
        Metric("差事","进行中 "+me["active"].A.Count+" / "+me["active_limit"].S+" · 办妥 "+me["completed"].S+" · 失手 "+me["failed"].S);
        foreach(var c in me["active"].A){
            string due=c["months_left"].I<=0?"本月截止":"余"+(c["months_left"].I+1)+"月";
            T("「"+c["label"].S+"」 · "+c["reward_text"].S+" · "+due,text);
            T(c["step"].S,small);
            if(c["target"].Has&&c["target_alive"].B){int id=c["target"].I;GUILayout.BeginHorizontal();
                if(GUILayout.Button("查看"+c["target_name"].S+" ›",chip))Later(()=>ViewPerson(id));
                if(GUILayout.Button("与其交互 ›",chip,GUILayout.Width(110)))Later(()=>OpenInteraction(id));
                GUILayout.EndHorizontal();}
        }
        if(!me["at_seat"].B)T("领取差事、巡守与请罪须走入本门驻地（"+me["seat_place_name"].S+"）。",small);
        var marks=me["discipline"].A;
        if(marks.Count>0){
            Metric("记过",Num(me["strikes"])+" / "+me["expel_at"].S+"点");
            foreach(var m in marks.Take(4))T((m["atoned"].B?"已请罪 · ":"")+m["act_name"].S+m["victim_name"].S+" · 触犯"+string.Join("、",m["precepts"].A.Select(x=>x.S))+" · 扣贡献"+Num(m["penalty"]),small);
        }
        if(me["records"].A.Count>0)Details("门中近事",me["records"]);
    }
    void DrawSectOptions(J options,J factionId){
        foreach(var option in options.A){
            J choice=option["choice"];
            if(Btn(option["label"].S+(option["points"].I>0?" / "+option["points"].S+"行动力":""),Paused&&option["enabled"].B)){
                if(choice.Has)HeroAction(option["action"].S,"faction",factionId,"choice",choice);
                else HeroAction(option["action"].S,"faction",factionId);
            }
            if(!option["enabled"].B)T(option["reason"].S,small);
        }
    }
}
}
