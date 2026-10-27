using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    J cityCliffSavedView;
    Texture2D cityCliffSavedArt,cityCliffProbeArt;
    float cityCliffSavedZoom;
    Vector2 cityCliffSavedPan;
    bool cityCliffSavedLabels,cityCliffSavedGrid,cityCliffSavedReach;

    // Native pixel regression: the near ground must cover the far cliff's
    // lower edge, including the neighbor interleaved with a whole 2x2 lot.
    public void CityBeginCliffPixelFixture(){
        cityCliffSavedView=City;cityCliffSavedZoom=cityZoom;cityCliffSavedPan=cityPan;
        cityCliffSavedLabels=cityLabels;cityCliffSavedGrid=cityGrid;cityCliffSavedReach=cityReach;
        cityCliffSavedArt=cityArt["01-open-plains"];
        var pixels=new Color32[512*512];
        for(int y=0;y<512;y++)for(int x=0;x<512;x++)if(Mathf.Abs(x-256)/254f+Mathf.Abs(y-342)/128f<=1)pixels[(511-y)*512+x]=new Color32(30,210,220,255);
        cityCliffProbeArt=new Texture2D(512,512,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};cityCliffProbeArt.SetPixels32(pixels);cityCliffProbeArt.Apply(false,true);
        cityArt["01-open-plains"]=cityCliffProbeArt;
        var rows=new List<J>();var heights=new List<J>();var tiles=new List<J>();
        for(int r=0;r<10;r++){
            rows.Add(new J(".........."));var levels=new List<J>();
            for(int c=0;c<10;c++){
                bool high=r>=3&&r<=4&&c>=3&&c<=4;levels.Add(new J(high?6:2));
                if(!high)tiles.Add(J.O("row",r,"col",c,"size",1,"asset","01-open-plains"));
            }
            heights.Add(new J(levels));
        }
        tiles.Add(J.O("row",3,"col",3,"size",2,"asset","28-terraced-fields"));
        var gate=J.O("id","gate","kind","gate","name","出口","row",9,"col",5,"footprint",J.O("row",9,"col",5,"size",1),"residents",new J(new List<J>()));
        City=J.O("layout_id","native-cliff-pixel-regression","place",-9999,"name","高低地面绘制检查","size",10,"theme","town","rows",new J(rows),"elevations",new J(heights),"terrain_tiles",new J(tiles),"nodes",new J(new List<J>{gate}),"entrance","gate");
        chosenRow=chosenCol=originRow=originCol=-1;cityZoom=1.2f;cityPan=Vector2.zero;cityLabels=cityGrid=cityReach=false;cityPointerDown=true;
        EnsureCityIndex();EnsureCityRoutes();
    }
    public bool ValidateCityCliffPixels(Texture2D frame,out J facts){
        CityGeometry(CityViewport,out float tw,out float th,out var center);var failures=new List<J>();var samples=new List<J>();
        foreach(var cell in new[]{new Vector2Int(3,5),new Vector2Int(4,5),new Vector2Int(5,3),new Vector2Int(5,4)}){
            var local=CitySurface(cell,tw,th,center)+new Vector2((cell.x==5?1:-1)*tw*.35f,-tw*.30f);
            var gui=local+CityViewport.position;float scale=Mathf.Min(frame.width/1680f,frame.height/960f);
            int x=Mathf.RoundToInt((frame.width-1680*scale)*.5f+gui.x*scale),y=frame.height-1-Mathf.RoundToInt((frame.height-960*scale)*.5f+gui.y*scale);
            var color=frame.GetPixel(x,y);bool surface=CitySurfacePick(local,tw,th,center)==cell;
            var sample=J.O("cell",cell.ToString(),"pixel_x",x,"pixel_y",y,"r",color.r,"g",color.g,"b",color.b,"visible_top",surface);samples.Add(sample);
            if(!surface||color.r>.3f||color.g<.6f||color.b<.6f)failures.Add(sample);
        }
        facts=J.O("sampled_ground_cells",samples.Count,"samples",new J(samples),"failures",new J(failures));return failures.Count==0;
    }
    public void CityEndCliffPixelFixture(){
        cityArt["01-open-plains"]=cityCliffSavedArt;Destroy(cityCliffProbeArt);City=cityCliffSavedView;cityCliffSavedView=null;
        cityZoom=cityCliffSavedZoom;cityPan=cityCliffSavedPan;cityLabels=cityCliffSavedLabels;cityGrid=cityCliffSavedGrid;cityReach=cityCliffSavedReach;cityPointerDown=false;
        EnsureCityIndex();EnsureCityRoutes();
    }
    public bool CitySmokeCliffFocus(){
        int drop=-1;CityGroundTile selected=new CityGroundTile();var neighbor=Vector2Int.zero;
        foreach(var tile in cityTerrainTiles)if(tile.Extent>1)for(int side=0;side<2;side++)for(int i=0;i<tile.Extent;i++){
            var next=tile.Corner+(side==0?new Vector2Int(tile.Extent,i):new Vector2Int(i,tile.Extent));if(!CityInside(next))continue;
            int difference=CityLevel(tile.Corner)-CityLevel(next);if(difference<=drop)continue;drop=difference;selected=tile;neighbor=next;
        }
        if(drop<=0)return false;
        cityZoom=3;cityPan=Vector2.zero;cityLabels=cityGrid=cityReach=false;chosenRow=chosenCol=-1;
        CityGeometry(CityViewport,out float tw,out float th,out var center);
        var high=CitySurface(selected.Corner,tw,th,center)+new Vector2(0,(selected.Extent-1)*th);
        var focus=(high+CitySurface(neighbor,tw,th,center))*.5f;cityPan+=new Vector2(CityViewport.width/2,CityViewport.height/2)-focus;CityClampPan();return true;
    }
}
}
