using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    void DrawCombatAttributes(J combat,double condition,double energy,bool compact=false) {
        if(!combat.Has)return;
        H("人物战斗属性");
        var abilities=combat["attributes"].A;
        for(int i=0;i<abilities.Count;i++) {
            if(!compact&&i%2==0)GUILayout.BeginHorizontal();
            var a=abilities[i];GUILayout.BeginVertical(card);
            Bar(a["name"].S,a["value"].N,100);T(a["description"].S,small);
            T("基础 "+Num(a["base"])+" · 功法 +"+Num(a["bonus"])+(a["bonus"].N>a["applied_bonus"].N?" · 已达上限":""),small);
            GUILayout.EndVertical();
            if(!compact&&(i%2==1||i==abilities.Count-1))GUILayout.EndHorizontal();
        }
        var p=combat["parameters"];var body=combat["physique"];
        double maxHealth=p["max_health"].N,maxStamina=p["stamina_max"].N;
        T("气血 "+(maxHealth*System.Math.Max(.25,condition/100)).ToString("0.#")+" / "+maxHealth.ToString("0.#")+
          "    气力 "+(maxStamina*energy/100).ToString("0.#")+" / "+maxStamina.ToString("0.#"),sub);
        T("架势上限 "+Num(p["balance_max"])+" · 疲劳随连续发力积累，停手后消退。",small);
        T("体格 · 身高 "+Num(body["height_cm"])+"厘米 · 体重 "+Num(body["weight_kg"])+"公斤 · 臂展 "+Num(body["arm_span_cm"])+"厘米",small);
        T("功法提升人物能力；兵器与衣甲另提供装备效果。",small);
    }
}
}
