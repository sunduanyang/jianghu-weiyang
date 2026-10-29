using System;
using UnityEngine;

// Original result fields supplied by ProgressController/WalletController.
// The local combat host must supply actual results for those game modes.
public sealed class OriginalUIPopupResult {
    public int StarsEarned,PreviousStars,NewStars,Coins,CurrentWave;
    public int CampaignProgressForNextButtons;
    public bool AdsRemoved;
    public Func<bool,int> CalculateReward;
    public Action AwardRewardBonus,RefreshNewItemsPips;
    public Func<int> GetNewItemsCount;
    public Action<Action,Action> RequestRewardedVideo;
    public Action<bool> SetGamepadNextEnabled;
    public bool HasRankedResult;
    public int RPDelta,RPBefore,RPAfter,RankIndexBefore,RankIndexAfter,GlobalRankDelta;
    public int? DisplayRankPoints;
    public int CurrentRankIndex;
    public Func<int,OriginalUIRankConfig> GetRankConfig;
}

public sealed class OriginalUIRankConfig {
    public int RankStartsAt;
    public Sprite BigBadge,SmallBadge,SmallBadgeFilled;
}

// Scalar portions of hp and both native ranked-popup tween constructors.
public static class OriginalUIPopupMath {
    public static float NudgeFill(float value,bool forceTop=false) {
        if(forceTop)return .95f;if(value>0&&value<.05f)value=.05f;return Mathf.Min(value,.95f);
    }
    public static float VisualTarget(float previousNormalized,float target,float currentSlider,bool won) {
        float visible=target>0&&target<1?NudgeFill(target):target;
        float distance=Mathf.Max(Mathf.Abs(visible-currentSlider),won?.02f:.03f);
        bool increasing=previousNormalized<target;
        visible=Mathf.Clamp01(currentSlider+(increasing?distance:-distance));
        if(increasing&&target<1)visible=Mathf.Min(visible,.95f);
        if(!increasing&&target>0&&visible<=.05f)visible=.05f;
        return visible;
    }
    public static string RP(int value)=>value.ToString("#,##0.##");
    public static string Threshold(int value)=>value>=10000?(value/1000f).ToString("0.#")+"K":RP(value);
    public static string Line(int value,int threshold)=>RP(value)+" / "+Threshold(threshold);
}
