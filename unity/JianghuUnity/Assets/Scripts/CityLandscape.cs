using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    readonly Dictionary<string,Texture2D> citySettlements=new Dictionary<string,Texture2D>();
    struct CityGroundTile {public Vector2Int Corner,End;public int Extent;public string Asset,BaseAsset;}
    readonly List<CityGroundTile> cityTerrainTiles=new List<CityGroundTile>();
    CityGroundTile[,] cityGroundCells;
    Texture2D CitySettlementArt(string key){
        if(key!="01-village-1cell"&&key!="02-town-4cell"&&key!="03-city-9cell")return null;
        if(!citySettlements.TryGetValue(key,out var t)){citySyncLoads++;t=Resources.Load<Texture2D>("CitySettlements/"+key);citySettlements[key]=t;}return t;
    }
    void IndexCityTerrainTiles(){
        cityTerrainTiles.Clear();cityGroundCells=new CityGroundTile[citySize,citySize];
        foreach(var t in City["terrain_tiles"].A){int extent=t["size"].I;var corner=new Vector2Int(t["row"].I,t["col"].I);
            var tile=new CityGroundTile{Corner=corner,End=corner+new Vector2Int(extent-1,extent-1),Extent=extent,Asset=CityGroundAsset(City,t),BaseAsset=CityBaseAsset(City["rows"][corner.x].S[corner.y],City["theme"].S)};
            cityTerrainTiles.Add(tile);for(int r=0;r<extent;r++)for(int c=0;c<extent;c++)cityGroundCells[corner.x+r,corner.y+c]=tile;
        }
        cityTerrainTiles.Sort((a,b)=>{int depth=(a.End.x+a.End.y).CompareTo(b.End.x+b.End.y);return depth!=0?depth:a.End.x.CompareTo(b.End.x);});
    }
    void DrawCitySettlement(J node,Vector2 p,float tw,Color tint){
        var art=CitySettlementArt(node["settlement_asset"].S);if(art==null)return;
        var old=GUI.color;GUI.color=tint;float scale=tw*CityLotSize(node)/128;
        GUI.DrawTexture(new Rect(p.x-128*scale,p.y-171*scale,256*scale,256*scale),art);GUI.color=old;
    }
    void DrawCityLandscape(List<Vector2Int> cells,float tw,float th,Vector2 center,Rect bounds){
        // Faces belong to individual cells, even when their artwork spans 2x2.
        // Drawing a whole lot's faces at its front corner covered neighboring
        // low ground that had already been painted on the same diagonal.
        foreach(var cell in cells){
            var tile=cityGroundCells[cell.x,cell.y];int extent=tile.Extent;
            var surface=CityGroundSurface(cell,tw,th,center);
            if(CityGroundVisible(surface,tw,1,bounds)){
                DrawCityBlock(cell,surface,tw,th,Color.white);
                // Give every cell a real top before adding the unbroken scenery.
                CityTexture(extent==1?tile.Asset:tile.BaseAsset,surface,tw,Color.white);
            }
            if(cell!=tile.End)continue;
            var p=CityGroundSurface(tile.Corner,tw,th,center)+new Vector2(0,(extent-1)*th);
            if(!CityGroundVisible(p,tw,extent,bounds))continue;
            if(extent>1)CityTexture(tile.Asset,p,tw*extent,Color.white);
            if(!cityLots.ContainsKey(tile.Corner))CityDiamond(p,tw*extent*.995f,th*extent*.995f,new Color(.09f,.12f,.07f,.52f),.8f);
        }
        foreach(var node in cityBuildingNodes)DrawCitySettlement(node,CityBuildingPoint(node,tw,th,center),tw,Color.white);
    }
    bool CityGroundVisible(Vector2 p,float tw,int extent,Rect bounds)=>p.x+tw*extent>=0&&p.x-tw*extent<=bounds.width&&p.y+tw*(extent*.5f+(cityMaxLevel+2)*CityRise)>=0&&p.y-tw*extent*1.5f<=bounds.height;
    void DrawCityRoads(List<Vector2Int> cells,float tw,float th,Vector2 center){
        foreach(var q in cells){
            if(cityLots.ContainsKey(q))continue;
            var p=CitySurface(q,tw,th,center);
            DrawCityStairs(q,p,tw,th);
            if(cityReach&&CityLocal&&cityCosts.TryGetValue(q,out int cost)&&cost<=Hero["points"].I)CityDiamond(p,tw*.94f,th*.94f,new Color(.69f,.90f,.53f,.65f));
        }
    }
}
}
