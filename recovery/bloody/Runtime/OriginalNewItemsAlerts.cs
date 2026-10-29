using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;

// NewItemsAlertsManager 0x34377bc..0x343947c. This local service owns only
// inventory/viewed state and original alert counts, not shop purchases.
public sealed class OriginalNewItemsAlerts {
    const string Format="bloody-local-shop-alerts-v1";
    public sealed class ItemMeta {
        public readonly ushort Id;
        public readonly int Kind,Region,UnlockRule,UnlocksAtLevel,SeasonalType,MaxOwnable;
        public readonly bool IsEmptyShopSlot;
        public ItemMeta(J row) {
            Id=unchecked((ushort)row["id"].I);Kind=row["kind"].I;Region=row["region"].I;UnlockRule=row["unlock_rule"].I;
            UnlocksAtLevel=row["unlocks_at_level"].I;SeasonalType=row["seasonal_type"].I;MaxOwnable=row["max_ownable"].I;IsEmptyShopSlot=row["empty_shop_slot"].B;
        }
    }
    readonly OriginalLocalProgress progress;
    readonly J source;
    readonly string savePath;
    readonly Dictionary<ushort,ItemMeta> items=new Dictionary<ushort,ItemMeta>();
    readonly Dictionary<(int,int),List<ushort>> byRegionKind=new Dictionary<(int,int),List<ushort>>();
    readonly Dictionary<int,List<ushort>> rankedSeasonalByKind=new Dictionary<int,List<ushort>>();
    readonly List<int> kindPips=new List<int>();
    readonly HashSet<ushort> viewed=new HashSet<ushort>();
    readonly Dictionary<ushort,byte> ownedOverrides=new Dictionary<ushort,byte>();
    readonly Dictionary<int,int> pipCounts=new Dictionary<int,int>();
    public IReadOnlyDictionary<int,int> KindPipCounts=>pipCounts;
    public IReadOnlyCollection<ushort> ViewedIds=>viewed;
    public int TopPipCount {get;private set;}
    public int BottomPipCount {get;private set;}
    public bool IncludeRankedForPips {get;set;}
    // Source hq's active seasonal type. The normal local host passes None=0.
    public int SeasonalType {get;set;}
    public event Action OnPipsRefreshed;
    OriginalNewItemsAlerts(J library,OriginalLocalProgress owner,J snapshot,string path) {
        source=library??throw new ArgumentNullException(nameof(library));progress=owner??throw new ArgumentNullException(nameof(owner));savePath=path;
        foreach(var row in source["kind_pips"].A)kindPips.Add(row["kind"].I);
        foreach(var row in source["items"].A) {
            var meta=new ItemMeta(row);if(meta.Id==0)continue;
            items[meta.Id]=meta;
            var key=(meta.Region,meta.Kind);
            if(!byRegionKind.TryGetValue(key,out var regionItems))byRegionKind[key]=regionItems=new List<ushort>();
            regionItems.Add(meta.Id);
            if(meta.UnlockRule==3||meta.UnlockRule==4) {
                if(!rankedSeasonalByKind.TryGetValue(meta.Kind,out var rankItems))rankedSeasonalByKind[meta.Kind]=rankItems=new List<ushort>();
                rankItems.Add(meta.Id);
            }
        }
        J entries=snapshot?["Inventory"]["Entries"]??source["inventory_defaults"]["entries"];
        foreach(var row in entries.A) {
            ushort id=unchecked((ushort)row["Id"].I);
            // Native GetOwnedCount returns the first matching inventory row.
            if(!ownedOverrides.ContainsKey(id))ownedOverrides[id]=unchecked((byte)row["Owned"].I);
        }
        J viewedRows=snapshot?["ViewedIds"]??source["inventory_defaults"]["viewed_ids"];
        foreach(var id in viewedRows.A)viewed.Add(unchecked((ushort)id.I));
    }
    public static OriginalNewItemsAlerts Open(string absoluteSavePath,OriginalLocalProgress progress) {
        if(string.IsNullOrWhiteSpace(absoluteSavePath)||!Path.IsPathRooted(absoluteSavePath))throw new ArgumentException("An isolated absolute shop-alert save path is required",nameof(absoluteSavePath));
        if(progress==null)throw new ArgumentNullException(nameof(progress));
        string path=Path.GetFullPath(absoluteSavePath);
        if(progress.SavePath!=null&&string.Equals(path,Path.GetFullPath(progress.SavePath),StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Shop alerts and local progress require separate save files",nameof(absoluteSavePath));
        var asset=Resources.Load<TextAsset>("OriginalShopAlertSource");
        if(asset==null)throw new InvalidOperationException("OriginalShopAlertSource is missing");
        return OpenFromSource(J.Parse(asset.text),path,progress);
    }
    public static OriginalNewItemsAlerts FromSource(J source,OriginalLocalProgress progress,J snapshot=null)=>new OriginalNewItemsAlerts(source,progress,snapshot,null);
    public static OriginalNewItemsAlerts OpenFromSource(J source,string absoluteSavePath,OriginalLocalProgress progress) {
        if(string.IsNullOrWhiteSpace(absoluteSavePath)||!Path.IsPathRooted(absoluteSavePath))throw new ArgumentException("An isolated absolute shop-alert save path is required",nameof(absoluteSavePath));
        if(progress==null)throw new ArgumentNullException(nameof(progress));
        string path=Path.GetFullPath(absoluteSavePath);J snapshot=null;
        if(progress.SavePath!=null&&string.Equals(path,Path.GetFullPath(progress.SavePath),StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Shop alerts and local progress require separate save files",nameof(absoluteSavePath));
        if(File.Exists(path)) {
            snapshot=J.Parse(File.ReadAllText(path));
            if(snapshot["format"].S!=Format||!snapshot["Inventory"]["Entries"].Has||!snapshot["ViewedIds"].Has)
                throw new InvalidDataException("Unknown or incomplete original shop-alert save: "+path);
        }
        var service=new OriginalNewItemsAlerts(source,progress,snapshot,path);if(snapshot==null)service.Save();return service;
    }
    public J Snapshot()=>J.O("format",Format,"source_apk_sha256",source["source_apk_sha256"].S,
        "Inventory",J.O("Entries",new J(ownedOverrides.Select(p=>J.O("Id",p.Key,"Owned",p.Value)).ToList())),
        "ViewedIds",new J(viewed.OrderBy(id=>id).Select(id=>new J(id)).ToList()));
    void Save() {
        if(savePath==null)return;
        string folder=Path.GetDirectoryName(savePath);Directory.CreateDirectory(folder);
        string temporary=savePath+".writing";File.WriteAllText(temporary,Snapshot().ToString());
        if(File.Exists(savePath))File.Replace(temporary,savePath,null);else File.Move(temporary,savePath);
    }
    public byte GetOwnedCount(ushort id) {
        if(ownedOverrides.TryGetValue(id,out var count))return count;
        if(!items.TryGetValue(id,out var meta))return 0;
        return meta.UnlockRule==1||meta.UnlockRule==3||meta.UnlockRule==4?unchecked((byte)meta.MaxOwnable):(byte)0;
    }
    public bool IsOwned(ushort id)=>GetOwnedCount(id)>0;
    public bool IsItemNew(ushort id)=>IsNew(id,progress.GetCompletedLevelsForRegion(progress.ShopRegion),progress.GetCurrentRank());
    bool IsNew(ushort id,int completedLevels,int rank) {
        if(!items.TryGetValue(id,out var meta)||meta.IsEmptyShopSlot||meta.UnlocksAtLevel==0||viewed.Contains(id))return false;
        if(IsOwned(id)&&meta.UnlockRule!=3)return false;
        if(meta.UnlockRule==4)return meta.SeasonalType!=0&&meta.SeasonalType==SeasonalType;
        if(meta.UnlockRule==3)return meta.UnlocksAtLevel<=rank;
        if(meta.UnlockRule==2)return meta.UnlocksAtLevel<=completedLevels;
        return false;
    }
    int CountKind(int kind,int region,bool includeRanked,int completed,int rank) {
        // Despite the host-facing argument name, the native true branch is a
        // separate cross-region Ranked/Seasonal list, not a union with region.
        List<ushort> list;
        if(includeRanked){if(!rankedSeasonalByKind.TryGetValue(kind,out list))return 0;}
        else if(!byRegionKind.TryGetValue((region,kind),out list))return 0;
        int count=0;foreach(ushort id in list)if(IsNew(id,completed,rank))count++;return count;
    }
    public int GetNewItemsCount(int region,bool includeRanked) {
        int completed=progress.GetCompletedLevelsForRegion(region),rank=progress.GetCurrentRank(),total=0;
        foreach(int kind in kindPips)total=unchecked(total+CountKind(kind,region,includeRanked,completed,rank));
        return total;
    }
    public void RefreshAllPips() {
        int region=progress.ShopRegion,completed=progress.GetCompletedLevelsForRegion(region),rank=progress.GetCurrentRank();
        TopPipCount=0;BottomPipCount=0;pipCounts.Clear();
        foreach(int kind in kindPips) {
            int count=CountKind(kind,region,IncludeRankedForPips,completed,rank);pipCounts[kind]=count;
            if(unchecked((uint)(kind-9))<4)TopPipCount=unchecked(TopPipCount+count);
            else if(unchecked((uint)(kind-14))<3)BottomPipCount=unchecked(BottomPipCount+count);
        }
        OnPipsRefreshed?.Invoke();
    }
    public void MarkItemViewed(ushort id) {if(!viewed.Add(id))return;Save();RefreshAllPips();}
    // Native ClearViewedIds clears memory/refreshes, without invoking dwg's
    // PlayerPrefs save. A later MarkItemViewed persists the resulting set.
    public void ClearViewedIds(){viewed.Clear();RefreshAllPips();}
}
