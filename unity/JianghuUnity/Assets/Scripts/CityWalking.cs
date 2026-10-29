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
        var route=cityWalkRoute;route.Clear();cityWalkIndex=1;bool arrived=false;
        Client.Busy=true;Client.Status="行走中 · Esc 停止";
        try{
            while(!cityWalkCancelled&&CityMovementAllowed&&!cityLoading&&SelectedPlace==place
                &&Hero["place"].I==place&&W["month"].I==month&&W["database"].S==database&&City["layout_id"].S==layout){
                var from=new Vector2Int(Hero["row"].I,Hero["col"].I);
                if(cityWalkRepath){
                    EnsureCityRoutes();
                    if(!cityCosts.TryGetValue(cityWalkDestination,out int cost)||cost>Hero["points"].I){Client.Status="行程已变化，请重新选择目的地";break;}
                    // Own the route: the hover preview is rebuilt as the hero moves.
                    route.Clear();route.AddRange(CityPreview(cityWalkDestination));cityWalkIndex=1;cityWalkRepath=false;
                }
                if(cityWalkIndex>=route.Count){arrived=true;break;}
                var next=route[cityWalkIndex];
                if(Mathf.Abs(next.x-from.x)+Mathf.Abs(next.y-from.y)!=1||CityBlocked(CityTile(next.x,next.y)))break;
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
            }
        }finally{
            cityStepAnimating=false;cityWalking=false;Client.Busy=false;
            if(string.IsNullOrEmpty(Client.Error)&&CurrentStage==Stage.City&&CityLocal)
                Client.Status=arrived?"已抵达 "+CityPlaceLabel(HeroTile):cityWalkCancelled?"已停止行走":"行走已中止";
            // Reconcile the rest of the world once, after walking. A newer hero
            // reply supersedes this snapshot if another walk starts meanwhile.
            Client.StartCoroutine(Client.Refresh());
        }
    }
}
}
