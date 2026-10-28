using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    public J Economy=new J();
    int economyPlace=-1,tradeAmount=1;
    Vector2 economyScroll;
    public void LoadEconomy(){
        if(economyPlace<0)economyPlace=Hero["place"].I;
        int requestedPlace=economyPlace;Economy=new J();
        Client.Fetch("/api/economy?place="+requestedPlace,j=>{if(requestedPlace==economyPlace)Economy=j;});
    }
    public void ShowEconomyForSmoke(){OpenMarket("市井供需");}
    public void ShowHouseholdForSmoke(){ViewPerson(SelectedPerson,PersonTab.Ties);}
    void MarketAction(string operation,string good){
        Client.Act("/api/economy",Client.HeroBody("market","operation",operation,"good",good,"amount",tradeAmount,"place",economyPlace),j=>{Economy=j["market"];LoadInventory();});
    }
    void DrawEconomy(){
        if(!Economy["enabled"].B){T("正在查询市集账目…");return;}
        var towns=Economy["towns"].A;int index=towns.FindIndex(t=>t["id"].I==economyPlace);
        GUILayout.BeginHorizontal();
        if(Btn("‹ 上一城")){economyPlace=towns[(index+towns.Count-1)%towns.Count]["id"].I;LoadEconomy();}
        GUILayout.Label(Economy["name"].S+" · 市集周转银 "+Num(Economy["cash"])+"两",sub,GUILayout.Width(400));
        if(Btn("下一城 ›")){economyPlace=towns[(index+1)%towns.Count]["id"].I;LoadEconomy();}
        if(Btn("主角所在城")){economyPlace=Hero["place"].I;LoadEconomy();}
        if(Btn("每笔 "+tradeAmount+"份"))tradeAmount=tradeAmount==1?5:1;
        GUILayout.EndHorizontal();
        T("食物先满足温饱，余钱用于衣药器具与文娱。库存影响下月价格；买卖每笔消耗1行动力。",small);
        bool here=economyPlace==Hero["place"].I;
        if(!here)T("主角需沿地图抵达本城后，才能在此买卖。",small);
        GUILayout.BeginHorizontal();GUILayout.BeginVertical(GUILayout.Width(880));
        economyScroll=GUILayout.BeginScrollView(economyScroll,GUILayout.Height(490));
        GUILayout.BeginHorizontal();
        foreach(var col in new[]{"物资 / 服务","存量 / 月需","买入 / 售出价","本月产 / 用 / 损","成交"})GUILayout.Label(col,small,GUILayout.Width(col=="物资 / 服务"?130:172));
        GUILayout.EndHorizontal();
        foreach(var g in Economy["goods"].A){
            GUILayout.BeginHorizontal(card);
            GUILayout.Label(g["name"].S,text,GUILayout.Width(122));
            GUILayout.Label(Num(g["stock"])+" / "+Num(g["target"]),text,GUILayout.Width(164));
            GUILayout.Label(Num(g["price"])+" / "+Num(g["sell_price"]),text,GUILayout.Width(164));
            GUILayout.Label(Num(g["produced"])+" / "+Num(g["consumed"])+" / "+Num(g["spoiled"]),small,GUILayout.Width(164));
            bool durable=g["id"].S!="study"&&g["id"].S!="leisure"&&g["id"].S!="transport";
            if(Btn("买",here&&durable&&Paused&&Hero["created"].B&&Hero["points"].I>0&&g["stock"].N>0))MarketAction("buy",g["id"].S);
            if(Btn("卖",here&&durable&&Paused&&Hero["points"].I>0&&Economy["household"]["stock"][g["id"].S].N>0))MarketAction("sell",g["id"].S);
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();GUILayout.EndVertical();
        GUILayout.BeginVertical(card);DrawHousehold(Economy,true);GUILayout.EndVertical();GUILayout.EndHorizontal();
    }
    void DrawHousehold(J data,bool hero){
        H(hero?"主角家计":"家计与储备");var life=data["household"];
        if(!data["enabled"].B){T("此存档尚未登记家计。");return;}
        if(!life["month"].Has)T("升级后的首次月结将记录需求与消费。",small);
        else {
            T("最近月结："+data["settlement_date"].S,small);
            Metric("需求满足",(life["satisfaction"].N*100).ToString("0")+"%");
            Metric("售货收入 / 支出",Num(life["income"])+" / "+Num(life["expenses"])+"银");
        }
        foreach(var pair in life["needs"].D){
            string label=pair.Key=="food"?"温饱":data["goods"].A.FirstOrDefault(g=>g["id"].S==pair.Key)?["name"].S??pair.Key;
            T(label+"  "+Num(pair.Value["satisfied"])+" / "+Num(pair.Value["required"])+(pair.Value["missing"].N>.0001?" · 尚缺 "+Num(pair.Value["missing"]):" · 已足"),small);
            if(pair.Value["reason"].S.Length>0)T("　"+pair.Value["reason"].S,small);
        }
        T("随身物资",sub);
        foreach(var g in data["goods"].A)if(life["stock"][g["id"].S].N>0)Metric(g["name"].S,Num(life["stock"][g["id"].S])+"份");
        T("短缺会增加生活压力；缺粮、缺药影响体力，缺衣缺器影响下月产出。",small);
    }
}
}
