using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    readonly Dictionary<string,J> cityViewCache=new Dictionary<string,J>();
    readonly Queue<string> cityViewOrder=new Queue<string>();
    bool cityWarmupStarted,cityWarmupComplete;
    int cityIndexBuilds,cityRouteBuilds,citySyncLoads,cityFaceBuilds;
    double cityLastLoadMs,cityLastPrepareMs;
    bool cityLastViewCacheHit;
    string CityViewKey(int place)=>W["database"].S+"/"+W["seed"].S+"/"+W["month"].S+"/"+W["events"].S+"/"+place;
    void UpdateCityWarmup(){if(cityWarmupStarted)return;cityWarmupStarted=true;StartCoroutine(WarmCityResources());}
    IEnumerator WarmCityResources(){
        var pack=Resources.Load<TextAsset>("CityBiomes/art-pack");if(pack!=null)foreach(var entry in J.Parse(pack.text)["assets"].A)if(!CityTextureReady(entry["id"].S,false))yield return LoadCityTexture(entry["id"].S,false);
        foreach(var key in new[]{"01-village-1cell","02-town-4cell","03-city-9cell"})if(!CityTextureReady(key,true))yield return LoadCityTexture(key,true);
        for(int steps=1;steps<=11;steps++){CityFace(steps,false);CityFace(steps,true);yield return null;}
        cityWarmupComplete=true;
    }
    bool CityTextureReady(string key,bool settlement)=>(settlement?citySettlements:cityArt).TryGetValue(key,out var texture)&&texture!=null;
    IEnumerator LoadCityTexture(string key,bool settlement){
        var cache=settlement?citySettlements:cityArt;if(cache.TryGetValue(key,out var texture)&&texture!=null)yield break;
        var request=Resources.LoadAsync<Texture2D>((settlement?"CitySettlements/":"CityBiomes/")+key);yield return request;
        cache[key]=request.asset as Texture2D;
    }
    static string CityGroundAsset(J city,J tile){
        if(city["version"].I<5&&tile["size"].I==1&&city["rows"][tile["row"].I].S[tile["col"].I]=='+'){
            string theme=city["theme"].S;return theme=="oasis"||theme=="highland"||theme=="frontier"?"07-dryland":"01-open-plains";
        }
        return tile["asset"].S;
    }
    static string CityBaseAsset(char terrain,string theme){
        bool dry=theme=="oasis"||theme=="highland"||theme=="frontier";
        return terrain=='~'||terrain=='='||terrain=='s'?"14-shallows":terrain=='^'?"16-low-hills":terrain=='t'?"03-woodland":terrain==','?"08-farmland":dry?"07-dryland":"01-open-plains";
    }
    IEnumerator PrepareCityResources(J data){
        var terrain=new HashSet<string>();foreach(var tile in data["terrain_tiles"].A){terrain.Add(CityGroundAsset(data,tile));if(tile["size"].I>1)terrain.Add(CityBaseAsset(data["rows"][tile["row"].I].S[tile["col"].I],data["theme"].S));}
        foreach(var key in terrain)if(!CityTextureReady(key,false))yield return LoadCityTexture(key,false);
        var buildings=new HashSet<string>();foreach(var node in CitySitesFor(data))buildings.Add(node["settlement_asset"].S);
        foreach(var key in buildings)if(!CityTextureReady(key,true))yield return LoadCityTexture(key,true);
        int max=0;foreach(var row in data["elevations"].A)foreach(var height in row.A)max=Mathf.Max(max,height.I);
        for(int steps=1;steps<=max+2;steps++){
            if(cityFaces.ContainsKey(steps*2)&&cityFaces.ContainsKey(steps*2+1))continue;
            CityFace(steps,false);CityFace(steps,true);yield return null;
        }
    }
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
        double prepare=Time.realtimeSinceStartupAsDouble;yield return PrepareCityResources(data);
        if(request!=cityRequest)yield break;
        cityLastPrepareMs=(Time.realtimeSinceStartupAsDouble-prepare)*1000;
        bool changed=!City.Has||City["place"].I!=data["place"].I;City=data;
        if(changed){SelectCityCell(new Vector2Int(-1,-1));originRow=originCol=-1;cityZoom=2.4f;cityPan=new Vector2(0,35);cityKind="全部聚落";cityLastClickAt=-10;}
        EnsureCityIndex();EnsureCityRoutes();cityLoading=false;cityPointerDown=false;cityHover=new Vector2Int(-1,-1);
        cityLastLoadMs=(Time.realtimeSinceStartupAsDouble-started)*1000;
    }
}
}
