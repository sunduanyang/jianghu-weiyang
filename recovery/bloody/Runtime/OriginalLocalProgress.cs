using System;
using System.Collections.Generic;
using System.IO;
using Jianghu;
using UnityEngine;

// Local owner for the recovered ProgressController / WalletController flow.
// The JSON envelope is this port's storage, not the Android save encryption.
public sealed class OriginalLocalProgress {
    const string Format="bloody-local-progress-v1";
    readonly J source,progress,wallet,prefs;
    readonly string savePath;
    public int GameMode {get;set;}
    public int Region {get;private set;}
    public int Stage {get;private set;}
    public int Level {get;private set;}
    public int Seed {get;private set;}
    public int LevelType {get;private set;}
    public int ShopRegion {get;private set;}
    public bool TutorialPassed=>progress["TutorialPassed"].B;
    public int Gold=>wallet["Gold"].I;
    public string SavePath=>savePath;
    public OriginalCampaignOutcome LastCampaignWinResult {get;private set;}
    public event Action OnProgressChanged,OnWalletChanged,OnCampaignSelectionChanged;
    public event Action<int> OnCampaignRegionChanged;
    public event Action<int,byte> OnCampaignMatchWon;

    OriginalLocalProgress(J originalSource,J snapshot,string path) {
        source=Clone(originalSource??throw new ArgumentNullException(nameof(originalSource)));
        savePath=path;
        var defaults=source["defaults"];
        progress=snapshot?["Progress"].Has==true?Clone(snapshot["Progress"]):J.O(
            "TutorialPassed",defaults["tutorial_passed"].B,
            "Campaign",J.O("Regions",Clone(defaults["campaign_regions"])),
            "Survival",J.O("MaxWavesPassed",defaults["survival_max_waves"].I),
            "Ranked",Clone(defaults["ranked"]),"AppliedGrantIds",Clone(defaults["applied_grant_ids"]));
        if(!progress["AppliedGrantIds"].Has)progress["AppliedGrantIds"]=new J(new List<J>());
        wallet=snapshot?["Wallet"].Has==true?Clone(snapshot["Wallet"]):J.O("Gold",defaults["gold_release_player"].I);
        prefs=snapshot?["PlayerPrefs"].Has==true?Clone(snapshot["PlayerPrefs"]):J.O();
        var selection=defaults["selection"];
        Region=selection["region"].I;Stage=selection["stage"].I;Level=selection["level"].I;
        Seed=selection["seed"].I;LevelType=selection["level_type"].I;ShopRegion=selection["shop_region"].I;
        // ProgressController.cwp restores only the raw region/stage prefs. It
        // does not run SetStage or restore the static current level/mode.
        string regionKey=source["prefs"]["region"].S;
        if(prefs[regionKey].Has)Region=prefs[regionKey].I;
        string stageKey=StageKey(Region);if(prefs[stageKey].Has)Stage=prefs[stageKey].I;
    }

