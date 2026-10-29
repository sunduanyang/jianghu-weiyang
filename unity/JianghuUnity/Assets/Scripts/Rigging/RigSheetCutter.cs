using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// 把一整张拆件图按不透明区域切成单块。没有透明通道的图以左上角颜色当背景。
// 相距两像素以内的笔画算同一块，太小的块（编号数字、杂点）丢弃。
public static class RigSheetCutter {
    public sealed class Cut {public Texture2D Tex;public RectInt Source;public int Area;}
    public static List<Cut> Run(Texture2D sheet,int minArea=0){
        int W=sheet.width,H=sheet.height;var px=sheet.GetPixels32();
        // Unity 的像素行从下往上；这里统一换成从上往下，记录的 source_rect 与看图软件一致。
        var mask=new bool[W*H];bool opaque=true;
        for(int i=0;i<px.Length;i+=97)if(px[i].a<250){opaque=false;break;}
        Color32 bg=px[(H-1)*W];
        for(int y=0;y<H;y++)for(int x=0;x<W;x++){
            var c=px[(H-1-y)*W+x];
            mask[y*W+x]=opaque?Mathf.Abs(c.r-bg.r)+Mathf.Abs(c.g-bg.g)+Mathf.Abs(c.b-bg.b)>48:c.a>40;
        }
        const int R=2;var grow=new bool[W*H];
        for(int y=0;y<H;y++)for(int x=0;x<W;x++){
            if(!mask[y*W+x])continue;
            for(int dy=-R;dy<=R;dy++){int yy=y+dy;if(yy<0||yy>=H)continue;for(int dx=-R;dx<=R;dx++){int xx=x+dx;if(xx>=0&&xx<W)grow[yy*W+xx]=true;}}
        }
        var label=new int[W*H];var stack=new int[W*H];var comps=new List<(int n,int x0,int y0,int x1,int y1,int area)>();int count=0;
        for(int i=0;i<W*H;i++){
            if(!grow[i]||label[i]!=0)continue;
            count++;int sp=0;stack[sp++]=i;label[i]=count;int x0=W,y0=H,x1=0,y1=0,area=0;
            while(sp>0){
                int k=stack[--sp];int kx=k%W,ky=k/W;if(mask[k])area++;
                if(kx<x0)x0=kx;if(kx>x1)x1=kx;if(ky<y0)y0=ky;if(ky>y1)y1=ky;
                if(kx>0&&grow[k-1]&&label[k-1]==0){label[k-1]=count;stack[sp++]=k-1;}
                if(kx<W-1&&grow[k+1]&&label[k+1]==0){label[k+1]=count;stack[sp++]=k+1;}
                if(ky>0&&grow[k-W]&&label[k-W]==0){label[k-W]=count;stack[sp++]=k-W;}
                if(ky<H-1&&grow[k+W]&&label[k+W]==0){label[k+W]=count;stack[sp++]=k+W;}
            }
            comps.Add((count,x0,y0,x1,y1,area));
        }
        int threshold=minArea>0?minArea:Mathf.Max(600,(int)(W*H*.0015f));
        var result=new List<Cut>();
        foreach(var c in comps.Where(c=>c.area>=threshold).OrderBy(c=>c.y0/40).ThenBy(c=>c.x0)){
            int w=c.x1-c.x0+1,h=c.y1-c.y0+1;var outPx=new Color32[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++){
                int si=(c.y0+y)*W+c.x0+x;if(label[si]!=c.n||!mask[si])continue;
                var src=px[(H-1-(c.y0+y))*W+c.x0+x];if(opaque)src.a=255;
                outPx[(h-1-y)*w+x]=src;
            }
            var t=new Texture2D(w,h,TextureFormat.RGBA32,true){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=2};
            t.SetPixels32(outPx);t.Apply(true,false);
            result.Add(new Cut{Tex=t,Source=new RectInt(c.x0,c.y0,w,h),Area=c.area});
        }
        return result;
    }
}
}
