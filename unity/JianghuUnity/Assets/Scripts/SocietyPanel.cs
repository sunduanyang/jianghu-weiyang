using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    int communityPlace = -1;
    Vector2 communityScroll, communityListScroll;

    public void ShowSocietyForSmoke(int place) { institutionKind = "乡里"; communityPlace = place; OpenJianghu(JianghuTab.Powers); }
    public void ShowSocialPersonForSmoke() { ViewPerson(SelectedPerson, PersonTab.Ties); }

    void DrawSocialPerson() {
        var s = Person["social_view"];
        if (!s.Has) return;
        T("家计与乡里", sub);
        var local=Person["local_life_view"];
        if(local["enabled"].B){
            Metric("固定住所",local["home"]["place_name"].S+" · "+local["home"]["name"].S);
            Metric("工作地点",local["working"].B?local["work"]["name"].S+" · "+local["work"]["facility"].S:"尚未从业，随家人生活");
            Metric("当前在场",local["current"]["name"].S+" · 第"+(local["current"]["row"].I+1)+"行，第"+(local["current"]["col"].I+1)+"列");
            if(local["working"].B)Metric("住所至工地",local["commute_cost"].S+"步行成本 · 含在谋生时间内");
            T(local["basis"].S,small);
        }
        Metric("同住家人 / 合计家资", s["household_members"].A.Count + "人 / " + Num(s["household_wealth"]) + "银");
        Metric("亲友扶助 · 给出 / 收到", Num(s["support_given"]) + " / " + Num(s["support_received"]) + "银");
        Metric("乡里互济 · 捐出 / 受助", Num(s["community_given"]) + " / " + Num(s["community_received"]) + "银");
        Metric("受照护 / 连续困顿", s["care_received"].S + "次 / " + s["hardship_months"].S + "月");
        foreach (var q in s["household_members"].A)
            { int member = q["id"].I; if (GUILayout.Button(q["name"].S + " · " + q["relation"].S + " · " + Num(q["wealth"]) + "银", chip)) Later(() => SwitchSubject(member)); }
        foreach (var key in new[] {"last_support_event", "last_care_event"})
            if (s[key].Has && Btn(key == "last_support_event" ? "查看最近扶助前因" : "查看最近照护前因")) OpenEvent(s[key].I);
        if (s["migration"].Has) T("困顿中考虑依此前见闻前往" + Place(s["migration"]["destination"].I)["name"].S + "投亲；行路仍需盘缠与两周。", small);
        if (Btn("查看所在地乡里")) { int home = Person["place"].I; Later(() => { institutionKind = "乡里"; communityPlace = home; OpenJianghu(JianghuTab.Powers); }); }
        GUILayout.Space(8);
    }

    void DrawCommunityDirectory() {
        var s = W["society"];
        if (!s.Has) { T("载入乡里账簿中。"); return; }
        var m = s["monthly"];
        T("本月亲友扶助 " + Num(m["support_silver"]) + "银 · 义仓救济 " + Num(m["relief_silver"]) + "银 · 捐助 " + Num(m["donated_silver"]) + "银 · 照护 " + m["care_visits"].S + "次", small);
        T("各乡义仓共存 " + Num(s["fund"]) + "银 · 连续困顿 " + s["hardship"].S + "人 · 缺少同地监护的未成年 " + s["unsupported_children"].S + "人", small);
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(275));
        communityListScroll = GUILayout.BeginScrollView(communityListScroll);
        foreach (var c in s["communities"].A.Where(c => c["name"].S.Contains(institutionQuery)))
            if (GUILayout.Button(c["name"].S + " · " + c["population"].S + "人\n义仓 " + Num(c["fund"]) + "银 · 待互济 " + c["needy"].S + "人", rowStyle)) communityPlace = c["place"].I;
        GUILayout.EndScrollView(); GUILayout.EndVertical();
        GUILayout.BeginVertical(GUILayout.Width(665));
        communityScroll = GUILayout.BeginScrollView(communityScroll);
        var town = s["communities"].A.FirstOrDefault(c => c["place"].I == communityPlace);
        if (town == null) T("择一处城镇，查看乡里家计与义仓收支。");
        else {
            H(town["name"].S + " · 乡里互济");
            Metric("在世居民 / 待互济", town["population"].S + " / " + town["needy"].S + "人");
            Metric("连续困顿 / 未成年缺监护", town["hardship"].S + " / " + town["unsupported_children"].S + "人");
            Metric("实有义仓", Num(town["fund"]) + "银");
            Metric("累计捐入 / 救济支出", Num(town["revenue"]) + " / " + Num(town["expenses"]) + "银");
            Metric("账面结余", (town["opening_fund"].N + town["revenue"].N - town["expenses"].N).ToString("0.##") + "银");
            Metric("邻里照护", town["care_visits"].S + "次");
            T("亲友先量力扶助，再由本乡义仓救急，随后官府救济。义仓由居民自愿捐银，按未成年、病弱与家资排序救助。探病双方各用一周，每人每月最多受一次邻里照护。", small);
            T("同住家庭按同地亲缘与监护连接统计；义亲可以互助。旧档从升级时开始记账，义仓期初为零。", small);
            T("最近乡里记事", sub);
            foreach (var id in town["records"].A.AsEnumerable().Reverse())
                if (Btn("查看第 " + id.S + " 条互济记录 ↗")) OpenEvent(id.I);
            if (town["records"].A.Count == 0) T("尚无捐助、救济或照护记录。", small);
            if (Btn("在地图中寻访此地")) { int place = town["place"].I; Later(() => { SelectedPlace = place; GoStage(Stage.World); }); }
        }
        GUILayout.EndScrollView(); GUILayout.EndVertical(); GUILayout.EndHorizontal();
    }
}
}