    public static OriginalLocalProgress Open(string absoluteSavePath) {
        if(string.IsNullOrWhiteSpace(absoluteSavePath)||!Path.IsPathRooted(absoluteSavePath))
            throw new ArgumentException("An isolated absolute local progress path is required",nameof(absoluteSavePath));
        var asset=Resources.Load<TextAsset>("OriginalProgressSource");
        if(asset==null)throw new InvalidOperationException("OriginalProgressSource is missing");
        return OpenFromSource(J.Parse(asset.text),absoluteSavePath);
    }
    // Explicit source injection lets numerical checks exercise this same class.
    public static OriginalLocalProgress FromSource(J originalSource,J snapshot=null)=>new OriginalLocalProgress(originalSource,snapshot,null);
    public static OriginalLocalProgress OpenFromSource(J originalSource,string absoluteSavePath) {
        if(string.IsNullOrWhiteSpace(absoluteSavePath)||!Path.IsPathRooted(absoluteSavePath))
            throw new ArgumentException("An isolated absolute local progress path is required",nameof(absoluteSavePath));
        string path=Path.GetFullPath(absoluteSavePath);J snapshot=null;
        if(File.Exists(path)) {
            snapshot=J.Parse(File.ReadAllText(path));
            if(snapshot["format"].S!=Format)throw new InvalidDataException("Unknown original local progress format: "+path);
            if(!snapshot["Progress"]["Campaign"]["Regions"].Has||!snapshot["Wallet"]["Gold"].Has)
                throw new InvalidDataException("Incomplete original local progress: "+path);
        }
        var state=new OriginalLocalProgress(originalSource,snapshot,path);
        if(snapshot==null)state.Save();
        return state;
    }
    static J Clone(J value)=>J.Parse(J.Write(value));
    string StageKey(int region)=>source["prefs"]["stage_prefix"].S+region;
    void Error(string key,params object[] values) {
        string text=source["errors"][key]["text"].S;Debug.LogError(values.Length==0?text:string.Format(text,values));
    }
    void ProgressChanged(){Save();OnProgressChanged?.Invoke();}
    void WalletChanged(){Save();OnWalletChanged?.Invoke();}
    public J Snapshot()=>J.O("format",Format,"source_apk_sha256",source["source_apk_sha256"].S,
        "Progress",Clone(progress),"Wallet",Clone(wallet),"PlayerPrefs",Clone(prefs));
    public void Save() {
        if(savePath==null)return;
        string directory=Path.GetDirectoryName(savePath);if(!Directory.Exists(directory))Directory.CreateDirectory(directory);
        string temporary=savePath+".writing";
        File.WriteAllText(temporary,J.Write(Snapshot()));
        if(File.Exists(savePath))File.Replace(temporary,savePath,null);else File.Move(temporary,savePath);
    }
    public void MarkTutorialPassed(){if(!TutorialPassed){progress["TutorialPassed"]=new J(true);ProgressChanged();}}
    public void MarkTutorialNotPassed(){if(TutorialPassed){progress["TutorialPassed"]=new J(false);ProgressChanged();}}
    public void SetCurrentShopRegion(int region){ShopRegion=region;}
    public void SetCurrentCampaignLevel(int level){Level=level;}
    public void SetCurrentCampaignRegion(int region) {
        if(Region!=region){Region=region;OnCampaignRegionChanged?.Invoke(region);}
        prefs[source["prefs"]["region"].S]=new J(region);
        string key=StageKey(region);
        int stage=prefs[key].Has?prefs[key].I:(Math.Max(GetCompletedLevelsForRegion(region),1)-1)/10;
        SetCurrentCampaignStage(stage);
    }
    public void SetCurrentCampaignStage(int stage) {
        Stage=stage;if(stage<0)Error("invalid_stage",stage);LevelType=GetLevelType(Region,stage);Seed=unchecked(Region*stage-1);
        prefs[StageKey(Region)]=new J(stage);Save();OnCampaignSelectionChanged?.Invoke();
    }
    public static int GetStageForLevel(int level)=>Math.Max(0,unchecked(level-1)/10);
    public static int GetLevelType(int region,int stage) {
        if(stage<0)return 0;
        if(stage<=4)return stage;
        if(stage==49)return 4; // ARM64 0x36617bc MOV W19,#4; Ghidra's ELF address is misleading.
        return new System.Random(unchecked(region*stage-1)).Next(5);
    }
    J GetRegionOrNull(int region) {
        foreach(var entry in progress["Campaign"]["Regions"].A)if(entry["RegionId"].I==region)return entry;
        return null;
    }
    J GetOrCreateRegion(int region) {
        var entry=GetRegionOrNull(region);if(entry!=null)return entry;
        entry=J.O("RegionId",region,"Levels",new J(new List<J>()));progress["Campaign"]["Regions"].A.Add(entry);return entry;
    }
    public int GetCompletedLevelsForRegion(int region)=>GetRegionOrNull(region)?["Levels"].A.Count??0;
    // Native GetCurrentRank returns (index, RankConfig). This scalar API exposes
    // its index for ItemManager; the points are saved data, not a server result.
    public int GetCurrentRank()=>GetRankForPoints(progress["Ranked"]["CurrentRankPoints"].I);
    public int GetRankForPoints(int points) {
        var ranks=source["rank_config"].A;
        for(int i=ranks.Count-1;i>=0;i--)if(points>=ranks[i]["RankStartsAt"].I)return i;
        return 0;
    }
    public int GetStarsForLevel(int region,int zeroBasedLevel) {
        var list=GetRegionOrNull(region)?["Levels"].A;
        return list!=null&&zeroBasedLevel>=0&&zeroBasedLevel<list.Count?unchecked((byte)list[zeroBasedLevel].I):0;
    }
    public int GetTotalStarsEarned(int region) {
        int total=0;var entry=GetRegionOrNull(region);if(entry!=null)foreach(var stars in entry["Levels"].A)total=unchecked(total+(byte)stars.I);return total;
    }
    public bool IsStageFullyCompleted(int region,int stage)=>unchecked(stage*10+10)<=GetCompletedLevelsForRegion(region);
    public int GetStagesUnlockedCount(int region){int count=1;while(IsStageFullyCompleted(region,count-1)){count++;if(count==100)return 100;}return count;}
    public int GetLevelsUnlockedInStage(int region,int stage) {
        int completed=unchecked(GetCompletedLevelsForRegion(region)-stage*10);
        completed=Math.Max(0,Math.Min(completed,10));return Math.Min(completed,9)+1;
    }
    public byte OnLevelCompleted(int region,int zeroBasedLevel,byte stars) {
        if(unchecked((byte)(stars-1))>=3){Error("invalid_stars",stars);return 0;}
        var entry=GetOrCreateRegion(region);var levels=entry["Levels"].A;byte previous=0;
        if(unchecked((uint)zeroBasedLevel)>999)Error("invalid_level_index",zeroBasedLevel);
        else if(zeroBasedLevel==levels.Count)levels.Add(new J(stars));
        else if(zeroBasedLevel<levels.Count) {
            previous=unchecked((byte)levels[zeroBasedLevel].I);if(stars>previous)levels[zeroBasedLevel]=new J(stars);
        } else Error("cannot_skip_level",zeroBasedLevel,levels.Count);
        // The native caller emits even if RecordLevelStars rejected the index.
        if(previous<stars)ProgressChanged();return previous;
    }
    public int CalculateCampaignReward(byte stars,bool doubled)=>CalculateCampaignRewardAt(Region,Level,stars,doubled);
    int CalculateCampaignRewardAt(int region,int level,byte stars,bool doubled) {
        J config=source["wallet"];
        if(!config.Has){Error("missing_wallet_config");return 0;}
        if(stars==0)return 0;
        if(stars>config["campaignNormalStarRewards"].A.Count){Error("invalid_reward_stars",stars);return 0;}
        var regionConfig=source["regions"][region.ToString()];
        if(!regionConfig.Has)throw new InvalidOperationException("Original campaign region configuration missing: "+region);
        string table=level==regionConfig["number_of_levels"].I?"campaignFinaBossRewards":level==500?"campaignBossRewards":level%10==0?"campaignMiniBossRewards":"campaignNormalStarRewards";
        if(config[table].A.Count<stars)throw new InvalidOperationException("Original wallet reward table is incomplete: "+table);
        int reward=config[table][stars-1].I;return doubled?unchecked(reward*config["doubledMultiplier"].I):reward;
    }
    public int AwardCampaignReward(byte previousStars,byte starsEarned,bool doubled) {
        if(starsEarned<=previousStars)return 0;
        int reward=CalculateCampaignReward(starsEarned,doubled);if(reward<1)return 0;
        wallet["Gold"]=new J(unchecked(Gold+reward));WalletChanged();return reward;
    }
    public int AwardCampaignRewardBonus(byte stars) {
        int reward=unchecked(CalculateCampaignReward(stars,true)-CalculateCampaignReward(stars,false));if(reward<1)return 0;
        wallet["Gold"]=new J(unchecked(Gold+reward));WalletChanged();return reward;
    }
    public OriginalCampaignOutcome RecordCampaignWin(byte stars) {
        byte previous=OnLevelCompleted(Region,unchecked(Level-1),stars);
        var result=new OriginalCampaignOutcome {StarsEarned=stars,PreviousStars=previous,NewStars=Math.Max(stars-previous,0),
            Coins=unchecked(CalculateCampaignReward(stars,false)-CalculateCampaignReward(previous,false)),Region=Region,Level=Level};
        LastCampaignWinResult=result;return result;
    }
    // MatchEndHandler uses absolute slot-0 HP. Ordered <= tests preserve its
    // NaN branch behavior; the host owns the once-per-match settlement guard.
    public static byte StarsForVictory(float absoluteHealth) {
        byte stars=3;if(absoluteHealth<=66)stars=2;if(absoluteHealth<=33)stars=1;return stars;
    }
    public OriginalCampaignOutcome RecordVictory(float absoluteHealth) {
        var result=RecordCampaignWin(StarsForVictory(absoluteHealth));result.EndingHealth=absoluteHealth;
        result.GoldAwarded=AwardCampaignReward((byte)result.PreviousStars,(byte)result.StarsEarned,false);
        OnCampaignMatchWon?.Invoke(Level,(byte)result.StarsEarned);return result;
    }
    public OriginalUIPopupResult CreatePopupResult(OriginalCampaignOutcome outcome,OriginalCampaignPopupServices services) {
        if(outcome==null)throw new ArgumentNullException(nameof(outcome));
        return new OriginalUIPopupResult {StarsEarned=outcome.StarsEarned,PreviousStars=outcome.PreviousStars,NewStars=outcome.NewStars,Coins=outcome.Coins,
            CampaignProgressForNextButtons=GetCompletedLevelsForRegion(Region),AdsRemoved=services?.AdsRemoved?.Invoke()??false,
            CalculateReward=doubled=>CalculateCampaignReward((byte)outcome.StarsEarned,doubled),
            AwardRewardBonus=()=>AwardCampaignRewardBonus((byte)outcome.StarsEarned),
            RefreshNewItemsPips=services?.RefreshNewItemsPips,GetNewItemsCount=services?.GetNewItemsCount,
            RequestRewardedVideo=services?.RequestRewardedVideo??((success,cancel)=>cancel?.Invoke()),
            SetGamepadNextEnabled=services?.SetGamepadNextEnabled};
    }
}

public sealed class OriginalCampaignOutcome {
    public int StarsEarned,PreviousStars,NewStars,Coins,GoldAwarded,Region,Level;
    public float EndingHealth;
    public bool HasWinnings=>Coins>0;
}

// Inventory alerts, real ad entitlement/completion and input focus belong to
// their actual local/external owners. No account or advertising data is made up.
public sealed class OriginalCampaignPopupServices {
    public Action RefreshNewItemsPips;
    public Func<int> GetNewItemsCount;
    public Func<bool> AdsRemoved;
    public Action<Action,Action> RequestRewardedVideo;
    public Action<bool> SetGamepadNextEnabled;
}
