using System;
using UnityEngine;

public sealed partial class OriginalEnvironmentCustomizer {
    public readonly struct SeasonalOptions {
        public readonly bool Christmas,Spooky;
        public SeasonalOptions(bool christmas,bool spooky){Christmas=christmas;Spooky=spooky;}
        public static SeasonalOptions None=>default;
    }
    OriginalSpookyExtras spookyExtras;
    public bool CurrentIsThroneRoom {get;private set;}
    static void ValidateSpecialContext(Context context) {
        if((context.ChristmasMode||context.SpookyMode)&&!context.IsMainMenuState.HasValue)
            throw new InvalidOperationException("Seasonal lighting requires the explicit original LocalGameController.IsMainMenuState context");
        if(context.BBLevelType==4&&context.InArena&&context.CampaignStage==49&&!context.GameMode.HasValue)
            throw new InvalidOperationException("Castle stage49 requires explicit original GameMode to resolve IsThroneRoom");
    }
    static bool ResolveSpecialThroneRoom(Context context)=>OriginalEnvironmentSpecials.ResolveThroneRoom(context.BBLevelType,context.InArena,context.GameMode??-1,context.CampaignStage,context.PreviousIsThroneRoom);
    void CustomizeSpecialMoon(Context context,int hour,SpriteRenderer normalRenderer) {
        normalRenderer.enabled=OriginalEnvironmentSpecials.NormalMoonRendererEnabled(context.ChristmasMode,context.SpookyMode);
        spookyMoon.SetActive(context.SpookyMode&&!context.ChristmasMode&&OriginalEnvironmentSpecials.SpookyMoonActive(hour));
        if(OriginalEnvironmentSpecials.ShouldSetRegularMoonRoot(context.ChristmasMode,context.SpookyMode))moon.gameObject.SetActive(IsMoonVisible(hour));
        // Native refreshes in the spooky branch, and explicitly stops these
        // coroutines in the ordinary branch, before toggling buildings.
        if(context.SpookyMode) {
            if(spookyExtras==null) {
                var node=Node("spookyEnviroExtras");spookyExtras=node.GetComponent<OriginalSpookyExtras>()??node.gameObject.AddComponent<OriginalSpookyExtras>();
                spookyExtras.Initialize(catalog,instanceRoot);
            }
            spookyExtras.RefreshSpookyCoroutines(hour,true);
        }
        else if(spookyExtras!=null)spookyExtras.StopAllCoroutines();
    }
}
