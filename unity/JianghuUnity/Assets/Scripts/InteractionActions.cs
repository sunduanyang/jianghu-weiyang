using System.Collections;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    J interactionData=new J();
    int interactionRequest,interactionSubject=-1,interactionResultEvent;
    string interactionChoice="",interactionItem="",interactionArt="",interactionDirection="buy",interactionResult="";
    Vector2 interactionScroll,interactionOptionsScroll;
    bool InteractionDataReady=>interactionData.Has&&interactionData["npc_id"].I==FocusPersonId;
    bool InteractionFormOpen=>InteractionDataReady&&new[]{"gift","trade","teach","steal"}.Contains(interactionChoice);
    bool InteractionConversationOpen=>interactionChoice.StartsWith("social:");
    J InteractionAction=>InteractionDataReady?interactionData["actions"].A.Concat(interactionData["conversations"].A).FirstOrDefault(a=>a["id"].S==interactionChoice)??new J():new J();

    void LoadInteraction(){
        int id=FocusPersonId,request=++interactionRequest;if(id<0)return;
        if(interactionSubject!=id){interactionSubject=id;interactionData=new J();interactionChoice="";interactionResult="";interactionResultEvent=0;interactionItem="";interactionArt="";interactionScroll=interactionOptionsScroll=Vector2.zero;ResetInteractionConversation();}
        Client.Fetch("/api/interaction?npc="+id,j=>{
            if(request!=interactionRequest||!TopIs(ViewKind.Interaction)||FocusPersonId!=id)return;
            interactionData=j;
            if(InteractionConversationOpen&&InteractionAction.Has)TopView.Tab=InteractionAction["category"].I;
            ObserveInteractionConversation(j["social"]);
            var latest=j["records"].A.FirstOrDefault();
            if(latest!=null&&latest["id"].I>interactionResultEvent){interactionResultEvent=latest["id"].I;interactionResult=latest["summary"].S;}
        });
    }
    void ChooseInteraction(string key){
        interactionChoice=key;interactionItem="";interactionArt="";interactionResult="";interactionScroll=Vector2.zero;
        if(InteractionConversationOpen){
            if(InteractionAction.Has&&TopIs(ViewKind.Interaction))TopView.Tab=InteractionAction["category"].I;
            conversationPane=0;conversationScroll=new Vector2(0,float.MaxValue);SelectLatestPersuasion();nextConversationPoll=0;
        }
    }
    void DrawInteractionWords(Rect r,string greeting){
        Fill(r,new Color(.075f,.115f,.115f));
        J action=InteractionAction;
        if(!action.Has){
            var social=InteractionSocial;var latest=social["requests"].A.LastOrDefault();
            string message=interactionResult.Length>0?interactionResult:latest!=null?(latest["status"].S=="pending"?"对方正在斟酌你的话……":latest["reply"].S.Length>0?CharacterName+"："+latest["reply"].S:greeting):greeting;
            GUI.Label(new Rect(r.x+24,r.y+14,r.width-48,76),message,new GUIStyle(centerTitle){fontSize=18,wordWrap=true});
            if(CharacterReady&&Person["player_reactions"].A.Any(x=>x["status"].S=="pending"))
                GUI.Label(new Rect(r.x+24,r.y+84,r.width-48,20),CharacterName+"神色微动，心里正在掂量这件事……",centerSmall);
            if(Client.Error.Length>0)GUI.Label(new Rect(r.x+20,r.yMax-36,r.width-210,28),Client.Error,small);
            if(GUI.Button(new Rect(r.xMax-174,r.yMax-38,154,28),"交谈记录与说服  ›",chip))Later(()=>ChooseInteraction("social:talk"));
            return;
        }
        GUI.Label(new Rect(r.x+22,r.y+12,r.width-44,30),action["label"].S+" · "+action["points"].S+"行动力",sub);
        GUI.Label(new Rect(r.x+22,r.y+46,r.width-44,62),action["description"].S,text);
        string why=!Paused?"请先暂停世界推演":action["reason"].S;
        GUI.Label(new Rect(r.x+22,r.yMax-42,r.width-214,34),why.Length>0?why:Client.Error,small);
        bool enabled=GUI.enabled;GUI.enabled=enabled&&Paused&&action["enabled"].B&&!Client.Busy;
        if(GUI.Button(new Rect(r.xMax-182,r.yMax-48,160,36),"确认"+action["label"].S,selected))Later(()=>BeginInteraction());GUI.enabled=enabled;
    }
    void BeginInteraction(string item=null,string art=null){
        if(Client.Busy||!InteractionDataReady)return;
        if(InteractionConversationOpen){SocialAction(InteractionAction["action"].S);return;}
        interactionRequest++;
        int id=FocusPersonId;string key=interactionChoice;
        J body=J.O("npc_id",id,"action",key,"revision",interactionData["revision"],"month",interactionData["month"]);
        if(item!=null)body["item_id"]=new J(item);
        if(art!=null)body["art"]=new J(art);
        if(key=="trade"){
            body["direction"]=new J(interactionDirection);
            var offer=interactionData[interactionDirection].A.FirstOrDefault(i=>i["id"].S==item);
            if(offer!=null)body["quoted_price"]=offer["price"];
        }
        StartCoroutine(SendInteraction(id,key,body));
    }
    IEnumerator SendInteraction(int id,string key,J body){
        Client.Busy=true;bool succeeded=false;J result=new J();
        yield return Client.Request("/api/interaction",body,j=>{succeeded=true;result=j;});
        string failure=succeeded?"":Client.Error;
        yield return Client.Refresh();Client.Busy=false;
        bool here=TopIs(ViewKind.Interaction)&&FocusPersonId==id;
        if(succeeded){
            if(result["martial"].Has)Martial=result["martial"];
            Client.Status=result["event"]["summary"].S;
            if(here){
                interactionData=result["interaction"];interactionResult=result["event"]["summary"].S;interactionResultEvent=result["event"]["id"].I;interactionItem="";interactionArt="";
                SelectPerson(id);
                if(result["martial"].Has){interactionChoice="";OpenBattle();}
                else if(!new[]{"gift","trade","teach","steal"}.Contains(key))interactionChoice="";
            }
        }else if(here){LoadInteraction();interactionResult=failure;}
    }
    void DrawInteractionForm(Rect area){
        Fill(area,new Color(.065f,.095f,.095f));Outline(area,hairline);
        J action=InteractionAction;
        if(GUI.Button(new Rect(area.x+16,area.y+12,124,32),"‹ 返回分类",chip))Later(()=>interactionChoice="");
        GUI.Label(new Rect(area.x+158,area.y+12,area.width-180,32),action["label"].S+" · "+action["points"].S+"行动力",sub);
        Rect content=new Rect(area.x+18,area.y+58,area.width-36,area.height-72);
        GUILayout.BeginArea(content);
        string why=!Paused?"请先暂停世界推演":action["reason"].S;
        T(interactionResult.Length>0?interactionResult:action["description"].S,text);
        if(Client.Error.Length>0)T(Client.Error,small);
        if(interactionChoice=="teach")DrawInteractionTeaching(action,why);
        else DrawInteractionItems(action,why);
        GUILayout.EndArea();
    }
    void DrawInteractionTeaching(J action,string why){
        var offers=interactionData["manual_offers"].A;
        interactionScroll=GUILayout.BeginScrollView(interactionScroll,GUILayout.Height(218));
        foreach(var offer in offers){string key=offer["art"].S;
            if(GUILayout.Button(offer["name"].S+(offer["reason"].S.Length>0?" · "+offer["reason"].S:""),interactionArt==key?rowOnStyle:rowStyle,GUILayout.Height(42)))interactionArt=key;
        }
        if(offers.Count==0)T("对方暂无可传授的功法。",small);
        GUILayout.EndScrollView();
        var chosen=offers.FirstOrDefault(o=>o["art"].S==interactionArt);
        string blocked=why.Length>0?why:chosen==null?"请选择一门功法":chosen["reason"].S;
        T(blocked.Length>0?blocked:"获得秘籍后，前往自己的行囊使用即可学会。",small);
        GUILayout.BeginHorizontal();
        if(Btn("请求传授秘籍",blocked.Length==0&&action["enabled"].B))Later(()=>BeginInteraction(art:interactionArt));
        if(Btn("查看我的行囊"))Later(()=>ViewPerson(-1,PersonTab.Bag));
        GUILayout.EndHorizontal();
    }
    void DrawInteractionItems(J action,string why){
        string key=interactionChoice;
        if(key=="trade"){
            GUILayout.BeginHorizontal();
            foreach(string mode in new[]{"buy","sell"})if(GUILayout.Button(mode=="buy"?"向对方购买":"向对方出售",interactionDirection==mode?selected:chip)){interactionDirection=mode;interactionItem="";interactionScroll=Vector2.zero;}
            GUILayout.EndHorizontal();
            T("我的银两 "+Num(interactionData["player_wealth"])+" · 对方银两 "+Num(interactionData["npc_wealth"]),small);
        }
        if(key=="steal")T("本次成功率 "+(interactionData["theft_chance"].N*100).ToString("0.#")+"% · 败露将结下怨隙",small);
        var rows=interactionData[key=="trade"?interactionDirection:key=="gift"?"gifts":"steal"].A;
        interactionScroll=GUILayout.BeginScrollView(interactionScroll,GUILayout.Height(key=="trade"?174:205));
        if(key=="steal"){
            bool money=interactionItem=="silver";
            if(GUILayout.Button("偷取银两 · 至多8银（对方现有 "+Num(interactionData["npc_wealth"])+"）",money?rowOnStyle:rowStyle,GUILayout.Height(38)))interactionItem="silver";
        }
        foreach(var item in rows){
            string id=item["id"].S;string price=key=="trade"?" · "+Num(item["price"])+"银":"";
            if(GUILayout.Button(item["grade_label"].S+" · "+item["name"].S+price,interactionItem==id?rowOnStyle:rowStyle,GUILayout.Height(38)))interactionItem=id;
        }
        if(rows.Count==0)T(key=="gift"?"没有可赠送的未装备物品。":"没有可选择的未装备物品。",small);
        GUILayout.EndScrollView();
        var chosen=rows.FirstOrDefault(i=>i["id"].S==interactionItem);
        bool silver=key=="steal"&&interactionItem=="silver";
        string blocked=why.Length>0?why:chosen==null&&!silver?"请选择一件物品":silver&&interactionData["npc_wealth"].N<=0?"对方已经没有余银":chosen==null?"":chosen["reason"].S;
        T(blocked.Length>0?blocked:chosen!=null?chosen["description"].S:"偷取失败或成功后被察觉，都会引起对方敌意。",small);
        string button=key=="trade"?(interactionDirection=="buy"?"确认购买":"确认出售"):key=="gift"?"确认赠送礼物":"确认偷窃";
        if(Btn(button,blocked.Length==0&&action["enabled"].B))Later(()=>BeginInteraction(silver?null:interactionItem));
    }
}
}
