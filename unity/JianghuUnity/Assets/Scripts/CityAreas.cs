using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    J[,] cityAreaProperties;
    static List<J> CitySitesFor(J data)=>data["settlements"].Has?data["settlements"].A:data["nodes"].A;
    List<J> CitySites=>CitySitesFor(City);
    void IndexCityAreas(){
        cityAreaProperties=null;if(!City["cell_areas"].Has)return;
        var areas=City["areas"].A;cityAreaProperties=new J[citySize,citySize];
        for(int row=0;row<citySize;row++)for(int col=0;col<citySize;col++)cityAreaProperties[row,col]=areas[City["cell_areas"][row][col].I];
    }
    J CityAreaAt(Vector2Int at)=>cityAreaProperties!=null&&CityInside(at)?cityAreaProperties[at.x,at.y]:new J();
    string CityAreaName(Vector2Int at){var area=CityAreaAt(at);return area.Has?area["name"].S:CityTerrain(CityTile(at.x,at.y));}
    static bool CityHasService(J site,string service)=>site["kind"].S==service||site["services"].A.Any(s=>s.S==service);
}
}
