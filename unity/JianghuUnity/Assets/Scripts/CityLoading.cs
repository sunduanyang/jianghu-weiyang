using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    readonly Dictionary<string,J> cityViewCache=new Dictionary<string,J>();
    readonly Queue<string> cityViewOrder=new Queue<string>();
    int cityIndexBuilds,cityRouteBuilds;
    double cityLastLoadMs;
    bool cityLastViewCacheHit;
    string CityViewKey(int place)=>W["database"].S+"/"+W["seed"].S+"/"+W["month"].S+"/"+W["events"].S+"/"+place;
    IEnumerator LoadCityRoutine(int place){
        int request=++cityRequest;SelectedPlace=place;cityLoading=true;double started=Time.realtimeSinceStartupAsDouble;
        string key=CityViewKey(place);cityLastViewCacheHit=cityViewCache.TryGetValue(key,out var data);
        if(!cityLastViewCacheHit){
            yield return Client.Request("/api/city?place="+place,null,j=>data=j);
            if(data==null){if(request==cityRequest)cityLoading=false;yield break;}
            if(!cityViewCache.ContainsKey(key))cityViewOrder.Enqueue(key);cityViewCache[key]=data;
            while(cityViewOrder.Count>8)cityViewCache.Remove(cityViewOrder.Dequeue());
        }
        if(request!=cityRequest)yield break;
        bool changed=!City.Has||City["place"].I!=data["place"].I;City=data;
        EnsureCityIndex();EnsureCityRoutes();if(changed||!CitySettlement(cityScrollSite).Has)CityAtlasReset();cityLoading=false;
        cityLastLoadMs=(Time.realtimeSinceStartupAsDouble-started)*1000;
    }
}
}
