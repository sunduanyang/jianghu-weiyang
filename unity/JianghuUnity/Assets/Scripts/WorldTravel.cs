using System.Collections;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    int worldTravelTarget=-1,worldTravelRequest;
    J worldTravelPlan=new J();
    string worldTravelError="";
    Vector2 worldTravelScroll;
    bool worldTravelMoving,worldTravelWaitingMonth;
    bool WorldTravelOpen=>worldTravelTarget>=0;
    Rect WorldTravelBounds=>new Rect(540,300,600,360);

    void ActivateWorldMapPlace(int place){
        if(DeferWhileDrawing(()=>ActivateWorldMapPlace(place)))return;
        if(CurrentStage!=Stage.World||ViewOpen||Client.Busy)return;
        SelectMapPlace(place);
        if(Hero["created"].B&&Hero["place"].I==place){
            cityMode=CityMode.Atlas;GoStage(Stage.City);return;
        }
        CloseTransient();worldTravelTarget=place;worldTravelPlan=new J();worldTravelError="";worldTravelScroll=Vector2.zero;
        StartCoroutine(LoadWorldTravelPlan(place,++worldTravelRequest));
    }
    IEnumerator LoadWorldTravelPlan(int target,int request){
        bool received=false;
        yield return Client.Request("/api/hero/travel?place="+target,null,j=>{
            received=true;if(request==worldTravelRequest&&target==worldTravelTarget)worldTravelPlan=j;
        });
        if(!received&&request==worldTravelRequest)worldTravelError=Client.Error;
    }
    bool WorldTravelPlanCurrent=>worldTravelPlan.Has&&worldTravelPlan["origin"].I==Hero["place"].I
        &&worldTravelPlan["revision"].I==Hero["revision"].I&&worldTravelPlan["month"].I==W["month"].I;
    void CloseWorldTravel(){worldTravelTarget=-1;worldTravelRequest++;worldTravelPlan=new J();worldTravelError="";worldTravelMoving=worldTravelWaitingMonth=false;dragging=false;mapPressedPlace=mapLastClickPlace=-1;}
    bool WorldTravelJourneyCurrent(int target,int request)=>worldTravelTarget==target&&worldTravelRequest==request;
    void ConfirmWorldTravel(){
        if(Client.Busy||worldTravelMoving||worldTravelWaitingMonth||!Paused||!WorldTravelPlanCurrent||!worldTravelPlan["enabled"].B)return;
        worldTravelMoving=true;worldTravelError="";
        StartCoroutine(RunWorldTravel(worldTravelTarget,worldTravelRequest));
    }
    IEnumerator RunWorldTravel(int target,int request){
        while(WorldTravelJourneyCurrent(target,request)){
            if(Hero["place"].I==target){
                CloseWorldTravel();SelectedPlace=target;cityMode=CityMode.Atlas;GoStage(Stage.City);yield break;
            }
            if(!Paused){worldTravelError="请先暂停世界推演并结束切磋。";break;}
            if(!WorldTravelPlanCurrent)yield return LoadWorldTravelPlan(target,request);
            if(!WorldTravelJourneyCurrent(target,request))yield break;
            if(!WorldTravelPlanCurrent||!worldTravelPlan["enabled"].B)break;
            J leg=worldTravelPlan["legs"].A[0];bool received=false;
            // Each real edge is saved separately; use the revision and month just quoted.
            J body=J.O("action","travel","place",leg["place"],"revision",worldTravelPlan["revision"],"month",worldTravelPlan["month"]);
            yield return Client.Action("/api/hero",body,j=>{received=true;Client.ApplyHeroStep(j);});
            if(!WorldTravelJourneyCurrent(target,request))yield break;
            if(!received){worldTravelError=Client.Error;break;}
            SelectedPlace=Hero["place"].I;Client.Status="已抵达"+Hero["place_name"].S;
            worldTravelPlan=new J();yield return new WaitForSecondsRealtime(.35f);
        }
        if(WorldTravelJourneyCurrent(target,request))worldTravelMoving=false;
    }
    void AdvanceWorldTravelMonth(){
        if(Client.Busy||worldTravelMoving||worldTravelWaitingMonth||!Paused||!WorldTravelPlanCurrent||!worldTravelPlan["needs_month"].B)return;
        worldTravelWaitingMonth=true;worldTravelError="";
        StartCoroutine(AdvanceWorldTravelMonthAndResume(worldTravelTarget,worldTravelRequest));
    }
    IEnumerator AdvanceWorldTravelMonthAndResume(int target,int request){
        int month=W["month"].I;bool received=false;
        yield return Client.Action("/api/hero",Client.HeroBody("end_turn"),j=>{received=true;Client.ApplyHeroStep(j);});
        if(!WorldTravelJourneyCurrent(target,request))yield break;
        if(received){
            // Model-enabled monthly turns complete asynchronously through the existing job.
            while(WorldTravelJourneyCurrent(target,request)&&W["month"].I==month&&W["job"]["remaining"].I>0){
                yield return new WaitForSecondsRealtime(.25f);if(!Client.Busy)yield return Client.Refresh();
            }
        }
        if(!WorldTravelJourneyCurrent(target,request))yield break;
        if(!received||W["month"].I!=month+1||!Paused){worldTravelWaitingMonth=false;worldTravelError=Client.Error.Length>0?Client.Error:"过月尚未完成，请稍后重新查看行程。";yield break;}
        worldTravelPlan=new J();yield return LoadWorldTravelPlan(target,request);
        if(WorldTravelJourneyCurrent(target,request)){worldTravelWaitingMonth=false;ConfirmWorldTravel();}
    }
    void DrawWorldTravel(){
        if(!WorldTravelOpen)return;
        Fill(new Rect(0,0,1680,960),new Color(0,0,0,.56f));
        Fill(WorldTravelBounds,new Color(.055f,.075f,.07f,1));Outline(WorldTravelBounds,brass);
        GUI.ModalWindow(19275,WorldTravelBounds,DrawWorldTravelWindow,"",card);
    }
    void DrawWorldTravelWindow(int id){
        GUILayout.BeginArea(new Rect(24,18,552,324));
        H("前往 · "+Place(worldTravelTarget)["name"].S);
        worldTravelScroll=GUILayout.BeginScrollView(worldTravelScroll,GUILayout.Height(220));
        T("当前所在 · "+Hero["place_name"].S,small);
        if(worldTravelWaitingMonth)T("正在过月，恢复精力后继续前往目标…",sub);
        else if(!worldTravelPlan.Has)T(worldTravelError.Length>0?worldTravelError:worldTravelMoving?"正在逐区前进…":"正在查看路线与精力…",small);
        else{
            T(worldTravelMoving?"正在逐区前往目标…":"沿途逐区移动",sub);
            if(worldTravelPlan["legs"].A.Count>0){
                T(string.Join(" → ",worldTravelPlan["stops"].A.Select(p=>p.S)),text);
                GUILayout.Space(8);
                T("全程所需精力："+worldTravelPlan["points"].S+"  /  当前剩余："+Hero["points"].S,sub);
                if(!worldTravelPlan["can_finish"].B&&worldTravelPlan["enabled"].B)
                    T("本月可先到"+Place(worldTravelPlan["travel_place"].I)["name"].S+"，消耗"+worldTravelPlan["travel_points"].S+"精力。",small);
                T("按实际经过的区域扣除精力，抵达后进入舆图。",small);
            }
            if(!WorldTravelPlanCurrent)T("行程已变化，请重新查看消耗。",small);
            else if(!Paused)T(W["battle_active"].B?"请先结束切磋，再安排行程。":"请先暂停世界推演。",small);
            else if(!worldTravelPlan["enabled"].B)T(worldTravelPlan["reason"].S,small);
            if(worldTravelError.Length>0)T(worldTravelError,small);
        }
        GUILayout.EndScrollView();GUILayout.FlexibleSpace();GUILayout.BeginHorizontal();
        if(Btn(worldTravelMoving?"停止行程":"取消"))Later(CloseWorldTravel);
        bool idle=!worldTravelMoving&&!worldTravelWaitingMonth&&!Client.Busy;
        if(idle&&(worldTravelError.Length>0||worldTravelPlan.Has&&!WorldTravelPlanCurrent)&&Btn("重新查看"))Later(()=>ActivateWorldMapPlace(worldTravelTarget));
        if(WorldTravelPlanCurrent&&worldTravelPlan["needs_month"].B){
            if(Btn("过月",idle&&Paused))Later(AdvanceWorldTravelMonth);
        }else if(Btn("确认移动",idle&&Paused&&WorldTravelPlanCurrent&&worldTravelPlan["enabled"].B))Later(ConfirmWorldTravel);
        GUILayout.EndHorizontal();GUILayout.EndArea();
    }
}
}
