using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public static class CharacterArt {
    const long Budget=128L*1024*1024;
    sealed class Entry {public RenderTexture Texture;public long Used,Bytes;}
    static readonly Dictionary<string,Entry> images=new Dictionary<string,Entry>();
    static readonly Dictionary<string,CharacterLook> looks=new Dictionary<string,CharacterLook>();
    static readonly Dictionary<string,J> metadata=new Dictionary<string,J>();
    static CharacterRenderer studio;
    static string studioArt="";
    static long bytes,tick;
    static readonly Rect BodyUV=new Rect(.25f,.04f,.50f,.84f),BustUV=new Rect(.27f,.40f,.46f,.42f);
    public static J LastFigureSampling {get;private set;}=J.O();
    public static CharacterLook Look(string id){if(!looks.TryGetValue(id,out var l)){l=CharacterLook.Parse(id);if(looks.Count>4096)looks.Clear();looks[id]=l;}return l;}
    public static J Skin(J catalog,string id){
        if(metadata.TryGetValue(id,out var result))return result;var look=Look(id);var original=catalog["skins"][look.Role];
        result=J.Parse(original.ToString());result["appearance_id"]=new J(id);result["body_id"]=new J(look.Role);result["head_id"]=new J(look.LegacyHead);result["modular_character"]=new J(true);
        if(metadata.Count>4096)metadata.Clear();metadata[id]=result;return result;
    }
    public static Texture Canvas(string id,string clip="bind",float phase=0,bool left=false,bool portrait=false,int size=192){
        return Capture(id,clip,phase,left,portrait?"portrait":"canvas",portrait&&size==192?256:size,portrait&&size==192?256:size);
    }
    static Texture Capture(string id,string clip,float phase,bool left,string view,int width,int height){
        int step=clip=="walk"?(int)(Mathf.Repeat(phase,1)*24):0;
        width=Mathf.Clamp(width,32,2048);height=Mathf.Clamp(height,32,2048);
        string key=id+":"+clip+":"+step+":"+left+":"+view+":"+width+"x"+height;
        if(images.TryGetValue(key,out var entry)){entry.Used=++tick;return entry.Texture;}
        var look=Look(id);if(studio==null||studioArt!=look.Art){studio?.Dispose();studio=new CharacterRenderer(256,asset:look.Art);studioArt=look.Art;}studio.Render(look,CharacterPose.Sample(clip,step/24f,left),look.DisplayScale,draw:false);
        Rect uv=view=="portrait"?studio.PortraitUV:view=="bust"?BustUV:view=="body"?BodyUV:new Rect(0,0,1,1);
        var texture=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32){name="Character cache "+key,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};texture.Create();
        studio.RenderCrop(texture,uv);
        long cost=(long)width*height*4;
        while(bytes+cost>Budget&&images.Count>0){var oldest=images.OrderBy(p=>p.Value.Used).First();oldest.Value.Texture.Release();UnityEngine.Object.Destroy(oldest.Value.Texture);bytes-=oldest.Value.Bytes;images.Remove(oldest.Key);}
        images[key]=new Entry{Texture=texture,Used=++tick,Bytes=cost};bytes+=cost;return texture;
    }
    static int RasterSize(float pixels)=>Mathf.Clamp(Mathf.CeilToInt(pixels*1.25f/64)*64,64,2048);
    static Vector2 PixelSize(Rect rect)=>new Vector2(GUI.matrix.MultiplyVector(new Vector3(rect.width,0,0)).magnitude,GUI.matrix.MultiplyVector(new Vector3(0,rect.height,0)).magnitude);
    public static void DrawPortrait(Rect rect,string id,bool deceased=false){var pixels=PixelSize(rect);int size=RasterSize(Mathf.Max(pixels.x,pixels.y));var prior=GUI.color;if(deceased)GUI.color=new Color(.64f,.63f,.59f);GUI.DrawTexture(rect,Capture(id,"bind",0,false,"portrait",size,size),ScaleMode.ScaleToFit,true);GUI.color=prior;}
    public static void DrawFigure(Rect rect,string id,string clip,float phase,bool left,bool bust,Color? tint=null){
        Rect uv=bust?BustUV:BodyUV;
        float ratio=uv.width/uv.height,height=Mathf.Min(rect.height,rect.width/ratio),width=height*ratio;
        var target=new Rect(rect.center.x-width/2,rect.yMax-height,width,height);var pixels=PixelSize(target);
        int rasterHeight=RasterSize(pixels.y),rasterWidth=RasterSize(pixels.x);
        var texture=Capture(id,clip,phase,left,bust?"bust":"body",rasterWidth,rasterHeight);
        LastFigureSampling=J.O("draw_width",pixels.x,"draw_height",pixels.y,"raster_width",texture.width,"raster_height",texture.height,"crop_before_raster",true,"bust",bust);
        var prior=GUI.color;GUI.color=tint??Color.white;GUI.DrawTexture(target,texture,ScaleMode.StretchToFill,true);GUI.color=prior;
    }
    public static J Facts()=>J.O("textures",images.Count,"bytes",bytes,"budget",Budget,"shared_studio_renderers",studio==null?0:1,"head_modules",CharacterRenderer.Catalog["heads"].A.Count);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset(){foreach(var e in images.Values){if(e.Texture!=null){e.Texture.Release();UnityEngine.Object.Destroy(e.Texture);}}images.Clear();looks.Clear();metadata.Clear();studio?.Dispose();studio=null;bytes=tick=0;}
}
}
