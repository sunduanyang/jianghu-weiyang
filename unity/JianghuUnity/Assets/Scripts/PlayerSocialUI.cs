using System;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    Vector2 conversationScroll;
    float nextConversationPoll;
    bool conversationPolling;

    void UpdateSocialDialogue(){
        if(!DialogueVisible||!Person.Has||Client.Busy||conversationPolling||Time.unscaledTime<nextConversationPoll)return;
        nextConversationPoll=Time.unscaledTime+1.2f;
        int id=SelectedPerson;conversationPolling=true;
        StartCoroutine(PollSocialPerson(id));
    }
    System.Collections.IEnumerator PollSocialPerson(int id){
        yield return Client.Request("/api/npc/"+id,null,j=>{
            if(SelectedPerson==id){
                bool wasPending=Person["player_social"]["pending"].B;
                Person=j;
                if(wasPending&&!j["player_social"]["pending"].B){conversationScroll.y=float.MaxValue;StartCoroutine(Client.Refresh());}
            }
        },false);
        conversationPolling=false;
    }

    public void SocialAction(string action){
        var social=Person["player_social"];
        int id=SelectedPerson;
        Client.Act("/api/social",J.O("npc_id",id,"action",action,"revision",social["revision"]),j=>{
            if(SelectedPerson==id)Person["player_social"]=j;
            talk="";nextConversationPoll=0;conversationScroll.y=float.MaxValue;
        });
    }

    public void Persuade(int requestId,string optionId=null){
        int id=SelectedPerson;
        J body=J.O("npc_id",id,"mode",optionId==null?"options":"persuade","request_id",requestId,"revision",Person["player_social"]["revision"]);
        if(optionId!=null)body["option_id"]=new J(optionId);
        Client.Act("/api/social",body,j=>{
            if(SelectedPerson==id)Person["player_social"]=j;
            nextConversationPoll=0;conversationScroll.y=float.MaxValue;
        });
    }

    void DrawSocialConversation(Rect area){
        var s=Person["player_social"];
        GUILayout.BeginArea(area);
        T(Person["name"].S+" · 当面交谈",heading);
        var r=s["relationship"];
        T((r["sworn"].B?"义亲 · ":"")+"情分 "+Num(r["affinity"])+" · 信任 "+Num(r["trust"]),small);
        conversationScroll=GUILayout.BeginScrollView(conversationScroll,GUILayout.Height(440));
        if(s["requests"].A.Count==0)T("你走上前，准备与对方说话。",small);
        foreach(var request in s["requests"].A){
            T("你"+(request["persuasion_label"].Has?"（"+request["persuasion_label"].S+"）":"")+"："+request["text"].S,text);
            if(request["status"].S=="pending")T("对方正在斟酌你的话……",sub);
            else if(request["status"].S=="completed"){
                T(Person["name"].S+(request["reply_source"].S=="规则转述"?"（依据决定转述）":"")+"："+request["reply"].S,sub);
                T(request["outcome"].S,small);
            }else T(request["error"].S,small);
            if(request["can_persuade"].B){
                T("对方已拒绝。选择一种说服方式，再请对方考虑一次。",small);
                foreach(var option in request["persuasion_options"].A){
                    bool selected=!request["selected_option"].Has||request["selected_option"].S==option["id"].S;
                    bool previous=GUI.enabled;GUI.enabled=previous&&Paused&&!Client.Busy&&W["ai"]["config"]["enabled"].B&&selected;
                    if(GUILayout.Button("【"+option["label"].S+"】"+option["text"].S,rowStyle))Persuade(request["id"].I,option["id"].S);
                    GUI.enabled=previous;
                }
            }else if(request["can_retry_options"].B){
                T(request["options_error"].S,small);
                if(Btn("重新拟定五种说服方式",Paused&&W["ai"]["config"]["enabled"].B))Persuade(request["id"].I);
            }
            GUILayout.Space(12);
        }
        GUILayout.EndScrollView();
        if(s["pending"].B){
            string stage=W["ai"]["current_stage"].S;
            T(string.IsNullOrEmpty(stage)?"正在等候回应……":stage,small);
        }
        if(!W["ai"]["config"]["enabled"].B){T("交谈需要本地模型：请在「系统 · 本地模型」启用 AI。",small);if(Btn("打开本地模型设置"))Later(()=>OpenSystem(SystemTab.Model));}
        else if(s["reason"].S!="")T(s["reason"].S,small);
        bool enabled=Paused&&W["ai"]["config"]["enabled"].B&&!s["pending"].B&&s["reason"].S=="";
        T("完成一次交谈耗1行动力；本月初次面谈占用对方1周。",small);
        GUILayout.BeginHorizontal();
        if(Btn("闲叙近况",enabled))SocialAction("talk");
        if(Btn("切磋",Paused&&!s["pending"].B))Intervene("challenge");
        GUILayout.EndHorizontal();
        var options=s["options"].A.Where(o=>o["id"].S!="talk").ToList();
        for(int i=0;i<options.Count;i+=3){
            GUILayout.BeginHorizontal();
            for(int j=i;j<Math.Min(i+3,options.Count);j++){
                var o=options[j];if(Btn(o["label"].S,enabled&&o["enabled"].B))SocialAction(o["id"].S);
            }
            GUILayout.EndHorizontal();
        }
        int favors=s["favors"].A.Count(f=>f["status"].S=="open");
        if(favors>0)T("尚记在心的人情 "+favors+" 件 · 可通过接济回报此前援助",small);
        foreach(var favor in s["favors"].A.Where(f=>f["status"].S=="open").Take(2))
            T((favor["owed_by"].S=="player"?"曾受对方援助 ":"曾援助对方 ")+Num(favor["amount"])+"银 · 尚未回报",small);
        foreach(var intro in s["introductions"].A.Where(i=>i["by"].I==SelectedPerson).Take(2)){
            if(Btn("引荐凭据："+intro["name"].S+" · 所知去向 "+Place(intro["place"].I)["name"].S)){
                int target=intro["target"].I;Later(()=>{TalkTo(target);conversationScroll=Vector2.zero;});
            }
        }
        if(Btn("告辞"))Later(Back);
        GUILayout.EndArea();
    }
}
}
