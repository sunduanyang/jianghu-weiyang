using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    const float CityWalkStepSeconds=.09f,CityWalkLandingSeconds=.025f;
    bool cityWalking,cityWalkCancelled,cityWalkRepath,cityStepAnimating;
    Vector2Int cityWalkDestination,cityStepFrom,cityStepTo;
    float cityStepProgress,cityStepStartedAt;
    readonly List<Vector2Int> cityWalkRoute=new List<Vector2Int>();
    int cityWalkIndex;

    // Quote the affordable prefix of the real route. The destination may be farther
    // than one monthly budget; every committed step still pays the backend's cost.
    int CityWalkThisMonthCost(Vector2Int target){
        int spend=0;var path=CityPreview(target);
        for(int i=1;i<path.Count;i++){
            int next=CityStepCost(CityTile(path[i].x,path[i].y));
            if(spend+next>Hero["points"].I)break;
            spend+=next;
        }
        return spend;
    }

    void BeginCityWalk(Vector2Int target){
        cityWalkDestination=target;cityWalkRepath=true;cityWalkCancelled=false;
        if(cityWalking)return;
        cityWalking=true;StartCoroutine(FollowCityWalk());
    }
    void StopCityWalk(){if(cityWalking)cityWalkCancelled=true;}

    IEnumerator CommitCityStep(int place,Vector2Int next,System.Action<J> done){
        J result=null;
        yield return Client.Request("/api/hero",Client.HeroBody("walk","place",place,"row",next.x,"col",next.y),j=>result=j);
        if(result!=null)Client.ApplyHeroStep(result);
        done(result);
    }

    // The backend still moves the hero one adjacent cell per request; the atlas
    // follows HeroTileFloat so the pennant glides along the brushed road.
    IEnumerator FollowCityWalk(){
        int place=Hero["place"].I,month=W["month"].I;
        string database=W["database"].S,layout=City["layout_id"].S;
        var route=cityWalkRoute;route.Clear();cityWalkIndex=1;bool arrived=false,budgetEnded=false;
        Client.Busy=true;Client.Status="行走中 · Esc 停止";
        try{
            while(!cityWalkCancelled&&CityMovementAllowed&&!cityLoading&&SelectedPlace==place
                &&Hero["place"].I==place&&W["month"].I==month&&W["database"].S==database&&City["layout_id"].S==layout){
                var from=new Vector2Int(Hero["row"].I,Hero["col"].I);
                if(cityWalkRepath){
                    EnsureCityRoutes();
                    if(!cityCosts.ContainsKey(cityWalkDestination)){Client.Status="行程已变化，请重新选择目的地";break;}
                    // Own the route: the hover preview is rebuilt as the hero moves.
                    route.Clear();route.AddRange(CityPreview(cityWalkDestination));cityWalkIndex=1;cityWalkRepath=false;
                }
                if(cityWalkIndex>=route.Count){arrived=true;break;}
                var next=route[cityWalkIndex];
                if(Mathf.Abs(next.x-from.x)+Mathf.Abs(next.y-from.y)!=1||CityBlocked(CityTile(next.x,next.y)))break;
                if(CityStepCost(CityTile(next.x,next.y))>Hero["points"].I){budgetEnded=true;break;}
                cityStepFrom=from;cityStepTo=next;cityStepProgress=0;cityStepAnimating=true;cityStepStartedAt=Time.realtimeSinceStartup;
                J result=null;bool committed=false;
                // Save the adjacent cell while its animation plays. The small
                // player reply replaces the old full-world refresh per step.
                StartCoroutine(CommitCityStep(place,next,j=>{result=j;committed=true;}));
                do{
                    cityStepProgress=Mathf.Clamp01((Time.realtimeSinceStartup-cityStepStartedAt)/CityWalkStepSeconds);
                    yield return null;
                }while(cityStepProgress<1||!committed||Time.realtimeSinceStartup-cityStepStartedAt<CityWalkStepSeconds+CityWalkLandingSeconds);
                if(result==null||Hero["place"].I!=place||Hero["row"].I!=next.x||Hero["col"].I!=next.y)break;
                cityStepAnimating=false;cityWalkIndex++;
                // Zero points is valid on the final cell; do not require another step.
                if(!cityWalkRepath&&cityWalkIndex>=route.Count){arrived=true;break;}
                if(!cityWalkRepath&&CityStepCost(CityTile(route[cityWalkIndex].x,route[cityWalkIndex].y))>Hero["points"].I){budgetEnded=true;break;}
                // A response may arrive after Update; give stop/reroute input a
                // frame to observe the landing before submitting the next step.
                yield return null;
            }
        }finally{
            cityStepAnimating=false;cityWalking=false;Client.Busy=false;
            if(string.IsNullOrEmpty(Client.Error)&&CurrentStage==Stage.City&&CityLocal)
                Client.Status=arrived?"已抵达 "+CityPlaceLabel(HeroTile):budgetEnded?"本月先行至此，过月后可继续前往 "+CityPlaceLabel(cityWalkDestination):cityWalkCancelled?"已停止行走":"行走已中止";
            // Reconcile the rest of the world once, after walking. A newer hero
            // reply supersedes this snapshot if another walk starts meanwhile.
            Client.StartCoroutine(Client.Refresh());
        }
    }
}
}
