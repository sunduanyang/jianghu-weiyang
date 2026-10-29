using System;
using System.IO;
using Jianghu;
using UnityEngine;

public sealed partial class OriginalPreview {
    public OriginalPlaytestCatalog SetupCatalog {get;private set;}
    public OriginalPlaytestSetup CurrentSetup=>setupStore.Value;
    public bool SetupMenuOpen=>setupMenu!=null&&setupMenu.IsOpen;
    OriginalPlaytestStore setupStore;
    OriginalPlaytestSetupMenu setupMenu;
    bool setupEnabled,setupPriorPaused;

    void InitializePlaytestSetupContext(string progressPath,string[] args) {
        SetupCatalog=new OriginalPlaytestCatalog();
        string settingsPath=progressPath+".setup.json";
        bool hadSettings=File.Exists(settingsPath),startup=Array.IndexOf(args,"-playtestSetup")>=0;
        setupStore=OriginalPlaytestStore.Open(settingsPath,SetupCatalog);
        setupEnabled=hadSettings||startup;
        if(!setupEnabled)return;
        if(Array.IndexOf(args,"-originalGameMode")<0)Progress.GameMode=0;
        if(Array.IndexOf(args,"-originalCampaignRegion")<0)Progress.SetCurrentCampaignRegion(CurrentSetup.Region);
        if(Array.IndexOf(args,"-originalCampaignLevel")<0)Progress.SetCurrentCampaignLevel(CurrentSetup.Level);
        if(Array.IndexOf(args,"-originalCampaignStage")<0)Progress.SetCurrentCampaignStage(OriginalLocalProgress.GetStageForLevel(Progress.Level));
    }
    void AttachPlaytestSetupMenu() {
        var args=Environment.GetCommandLineArgs();
        if(automated)return;
        foreach(string argument in args)
            if(argument.StartsWith("-validateOriginal",StringComparison.Ordinal)&&!argument.StartsWith("-validateOriginalSetup",StringComparison.Ordinal))return;
        setupMenu=OriginalPlaytestSetupMenu.Attach(this);
        if(Array.IndexOf(args,"-playtestSetup")>=0)OpenPlaytestSetup();
    }
    bool HandlePlaytestSetupKeys() {
        if(setupMenu==null)return false;
        if(Input.GetKeyDown(KeyCode.F2)) {
            if(SetupMenuOpen)ClosePlaytestSetup();else OpenPlaytestSetup();
            return true;
        }
        if(!SetupMenuOpen)return false;
        if(Input.GetKeyDown(KeyCode.Escape))ClosePlaytestSetup();
        return true;
    }
    public void OpenPlaytestSetup() {
        if(player==null||IsTransitioning||SetupMenuOpen)return;
        if(setupMenu==null)setupMenu=OriginalPlaytestSetupMenu.Attach(this);
        var draft=CurrentSetup.Clone();draft.Region=Progress.Region;draft.Level=Progress.Level;
        setupPriorPaused=Paused;SetPaused(true);
        setupMenu.Open(draft,setupStore.LoadError);
    }
    public void ClosePlaytestSetup() {
        if(!SetupMenuOpen)return;
        setupMenu.Hide();SetPaused(setupPriorPaused);
    }
    public bool StartPlaytestSetup(OriginalPlaytestSetup selected,out string error) {
        error=null;
        if(IsTransitioning){error="正在切换场景，请稍候。";return false;}
        try {
            SetupCatalog.Validate(selected);
            setupStore.Save(selected);setupEnabled=true;
            Progress.SetCurrentCampaignRegion(CurrentSetup.Region);
            Progress.SetCurrentCampaignLevel(CurrentSetup.Level);
            Progress.SetCurrentCampaignStage(OriginalLocalProgress.GetStageForLevel(CurrentSetup.Level));
            setupMenu?.Hide();ResetPointers();
            TransitionOriginalArena(0);
            return true;
        } catch(Exception failure) {
            error="无法应用配置："+failure.Message;
            return false;
        }
    }
    void ApplyPlaytestPlayerConfig() {
        if(GameMode==5||!setupEnabled)player.ApplyOriginalNamedConfig(GameMode==5?"Player_1":"Slot_0");
        else player.ApplyOriginalConfig(J.Parse(J.Write(CurrentSetup.Character)));
    }
    void RememberPlaytestLocation() {
        if(!setupEnabled||setupStore==null)return;
        var selected=CurrentSetup.Clone();selected.Region=Progress.Region;selected.Level=Progress.Level;
        setupStore.Save(selected);
    }
}
