using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    Texture2D cityRiverSavedWater;
    public void CityBeginRiverPixelFixture(){
        CityBeginCliffPixelFixture();cityRiverSavedWater=cityArt["42-blue-water"];cityArt["42-blue-water"]=cityCliffProbeArt;
        var rows=new List<J>();var heights=new List<J>();var tiles=new List<J>();
        for(int r=0;r<10;r++){
            string line="";var levels=new List<J>();
            for(int c=0;c<10;c++){
                bool bank=c<=2||c>=7,bridge=r==5&&c>=3&&c<=6;
                char terrain=bridge?'s':bank?(r==5?'+':'.'):'~';line+=terrain;levels.Add(new J(bank?1:0));
                tiles.Add(J.O("row",r,"col",c,"size",1,"asset",bank?"01-open-plains":"42-blue-water"));
            }
            rows.Add(new J(line));heights.Add(new J(levels));
        }
        var gate=J.O("id","gate","kind","gate","name","出口","row",9,"col",2,"footprint",J.O("row",9,"col",2,"size",1),"residents",new J(new List<J>()));
        City=J.O("version",7,"layout_id","native-natural-ford-pixel-regression","place",-9999,"name","自然浅滩与河岸检查","size",10,"theme","town","rows",new J(rows),"elevations",new J(heights),"terrain_tiles",new J(tiles),"nodes",new J(new List<J>{gate}),"entrance","gate");
        EnsureCityIndex();EnsureCityRoutes();
    }
    Color CityRiverSample(Texture2D frame,Vector2 local){
        var gui=local+CityViewport.position;float scale=Mathf.Min(frame.width/1680f,frame.height/960f);
        int x=Mathf.RoundToInt((frame.width-1680*scale)*.5f+gui.x*scale),y=frame.height-1-Mathf.RoundToInt((frame.height-960*scale)*.5f+gui.y*scale);
        return frame.GetPixel(x,y);
    }
    public bool ValidateCityRiverPixels(Texture2D frame,out J facts){
        CityGeometry(CityViewport,out float tw,out float th,out var center);var samples=new List<J>();int water=0,decks=0;
        foreach(int col in new[]{4,5}){
            var cell=new Vector2Int(5,col);var floor=CityXY(cell,tw,th,center)+new Vector2(0,tw*.25f);
            var waterColor=CityRiverSample(frame,floor);
            bool wet=waterColor.r<.3f&&waterColor.g>.6f&&waterColor.b>.6f;
            if(wet)water++;var deckSamples=new List<J>();
            foreach(float offset in new[]{-.1f,0,.1f}){
                var deckColor=CityRiverSample(frame,CitySurface(cell,tw,th,center)+new Vector2(-1,2).normalized*(tw*offset));
                bool natural=deckColor.r<.3f&&deckColor.g>.6f&&deckColor.b>.6f;if(natural)decks++;
                deckSamples.Add(J.O("offset",offset,"natural_surface",natural,"r",deckColor.r,"g",deckColor.g,"b",deckColor.b));
            }
            samples.Add(J.O("cell",cell.ToString(),"water_visible",wet,"water_r",waterColor.r,"water_g",waterColor.g,"water_b",waterColor.b,"ford_samples",new J(deckSamples)));
        }
        facts=J.O("water_samples_passed",water,"ford_samples_passed",decks,"samples",new J(samples));return water==2&&decks==6;
    }
    public bool ValidateNoBridges(out J facts){
        int bridges=0,fords=0;bool flat=true;
        foreach(var cell in cityCells){char t=CityTile(cell.x,cell.y);if(t=='=')bridges++;if(t=='s'){fords++;flat&=CityLevel(cell)==0;}}
        facts=J.O("bridges",bridges,"fords",fords,"fords_at_water_level",flat);return bridges==0&&flat;
    }
    public void CityEndRiverPixelFixture(){cityArt["42-blue-water"]=cityRiverSavedWater;CityEndCliffPixelFixture();}
    public void CitySmokeRiverFocus(){
        cityZoom=2.4f;cityPan=Vector2.zero;cityLabels=cityGrid=cityReach=false;chosenRow=chosenCol=-1;
        CityGeometry(CityViewport,out float tw,out float th,out var center);var sum=Vector2.zero;int count=0;
        foreach(var cell in cityCells)if(CityTile(cell.x,cell.y)=='s'){sum+=CityXY(cell,tw,th,center);count++;}
        if(count>0)cityPan+=new Vector2(CityViewport.width/2,CityViewport.height/2)-sum/count;CityClampPan();
    }
}
}
