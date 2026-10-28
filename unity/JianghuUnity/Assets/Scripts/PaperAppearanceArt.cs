using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// A look combines calibrated split heads with a complete, compatible outfit.
// Recolouring runs once on the GPU, preserving source alpha and ink outlines.
public static class PaperAppearanceArt {
    const string Prefix="look~";
    const long Budget=128L*1024*1024;
    sealed class Cached { public RenderTexture Texture; public long Used,Bytes; }
    static readonly Dictionary<string,Cached> textures=new Dictionary<string,Cached>();
    static readonly Dictionary<string,J> skins=new Dictionary<string,J>();
    static readonly Dictionary<string,Texture2D> sources=new Dictionary<string,Texture2D>();
    static readonly Dictionary<string,Texture2D> masks=new Dictionary<string,Texture2D>();
    static long tick,bytes;
    static Material material;
    static readonly Color[] SkinTones={C(239,207,167),C(221,183,139),C(192,148,106),C(159,116,80)};
    static readonly Color[] HairTones={C(25,25,23),C(40,32,26),C(58,40,28),C(46,47,44)};
    static readonly Color[] Primary={C(71,89,92),C(136,94,67),C(80,101,79),C(104,91,109),C(65,80,113),C(125,67,63),C(110,94,71),C(179,171,148)};
    static readonly Color[] Secondary={C(128,119,99),C(80,88,83),C(135,118,85),C(92,105,103),C(137,120,98),C(93,99,85),C(78,96,94),C(92,108,111)};
    static Color C(int r,int g,int b)=>new Color(r/255f,g/255f,b/255f,1);
    public static bool IsLook(string id)=>CharacterLook.IsId(id)||id!=null&&id.StartsWith(Prefix,StringComparison.Ordinal);
    public static string Id(string body,J person) {
        if(person["appearance"]["version"].I>=2)return CharacterLook.ForPerson(body,person);
        J p=person["appearance"];if(p["version"].I!=1)return body;
        string head=person["age_months"].I<192?(person["sex"].S=="女"?"f-child":"m-child"):p["head"].S;
        if(body=="m-monk")head="m-monk";
        int grey=person["appearance_view"]["grey"].Has?person["appearance_view"]["grey"].I:Mathf.Clamp((person["age_months"].I/12-42)/7,0,5);
        return Prefix+body+"~"+head+"~"+p["skin"].I+"~"+p["hair"].I+"~"+p["palette"].I+"~"+p["face_width"].I+"~"+grey;
    }
    public static J Skin(J catalog,string id) {
        if(CharacterLook.IsId(id))return CharacterArt.Skin(catalog,id);
        if(!IsLook(id))return catalog["skins"][id];
        if(skins.TryGetValue(id,out var found))return found;
        string[] pieces=id.Split('~');
        J original=catalog["skins"][pieces[1]],donor=catalog["skins"][pieces[2]];
        if(!original.Has||!donor.Has)throw new InvalidOperationException("Appearance references missing split parts: "+id);
        var skin=new J(new Dictionary<string,J>(original.D));
        skin["parts"]=new J(new Dictionary<string,J>(original["parts"].D));
        J head=J.Parse(donor["parts"]["head-neck"].ToString());
        head["height"]=skin["parts"]["head-neck"]["height"];
        head["breadth"]=new J((head["breadth"].Has?head["breadth"].N:1)*(.94+int.Parse(pieces[6])*.03));
        head["uv"]=J.Parse("[0,0,1,1]");
        skin["parts"]["head-neck"]=head;skin["portrait"]=head;
        skin["appearance_id"]=new J(id);skin["body_id"]=new J(pieces[1]);skin["head_id"]=new J(pieces[2]);
        skins[id]=skin;return skin;
    }
    public static Texture Atlas(J skin,bool head=false) {
        string id=skin["appearance_id"].S;
        if(CharacterLook.IsId(id))return CharacterArt.Canvas(id,portrait:head,size:384);
        if(id.Length==0)return Source(skin["id"].S);
        string[] p=id.Split('~');
        string sourceId=head?p[2]:p[1];
        // Head identity and outfit colour cache independently, so hundreds of
        // NPCs do not allocate hundreds of full-sized combined atlases.
        string key=head?"head:"+p[2]+":"+p[3]+":"+p[4]+":"+p[7]:"body:"+p[1]+":"+p[3]+":"+p[5];
        if(textures.TryGetValue(key,out var cached)){cached.Used=++tick;return cached.Texture;}
        Texture2D source=Source(sourceId);
        if(!masks.TryGetValue(sourceId,out var mask)){mask=Resources.Load<Texture2D>("Appearance/Masks/"+sourceId);masks[sourceId]=mask;}
        if(mask==null)throw new InvalidOperationException("Missing appearance recolouring mask: "+sourceId);
        if(material==null){var shader=Resources.Load<Shader>("Appearance/Recolor");if(shader==null||!shader.isSupported)throw new InvalidOperationException("Appearance shader unavailable");material=new Material(shader);}
        var crop=new Vector4(0,0,1,1);int width=source.width,height=source.height;
        if(head){J part=BaseHead(sourceId);crop=new Vector4((float)part["uv"][0].N,(float)part["uv"][1].N,(float)part["uv"][2].N,(float)part["uv"][3].N);width=part["size"][0].I;height=part["size"][1].I;}
        int palette=Mathf.Clamp(int.Parse(p[5]),0,Primary.Length-1),skinIndex=Mathf.Clamp(int.Parse(p[3]),0,SkinTones.Length-1),hairIndex=Mathf.Clamp(int.Parse(p[4]),0,HairTones.Length-1);
        material.SetTexture("_MaskTex",mask);material.SetVector("_Crop",crop);
        material.SetColor("_Primary",Primary[palette]);material.SetColor("_Secondary",Secondary[palette]);
        material.SetColor("_Skin",SkinTones[skinIndex]);material.SetColor("_Hair",Color.Lerp(HairTones[hairIndex],C(179,176,162),Mathf.Clamp01(int.Parse(p[7])/5f)));
        var texture=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32){name=key,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,useMipMap=true,autoGenerateMips=true};texture.Create();
        RenderTexture prior=RenderTexture.active;
        try{Graphics.Blit(source,texture,material);}finally{RenderTexture.active=prior;}
        long cost=(long)width*height*4*4/3;
        while(bytes+cost>Budget&&textures.Count>0){var oldest=textures.OrderBy(k=>k.Value.Used).First();bytes-=oldest.Value.Bytes;UnityEngine.Object.Destroy(oldest.Value.Texture);textures.Remove(oldest.Key);}
        textures[key]=new Cached{Texture=texture,Used=++tick,Bytes=cost};bytes+=cost;return texture;
    }
    static J baseCatalog;
    static J BaseHead(string id){if(baseCatalog==null)baseCatalog=J.Parse(Resources.Load<TextAsset>("PaperCast/index").text);return baseCatalog["skins"][id]["parts"]["head-neck"];}
    static Texture2D Source(string id){if(!sources.TryGetValue(id,out var texture)){texture=Resources.Load<Texture2D>("PaperCast/"+id);if(texture==null)throw new InvalidOperationException("Missing split atlas: "+id);sources[id]=texture;}return texture;}
    public static Texture PartAtlas(J skin,J part,Texture body)=>skin["appearance_id"].Has?Atlas(skin,part==skin["parts"]["head-neck"]):body;
    public static J CacheFacts()=>J.O("textures",textures.Count,"bytes",bytes,"budget",Budget);
    public static Color SkinTone(J skin,Color fallback)=>skin["appearance_id"].Has?SkinTones[Mathf.Clamp(int.Parse(skin["appearance_id"].S.Split('~')[3]),0,SkinTones.Length-1)]:fallback;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset(){foreach(var c in textures.Values)if(c.Texture!=null)UnityEngine.Object.Destroy(c.Texture);textures.Clear();skins.Clear();sources.Clear();masks.Clear();baseCatalog=null;bytes=tick=0;if(material!=null)UnityEngine.Object.Destroy(material);material=null;}
}
}
