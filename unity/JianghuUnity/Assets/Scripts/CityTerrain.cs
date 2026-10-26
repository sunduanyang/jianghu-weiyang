using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    // Elevation is presentation data. The backend remains the owner of routes,
    // walk costs and saves; every client derives the same terraces from the city.
    const float CityRise=.28f;
    int[,] cityLevels;
    readonly Dictionary<int,Texture2D> cityFaces=new Dictionary<int,Texture2D>();

    void BuildCityTerrain(){
        int size=City["size"].I;cityLevels=new int[size,size];
        float seed=City["place"].I*.731f;
        for(int row=0;row<size;row++)for(int col=0;col<size;col++){
            char t=CityTile(row,col);
            float hill=Mathf.PerlinNoise(seed+row*.19f,17.3f+seed+col*.19f);
            int level=2+Mathf.Clamp(Mathf.FloorToInt((hill-.25f)*7),0,3);
            if(t=='~')level=0;
            else if(t=='=')level=2;
            else if(t=='^')level+=3;
            else if(t=='t')level++;
            cityLevels[row,col]=level;
        }
        // A facility occupies one level across its entire 1x1 or 2x2 foundation.
        foreach(var node in City["nodes"].A){
            var corner=CityLotCorner(node);int extent=CityLotSize(node),level=0;
            for(int r=0;r<extent;r++)for(int c=0;c<extent;c++)level=Mathf.Max(level,cityLevels[corner.x+r,corner.y+c]);
            SetCityLotLevel(node,level);
        }
        // Walkable neighbours rise by at most one stair. Lowering only makes
        // this relaxation converge, including lots that meet a river crossing.
        bool changed=true;
        while(changed){
            changed=false;
            for(int row=0;row<size;row++)for(int col=0;col<size;col++){
                var q=new Vector2Int(row,col);if(CityBlocked(CityTile(row,col)))continue;
                foreach(var step in CitySteps){
                    var next=q+step;if(!CityInside(next)||CityBlocked(CityTile(next.x,next.y)))continue;
                    int ceiling=CityLevel(next)+1;if(CityLevel(q)<=ceiling)continue;
                    if(cityLots.TryGetValue(q,out var node))SetCityLotLevel(node,ceiling);
                    else cityLevels[row,col]=ceiling;
                    changed=true;
                }
            }
        }
    }
    void SetCityLotLevel(J node,int level){
        var corner=CityLotCorner(node);int extent=CityLotSize(node);
        for(int r=0;r<extent;r++)for(int c=0;c<extent;c++)cityLevels[corner.x+r,corner.y+c]=level;
    }
    int CityLevel(Vector2Int q)=>CityInside(q)&&cityLevels!=null?cityLevels[q.x,q.y]:-2;
    int CityGroundLevel(Vector2Int q)=>CityInside(q)&&CityTile(q.x,q.y)=='='?0:CityLevel(q);
    Vector2 CitySurface(Vector2Int q,float tw,float th,Vector2 center)=>CityXY(q,tw,th,center)-new Vector2(0,CityLevel(q)*tw*CityRise);
    Vector2Int CitySurfacePick(Vector2 mouse,float tw,float th,Vector2 center){
        // Test the raised diamonds in reverse paint order. The flat inverse
        // would select the tile behind a hill, and miss the actual map edges.
        int size=City["size"].I;
        for(int sum=(size-1)*2;sum>=0;sum--)for(int row=size-1;row>=0;row--){
            int col=sum-row;if(col<0||col>=size)continue;
            var q=new Vector2Int(row,col);var d=mouse-CitySurface(q,tw,th,center);
            if(Mathf.Abs(d.x)/tw+Mathf.Abs(d.y)/th<=1.001f)return q;
            int level=CityGroundLevel(q);
            var ground=CityXY(q,tw,th,center)-new Vector2(0,level*tw*CityRise);d=mouse-ground;
            if(CityTile(row,col)=='='&&Mathf.Abs(d.x)/tw+Mathf.Abs(d.y)/th<=1.001f)return q;
            for(int side=0;side<2;side++){
                if(side==0?(d.x< -tw||d.x>0):(d.x<0||d.x>tw))continue;
                int drop=level-CityGroundLevel(q+new Vector2Int(side==0?1:0,side==1?1:0));
                float edge=th*(1-Mathf.Abs(d.x)/tw);
                if(drop>0&&d.y>=edge&&d.y<=edge+drop*tw*CityRise)return q;
            }
        }
        return new Vector2Int(-1,-1);
    }
    Texture2D CityFace(int steps,bool right){
        int key=steps*2+(right?1:0);if(cityFaces.TryGetValue(key,out var cached))return cached;
        int depth=Mathf.RoundToInt(128*CityRise*steps),height=64+depth;
        var pixels=new Color32[128*height];
        for(int y=0;y<height;y++)for(int x=0;x<128;x++){
            float top=right?(127-x)*.5f:x*.5f,d=y-top;
            if(d<0||d>depth)continue;
            float grain=((x*13+y*7)%11-5)*.0025f;
            float shade=(right?.68f:.85f)-d/Mathf.Max(1,depth)*.13f+grain;
            // Fine strata and shaded vertical joints make the thickness legible.
            if(d>3&&Mathf.Repeat(d,128*CityRise)<1.2f)shade-=.08f;
            if(x==0||x==127)shade-=.09f;
            if(d<1.5f)shade+=.10f;
            byte v=(byte)Mathf.Clamp(shade*255,0,255);
            pixels[(height-1-y)*128+x]=new Color32(v,v,v,255);
        }
        var texture=new Texture2D(128,height,TextureFormat.RGBA32,false){name="City block face "+key,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        texture.SetPixels32(pixels);texture.Apply(false,true);cityFaces[key]=texture;return texture;
    }
    void DrawCityBlock(Vector2Int q,Vector2 p,float tw,float th,Color tint){
        char terrain=CityTile(q.x,q.y);bool water=terrain=='~'||terrain=='=';int level=water?0:CityLevel(q);
        Color earth=water?new Color(.30f,.47f,.48f):terrain=='^'?new Color(.52f,.51f,.42f):new Color(.61f,.55f,.39f);
        var old=GUI.color;GUI.color=earth*tint;
        for(int side=0;side<2;side++){
            var neighbor=q+new Vector2Int(side==0?1:0,side==1?1:0);
            int neighborLevel=CityGroundLevel(neighbor);
            int steps=level-neighborLevel;if(steps<=0)continue;
            float depth=steps*tw*CityRise;
            GUI.DrawTexture(new Rect(p.x+(side==0?-tw:0),p.y,tw,th+depth),CityFace(steps,side==1));
        }
        GUI.color=old;
    }
    void DrawCityStairs(Vector2Int q,Vector2 p,float tw,float th){
        if(CityTile(q.x,q.y)!='+')return;
        // Three stone treads bridge each one-level road transition.
        for(int side=0;side<2;side++){
            var next=q+new Vector2Int(side==0?1:0,side==1?1:0);
            if(!CityInside(next)||!"+=".Contains(CityTile(next.x,next.y))||CityLevel(q)!=CityLevel(next)+1)continue;
            float direction=side==0?-1:1;
            var edge=p+new Vector2(direction*tw*.5f,th*.5f);
            for(int i=1;i<=3;i++){
                var tread=edge+new Vector2(0,tw*CityRise*i/4);
                Line(tread-new Vector2(tw*.13f,direction*-th*.13f),tread+new Vector2(tw*.13f,direction*-th*.13f),new Color(.82f,.76f,.59f),1);
            }
        }
    }
}
}
