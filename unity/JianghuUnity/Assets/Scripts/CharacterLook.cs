using System;
using UnityEngine;

namespace Jianghu {
public sealed class CharacterLook {
    public const string Prefix="look2~";
    public string Id,Role,LegacyHead,Art="";
    public int Skin,Hair,Palette,Width,Grey,Eyes,Brows,Lips,Outfit,Sex,Stage,Variant;
    public float DisplayScale=>Stage==0?.77f:Stage==2?.96f:1;
    public int Head=>Role=="m-monk"?6:Stage==0?(Sex==0?4:5):Stage==2?(Sex==0?(Variant==0?2:9):3):Sex==0?(Variant==0?0:7):(Variant==0?1:8);
    public int Clothing=>Role.Contains("scholar")||Role.Contains("healer")||Role.Contains("official")?1:Role.Contains("escort")||Role.Contains("swordswoman")||Role.Contains("monk")?0:Outfit;
    public static bool IsId(string id)=>id!=null&&id.StartsWith(Prefix,StringComparison.Ordinal);
    public static string ForPerson(string role,J person){
        J p=person["appearance"];int age=person["age_months"].Has?person["age_months"].I/12:28;
        int grey=person["appearance_view"]["grey"].Has?person["appearance_view"]["grey"].I:Mathf.Clamp((age-42)/7,0,5);
        string trial=GameClient.Arg("-redLotusTrial")=="1"&&person["sex"].S!="女"&&age>=16?"~red-lotus":"";
        return Prefix+role+"~"+(p["head"].Has?p["head"].S:role)+"~"+p["skin"].I+"~"+p["hair"].I+"~"+p["palette"].I+"~"+p["face_width"].I+"~"+grey+"~"+p["eyes"].I+"~"+p["brows"].I+"~"+p["lips"].I+"~"+p["outfit"].I+"~"+(person["sex"].S=="女"?1:0)+"~"+(age<16?0:age>=60?2:1)+"~"+p["head_variant"].I+trial;
    }
    static int N(string[] p,int at,int max){return at<p.Length&&int.TryParse(p[at],out int n)?Mathf.Clamp(n,0,max):0;}
    public static CharacterLook Parse(string id){
        if(!IsId(id))throw new ArgumentException("A version 2 appearance is required",nameof(id));var p=id.Split('~');
        return new CharacterLook{Id=id,Role=p[1],LegacyHead=p[2],Skin=N(p,3,3),Hair=N(p,4,3),Palette=N(p,5,7),Width=N(p,6,4),Grey=N(p,7,5),Eyes=N(p,8,2),Brows=N(p,9,2),Lips=N(p,10,2),Outfit=N(p,11,1),Sex=N(p,12,1),Stage=N(p,13,2),Variant=N(p,14,1),Art=p.Length>15&&p[15]=="red-lotus"?p[15]:""};
    }
    public string FaceKey=>LegacyHead+":"+Skin+":"+Hair+":"+Width+":"+Eyes+":"+Brows+":"+Lips+":"+Variant;
    public static Color C(int r,int g,int b)=>new Color(r/255f,g/255f,b/255f,1);
    static readonly Color[] SkinColors={C(239,207,167),C(221,183,139),C(192,148,106),C(159,116,80)};
    static readonly Color[] HairColors={C(25,25,23),C(40,32,26),C(58,40,28),C(46,47,44)};
    static readonly Color[] PrimaryColors={C(50,83,91),C(125,76,52),C(56,92,73),C(97,73,110),C(47,66,112),C(126,48,49),C(103,79,53),C(152,151,134)};
    static readonly Color[] SecondaryColors={C(170,145,105),C(159,142,108),C(186,171,131),C(179,157,129),C(157,152,130),C(177,151,112),C(160,145,116),C(77,103,99)};
    public Color SkinColor=>SkinColors[Skin];
    public Color HairColor=>Color.Lerp(HairColors[Hair],C(181,178,163),Stage==2?1:Grey/5f);
    public Color Primary=>PrimaryColors[Palette];
    public Color Secondary=>SecondaryColors[Palette];
}
}
