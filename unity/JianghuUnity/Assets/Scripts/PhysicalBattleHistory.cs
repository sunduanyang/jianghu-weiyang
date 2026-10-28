namespace Jianghu {
public partial class JianghuApp {
    bool DrawPhysicalHistory(J battle){
        if(battle["ruleset"].I!=4||battle["fighters"].A.Count!=2)return false;
        string result=battle["winner"].Has?battle["fighters"][battle["winner"].I]["name"].S+"胜出":"双方收招";
        T("江湖切磋 · "+result,sub);
        T("交手 "+battle["elapsed"].N.ToString("0.0")+" 秒 · "+battle["reason"].S,small);
        for(int i=0;i<2;i++){
            var f=battle["fighters"][i];var summary=battle["summary"][i];
            T(f["name"].S+" · "+f["weapon"]["name"].S,sub);
            T("剩余气血 "+Num(f["vigor"])+" / "+Num(f["vigor_max"])+" · 命中 "+summary["hits"].S+" 次 · 造成伤害 "+Num(summary["damage"]),small);
        }
        T(battle["resource_policy"].S=="per-bout-v1"?"本场伤势、气力与疲劳不带入下场。":"交手结果已归档。",small);return true;
    }
}
}
