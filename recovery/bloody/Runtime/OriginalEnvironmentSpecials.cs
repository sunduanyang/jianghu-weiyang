using System;
using Jianghu;

// Source-only decisions for EnvironmentCustomizer.duf 0x36c9440 and
// Customize 0x36c83c8. The existing ordinary driver is unchanged for Build16.
public static class OriginalEnvironmentSpecials {
    // Native updates the shared flag only in the Castle branch. All other
    // level types retain the caller's current explicit global value.
    public static bool ResolveThroneRoom(int levelType,bool inArena,int gameMode,int campaignStage,bool previousIsThroneRoom) {
        return levelType==4?inArena&&gameMode==0&&campaignStage==49:previousIsThroneRoom;
    }
    public static bool PermitHolidayLighting(bool isMainMenuState,bool isThroneRoom)=>isMainMenuState||!isThroneRoom;
    public static string GraphicsKey(J region,int levelType,bool isThroneRoom,out int stageIndex) {
        stageIndex=region["data"]["stages"].A.FindIndex(stage=>stage["levelType"].I==levelType);
        if(stageIndex<0)throw new InvalidOperationException("Original special environment stage type is missing");
        return levelType==4&&isThroneRoom?"throneRoomGraphics":"stages["+stageIndex+"].graphicsContainer";
    }
    public static float CastleAmbienceValue(bool isThroneRoom)=>isThroneRoom?0f:-80f;
    // Native spooky-only branch starts at 21, whereas regular/Christmas moon
    // starts at 20. Off-hour 4 is excluded in both source byte comparisons.
    public static bool SpookyMoonActive(int hour)=>unchecked((byte)(hour-21))<239;
    public static bool RegularMoonActive(int hour)=>unchecked((uint)(hour-20))<0xfffffff0u;
    public static bool NormalMoonRendererEnabled(bool christmas,bool spooky)=>!spooky||christmas;
    public static bool ShouldSetRegularMoonRoot(bool christmas,bool spooky)=>!spooky||christmas;
    public static bool ShouldRunSpookyCoroutines(bool christmas,bool spooky)=>spooky;
}
