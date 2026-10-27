using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    bool showHeroRank;
    public void ShowRankProgressForSmoke(){personScreen=true;dialogueOpen=false;personTab=10;personScroll=Vector2.zero;}
    public void ShowHeroRankForSmoke(){personScreen=false;dialogueOpen=false;Page=3;battleView=false;showHeroRank=true;Client.Fetch("/api/martial",j=>Martial=j);}
    void DrawRankProgress(J view,bool hero){
        H("品阶进退");
        if(!view["enabled"].B){T("尚未启用品阶考核。",small);return;}
        T(view["description"].S,small);
        if(view["next_assessment"].Has)T("下次月结考核："+view["next_assessment"].S,small);
        var sect=view["sect"];
        if(sect.Has){
            if(sect["current"].I<=2)T(sect["current"].I==1?"在任主持：持续失职或违纪将重新推举。":"二品以上须出现主持空缺，并经门中实际推举。",sub);
            else {
                T("下一品："+"一二三四五六七八九"[sect["target"].I-1]+"品",sub);
                Metric("连续达标",sect["completed_months"].S+" / "+sect["required_months"].S+"月");
                foreach(var req in sect["requirements"].A)T((req["met"].B?"✓ ":"· ")+req["label"].S+"  "+Num(req["current"])+" / "+Num(req["required"]),small);
            }
            if(sect["blocked"].S.Length>0)T(sect["blocked"].S,sub);
            if(sect["penalty"].N>0)Metric("待补修的违纪扣功",Num(sect["penalty"]));
            if(sect["deficient_months"].I>0)T("维持考察 "+sect["deficient_months"].S+" / 3月 · "+sect["reason"].S,sub);
            T("捐助不能替代履职。病伤休养不按失职降阶；短期数值波动设缓冲。",small);
        }
        if(view["warning"].S.Length>0)T(view["warning"].S,sub);
        foreach(var option in view["options"].A){
            GUILayout.BeginVertical(card);T(option["label"].S+" → "+option["title"].S,sub);
            foreach(var req in option["requirements"].A)T((req["met"].B?"✓ ":"· ")+req["label"].S+"  "+Num(req["current"])+" / "+Num(req["required"]),small);
            T("筹办费用 "+Num(option["cost"])+"银"+(hero?" · 4行动力":""),small);
            if(hero&&Btn("办理 · "+option["label"].S,Paused&&option["enabled"].B&&Hero["points"].I>=4))
                Client.Act("/api/ranks",Client.HeroBody(option["kind"].S),j=>{});
            GUILayout.EndVertical();
        }
        if(!sect.Has&&view["options"].A.Count==0)T("当前高阶身份依实际家业与任职维持；一、二品须真实空缺与任命。",small);
        T("升降履历",sub);
        foreach(var item in view["history"].A.TakeLast(4).Reverse()){
            T(item["text"].S,small);
            if(item["event_id"].Has&&Btn("查看这次升降的年谱"))OpenEvent(item["event_id"].I);
        }
    }
}
}
