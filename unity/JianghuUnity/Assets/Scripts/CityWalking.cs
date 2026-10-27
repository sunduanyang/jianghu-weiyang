using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    const float CityWalkStepSeconds=.09f,CityWalkLandingSeconds=.025f;
    bool cityWalking,cityWalkCancelled,cityWalkRepath,cityStepAnimating,cityWalkKeyboard;
    Vector2Int cityWalkDestination,cityStepFrom,cityStepTo;
    float cityStepProgress,cityStepStartedAt;
    readonly List<Vector2Int> cityWalkRoute=new List<Vector2Int>();
    int cityWalkIndex;

    void BeginCityWalk(Vector2Int target,bool keyboard=false){
        cityWalkDestination=target;cityWalkRepath=true;cityWalkCancelled=false;cityWalkKeyboard=keyboard;
        if(cityWalking)return;
        cityWalking=true;StartCoroutine(FollowCityWalk());
    }
    void StopCityWalk(){if(cityWalking)cityWalkCancelled=true;}
    Vector2 CityHeroSurface(float tw,float th,Vector2 center)=>cityStepAnimating
        ?Vector2.Lerp(CitySurface(cityStepFrom,tw,th,center),CitySurface(cityStepTo,tw,th,center),cityStepProgress)
        :CitySurface(new Vector2Int(Hero["row"].I,Hero["col"].I),tw,th,center);
    Vector2 CityHeroOverview(float unit,Vector2 center)=>cityStepAnimating
        ?Vector2.Lerp(CityXY(cityStepFrom,unit,unit*.5f,center),CityXY(cityStepTo,unit,unit*.5f,center),cityStepProgress)
        :CityXY(new Vector2Int(Hero["row"].I,Hero["col"].I),unit,unit*.5f,center);

    IEnumerator CommitCityStep(int place,Vector2Int next,System.Action<J> done){
        J result=null;
        yield return Client.Request("/api/hero",Client.HeroBody("walk","place",place,"row",next.x,"col",next.y),j=>result=j);
        if(result!=null)Client.ApplyHeroStep(result);
        done(result);
    }

    IEnumerator FollowCityWalk(){
        int place=Hero["place"].I,month=W["month"].I;
        string database=W["database"].S,layout=City["layout_id"].S;
        var route=cityWalkRoute;route.Clear();cityWalkIndex=1;bool arrived=false;
        Client.Busy=true;Client.Status="行走中 · Esc 停止";
        try{
            while(!cityWalkCancelled&&CityMovementAllowed&&!cityLoading&&SelectedPlace==place
                &&Hero["place"].I==place&&W["month"].I==month&&W["database"].S==database&&City["layout_id"].S==layout){
                var from=new Vector2Int(Hero["row"].I,Hero["col"].I);
                if(cityWalkKeyboard){
                    var nextCell=from+cityHeldDirection;
                    if(cityHeldDirection==Vector2Int.zero||!CityInside(nextCell)||CityBlocked(CityTile(nextCell.x,nextCell.y))
                        ||CityStepCost(CityTile(nextCell.x,nextCell.y))>Hero["points"].I)break;
                    cityWalkDestination=nextCell;route.Clear();route.Add(from);route.Add(nextCell);cityWalkIndex=1;cityWalkRepath=false;
                    SelectCityCell(nextCell);
                }else if(cityWalkRepath){
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
                // Save the adjacent tile while its animation plays. The small
                // player reply replaces the old full-world refresh per step.
                StartCoroutine(CommitCityStep(place,next,j=>{result=j;committed=true;}));
                do{
                    cityStepProgress=Mathf.Clamp01((Time.realtimeSinceStartup-cityStepStartedAt)/CityWalkStepSeconds);
                    yield return null;
                }while(cityStepProgress<1||!committed||Time.realtimeSinceStartup-cityStepStartedAt<CityWalkStepSeconds+CityWalkLandingSeconds);
                if(result==null||Hero["place"].I!=place||Hero["row"].I!=next.x||Hero["col"].I!=next.y)break;
                cityStepAnimating=false;cityWalkIndex++;
                // Zero points is valid on the final tile; do not require another step.
                if(!cityWalkKeyboard&&!cityWalkRepath&&cityWalkIndex>=route.Count){arrived=true;break;}
            }
        }finally{
            cityStepAnimating=false;cityWalking=false;cityWalkKeyboard=false;Client.Busy=false;
            if(string.IsNullOrEmpty(Client.Error)&&Page==1&&CityLocal)
                Client.Status=arrived?"已抵达 "+CityCoordinate(new Vector2Int(Hero["row"].I,Hero["col"].I)):cityWalkCancelled?"已停止行走":"行走已中止";
            // Reconcile the rest of the world once, after walking. A newer hero
            // reply supersedes this snapshot if another walk starts meanwhile.
            Client.StartCoroutine(Client.Refresh());
        }
    }
}
}
