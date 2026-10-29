using System;
using System.Collections.Generic;
using UnityEngine;

namespace Jianghu {
// Resource selection only. Appearance IDs retain the saved facial parameters;
// original painted cast pieces use their own face and colours at render time.
public static class CharacterCastCatalog {
    public const string Prefix="cast24-";
    public sealed class Entry {
        public string Id,Asset,Label;
        public int Sex,Stage;
    }
    static bool loaded;
    static readonly List<Entry> entries=new List<Entry>();
    static readonly Dictionary<string,Entry> byId=new Dictionary<string,Entry>(StringComparer.Ordinal);
    static readonly Dictionary<string,Entry> byAsset=new Dictionary<string,Entry>(StringComparer.Ordinal);
    static void Load(){
        if(loaded)return;loaded=true;
        var text=Resources.Load<TextAsset>("Characters/Cast24/catalog");if(text==null)return;
        var catalog=J.Parse(text.text);
        foreach(var item in catalog["characters"].A){
            string id=item["id"].S,asset=item["asset"].S;
            if(!SafeId(id)||asset!=Prefix+id||byId.ContainsKey(id))throw new InvalidOperationException("Invalid cast character resource: "+id);
            var entry=new Entry{Id=id,Asset=asset,Label=item["label"].Has?item["label"].S:id,Sex=id.StartsWith("f-",StringComparison.Ordinal)?1:0,Stage=id.EndsWith("-child",StringComparison.Ordinal)?0:id.EndsWith("-elder",StringComparison.Ordinal)?2:1};
            entries.Add(entry);byId.Add(id,entry);byAsset.Add(asset,entry);
        }
    }
    static bool SafeId(string id){if(string.IsNullOrEmpty(id))return false;foreach(char c in id)if(c!='-'&&(c<'a'||c>'z')&&(c<'0'||c>'9'))return false;return true;}
    public static IReadOnlyList<Entry> Entries {get{Load();return entries;}}
    public static bool Available {get{Load();return entries.Count>0;}}
    public static bool IsAsset(string asset){Load();return asset!=null&&byAsset.ContainsKey(asset);}
    public static string Resolve(string role,int sex,int stage){
        Load();if(entries.Count==0)return "";
        string prefix=sex==1?"f-":"m-",id=stage==0?prefix+"child":stage==2?prefix+"elder":role;
        if(id!=null&&byId.TryGetValue(id,out var found))return found.Asset;
        if(byId.TryGetValue(prefix+"youth-farmer",out found))return found.Asset;
        return "";
    }
    public static string ResourcePath(string asset){
        if(asset=="red-lotus")return "Characters/RedLotus";
        if(string.IsNullOrEmpty(asset)||asset=="legacy")return "Characters";
        Load();if(byAsset.TryGetValue(asset,out var entry))return "Characters/Cast24/"+entry.Id;
        throw new ArgumentException("Unknown character artwork: "+asset,nameof(asset));
    }
    public static string AppearanceId(Entry entry)=>CharacterLook.Prefix+entry.Id+"~"+entry.Id+"~0~0~0~2~0~0~0~0~0~"+entry.Sex+"~"+entry.Stage+"~0~"+entry.Asset;
    public static J Facts()=>J.O("available",Available,"characters",entries.Count,"automatic_role_mapping",true,"source","Characters/Cast24/catalog");
}
}
