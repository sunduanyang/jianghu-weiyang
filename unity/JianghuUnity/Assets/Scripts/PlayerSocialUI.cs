using System;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    Vector2 conversationScroll;
    float nextConversationPoll;
    bool conversationPolling;
    int conversationPane,persuasionRequestId,observedConversation,socialNetworkEpoch;

    J InteractionSocial {
        get {
            J cached=InteractionDataReady?interactionData["social"]:new J();
            J current=CharacterReady?Person["player_social"]:new J();
            return current.Has&&(!cached.Has||current["revision"].I>=cached["revision"].I)?current:cached;
        }
    }
    void ResetInteractionConversation(){
        socialNetworkEpoch++;conversationPane=persuasionRequestId=observedConversation=0;
        conversationScroll=Vector2.zero;nextConversationPoll=0;
    }
    void SelectLatestPersuasion(){
        string action=InteractionConversationOpen?interactionChoice.Substring(7):"";
        var refusal=InteractionSocial["requests"].A.LastOrDefault(r=>r["action"].S==action&&(r["can_persuade"].B||r["can_retry_options"].B));
        if(refusal!=null){persuasionRequestId=refusal["id"].I;conversationPane=1;conversationScroll=Vector2.zero;}
    }
    void ObserveInteractionConversation(J social){
        var latest=social["requests"].A.LastOrDefault();if(latest==null)return;
        int completed=latest["status"].S=="pending"?0:latest["id"].I;
        if(completed<=observedConversation)return;
        observedConversation=completed;
        if(!InteractionConversationOpen||latest["action"].S!=interactionChoice.Substring(7))return;
        if(latest["can_persuade"].B||latest["can_retry_options"].B){persuasionRequestId=completed;conversationPane=1;conversationScroll=Vector2.zero;}
        else{conversationPane=0;conversationScroll.y=float.MaxValue;}
    }
    void ApplySocialSnapshot(int id,J social){
        if(FocusPersonId!=id||SelectedPerson!=id)return;
        if(CharacterReady&&social["revision"].I>=Person["player_social"]["revision"].I)Person["player_social"]=social;
        if(InteractionDataReady&&social["revision"].I>=interactionData["social"]["revision"].I)interactionData["social"]=social;
        ObserveInteractionConversation(social);
    }
    void UpdateSocialDialogue(){
        if((!DialogueVisible&&!TopIs(ViewKind.Interaction))||!CharacterReady||!Person.Has||Client.Busy||conversationPolling||Time.unscaledTime<nextConversationPoll)return;
        nextConversationPoll=Time.unscaledTime+1.2f;
        int id=FocusPersonId;conversationPolling=true;
        StartCoroutine(PollSocialPerson(id));
    }
    System.Collections.IEnumerator PollSocialPerson(int id){
        int epoch=socialNetworkEpoch;
        yield return Client.Request("/api/npc/"+id,null,j=>{
            if(epoch!=socialNetworkEpoch||FocusPersonId!=id||SelectedPerson!=id)return;
            if(j["player_social"]["revision"].I<InteractionSocial["revision"].I)return;
            bool wasPending=InteractionSocial["pending"].B;
            Person=j;ApplySocialSnapshot(id,j["player_social"]);
            if(wasPending&&!j["player_social"]["pending"].B){StartCoroutine(Client.Refresh());if(TopIs(ViewKind.Interaction))LoadInteraction();}
        },false);
        conversationPolling=false;
    }
    public void SocialAction(string action){
        if(Client.Busy||!CharacterReady)return;
        int id=FocusPersonId,epoch=++socialNetworkEpoch;
        Client.Act("/api/social",J.O("npc_id",id,"action",action,"revision",InteractionSocial["revision"]),j=>{
            if(epoch!=socialNetworkEpoch||FocusPersonId!=id)return;
            ApplySocialSnapshot(id,j);talk="";nextConversationPoll=0;conversationPane=0;conversationScroll.y=float.MaxValue;
        });
    }
    public void Persuade(int requestId,string optionId=null){
        if(Client.Busy||!CharacterReady)return;
        int id=FocusPersonId,epoch=++socialNetworkEpoch;
        J body=J.O("npc_id",id,"mode",optionId==null?"options":"persuade","request_id",requestId,"revision",InteractionSocial["revision"]);
        if(optionId!=null)body["option_id"]=new J(optionId);
        Client.Act("/api/social",body,j=>{
            if(epoch!=socialNetworkEpoch||FocusPersonId!=id)return;
            ApplySocialSnapshot(id,j);nextConversationPoll=0;
            conversationPane=optionId==null?1:0;conversationScroll=optionId==null?Vector2.zero:new Vector2(0,float.MaxValue);
        });
    }
    void DrawSocialConversation(Rect area)=>DrawInteractionConversation(area);
    void DrawInteractionConversation(Rect area){
        Fill(area,new Color(.065f,.095f,.095f));Outline(area,hairline);
        J social=InteractionSocial,action=InteractionAction;
        string label=action.Has?action["label"].S:"闲叙近况";
        string headingLabel=label;
        var shownRefusal=conversationPane==1?InteractionPersuasionRequest(social):null;
        if(shownRefusal!=null){var topic=interactionData["conversations"].A.FirstOrDefault(t=>t["action"].S==shownRefusal["action"].S);if(topic!=null)headingLabel=topic["label"].S;}
        if(GUI.Button(new Rect(area.x+12,area.y+10,122,30),"‹ 返回分类",chip))Later(()=>interactionChoice="");
        GUI.Label(new Rect(area.x+150,area.y+11,area.width-168,28),headingLabel+" / "+CharacterName,sub);
        string[] tabs={"交谈记录","五种性格说服","人情与引荐"};float width=(area.width-24)/3;
        for(int i=0;i<tabs.Length;i++){
            int pane=i;
            if(GUI.Button(new Rect(area.x+12+i*width,area.y+48,width-4,30),tabs[i],conversationPane==i?selected:chip))Later(()=>{conversationPane=pane;conversationScroll=pane==0?new Vector2(0,float.MaxValue):Vector2.zero;});
        }
        float footerHeight=conversationPane==1?36:90;
        Rect body=new Rect(area.x+16,area.y+88,area.width-32,area.height-96-footerHeight);
        GUILayout.BeginArea(body);
        conversationScroll=GUILayout.BeginScrollView(conversationScroll);
        if(conversationPane==1)DrawInteractionPersuasion(social);
        else if(conversationPane==2)DrawInteractionSocialTies(social);
        else DrawInteractionTranscript(social);
        GUILayout.EndScrollView();GUILayout.EndArea();
        Rect footer=new Rect(area.x+16,area.yMax-footerHeight,area.width-32,footerHeight-6);
        GUILayout.BeginArea(footer);
        bool model=W["ai"]["config"]["enabled"].B;
        string why=!Paused?"请先暂停世界推演":social["reason"].S;
        string hint=social["pending"].B?(W["ai"]["current_stage"].S.Length>0?W["ai"]["current_stage"].S:"对方正在斟酌你的话……"):why.Length>0?why:"每次回应耗1行动力；可重复交谈，不占用对方时间。";
        if(conversationPane==1){
            if(!model){if(Btn("启用本地模型以继续说服"))Later(()=>OpenSystem(SystemTab.Model));}
            else T(Client.Error.Length>0?Client.Error:hint,small);
            GUILayout.EndArea();return;
        }
        T(Client.Error.Length>0?Client.Error:hint,small);
        GUILayout.BeginHorizontal();
        var topics=interactionData["conversations"].A;
        if(topics.Count>0){
            string chosen=Choose(label,topics.Select(t=>t["label"].S).ToArray(),"商谈话题");
            if(chosen!=label){string key=topics.First(t=>t["label"].S==chosen)["id"].S;Later(()=>ChooseInteraction(key));}
        }
        if(Btn(social["pending"].B?"等候回应…":"提出"+label+" · 1行动力",model&&Paused&&CharacterReady&&social.Has&&!social["pending"].B&&why.Length==0))Later(()=>SocialAction(action.Has?action["action"].S:"talk"));
        GUILayout.EndHorizontal();
        if(!model){if(Btn("启用本地模型以交谈与说服"))Later(()=>OpenSystem(SystemTab.Model));}
        GUILayout.EndArea();
    }
    void DrawInteractionTranscript(J social){
        if(social["requests"].A.Count==0){T("你走上前，准备与对方说话。",sub);T("选择下方话题并提出请求，回应与结果会留在这里。对方拒绝时，可以尝试五种性格的说服。",text);}
        foreach(var request in social["requests"].A){
            GUILayout.BeginVertical(card);
            T("你"+(request["persuasion_label"].Has?"（"+request["persuasion_label"].S+"）":"")+"："+request["text"].S,text);
            if(request["status"].S=="pending")T("对方正在斟酌你的话……",sub);
            else if(request["status"].S=="completed"){
                T(CharacterName+"："+request["reply"].S,sub);T(request["outcome"].S,small);
            }else T(request["error"].S,small);
            if(request["can_persuade"].B||request["can_retry_options"].B){
                int requestId=request["id"].I;
                if(Btn("选择五种性格说服  ›"))Later(()=>{persuasionRequestId=requestId;conversationPane=1;conversationScroll=Vector2.zero;});
            }
            GUILayout.EndVertical();GUILayout.Space(8);
        }
    }
    J InteractionPersuasionRequest(J social)=>social["requests"].A.FirstOrDefault(r=>r["id"].I==persuasionRequestId)
        ??social["requests"].A.LastOrDefault(r=>r["persuasion_options"].A.Count>0||r["can_retry_options"].B);
    void DrawInteractionPersuasion(J social){
        var request=InteractionPersuasionRequest(social);
        if(request==null){T("对方拒绝请求后，可在这里选择一种性格继续说服。",sub);T("刚正 · 仁善 · 中庸 · 叛逆 · 唯我",text);T("台词会结合原请求与对方说出的拒绝理由拟定，每种性格都可能成功或被拒绝。",small);return;}
        T("你："+request["text"].S,small);T(CharacterName+"："+request["reply"].S,text);
        if(request["can_persuade"].B)T("选择一句继续说服 · 回应耗1行动力 · 对方仍可拒绝",small);
        if(social["stance"].Has)T("对方处世立场："+social["stance"]["label"].S+"。"+social["stance"]["text"].S+"与之相合的说法较易入耳，但不保证成功。",small);
        else if(request["can_retry_options"].B){
            T(request["options_error"].S,small);
            if(Btn("重新拟定五种说服方式",Paused&&W["ai"]["config"]["enabled"].B))Later(()=>Persuade(request["id"].I));
        }else T(social["reason"].S.Length>0?social["reason"].S:request["appeal_request_id"].Has?"这次说服已经提出，后续回应保留在交谈记录中。":"这次说服现已不可提出，可回到交谈记录查看结果。",small);
        var style=new GUIStyle(rowStyle){wordWrap=true,fontSize=16,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(12,12,8,8)};
        foreach(var option in request["persuasion_options"].A){
            bool selectedOption=!request["selected_option"].Has||request["selected_option"].S==option["id"].S;
            bool fits=social["stance"]["key"].S==option["id"].S;
            string line="【"+option["label"].S+(fits?" · 相合":"")+"】 "+option["text"].S;
            float height=Mathf.Max(50,style.CalcHeight(new GUIContent(line),InteractionPanel.width-100));
            bool previous=GUI.enabled;GUI.enabled=previous&&request["can_persuade"].B&&selectedOption&&Paused&&!Client.Busy&&W["ai"]["config"]["enabled"].B;
            if(GUILayout.Button(line,style,GUILayout.MinHeight(height))){int requestId=request["id"].I;string optionId=option["id"].S;Later(()=>Persuade(requestId,optionId));}
            GUI.enabled=previous;GUILayout.Space(5);
        }
    }
    void DrawInteractionSocialTies(J social){
        var favors=social["favors"].A.Where(f=>f["status"].S=="open").ToList();
        T("人情往来 · 待回报 "+favors.Count+" 件",sub);
        if(favors.Count==0)T("你们之间暂无待回报的援助。",small);
        foreach(var favor in social["favors"].A){T((favor["owed_by"].S=="player"?"曾受对方援助 ":"曾援助对方 ")+Num(favor["amount"])+"银 · "+(favor["status"].S=="open"?"尚未回报":"已经回报"),text);}
        GUILayout.Space(16);T("引荐凭据",sub);
        var introductions=social["introductions"].A.Where(i=>i["by"].I==FocusPersonId).ToList();
        if(introductions.Count==0)T("对方尚未为你引荐熟人。",small);
        foreach(var intro in introductions){
            if(Btn(intro["name"].S+" · 最近所知在 "+Place(intro["place"].I)["name"].S+"  ›")){int target=intro["target"].I;Later(()=>ViewPerson(target));}
        }
    }
}
}
