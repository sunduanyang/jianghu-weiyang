using System;
using System.IO;
using Jianghu;
using UnityEngine;

public static partial class OriginalRecoveryBuild {
    public static void ProbeCampaign() {
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-recoveryCampaignProbeOutput");
        if(at<0||at+1>=args.Length)throw new ArgumentException("Campaign probe output path required");
        string output=Path.GetFullPath(args[at+1]);Directory.CreateDirectory(output);
        var generated=OriginalCampaignGenerator.Load().GenerateCampaign(1,1);
        bool matches=generated.Data["Name"].S=="Willow Murphy";
        var report=J.O("scope","Original level-1 generator in the matching Unity engine; reference name independently observed in the supplied APK",
            "unity",Application.unityVersion,"region",1,"level",1,"npc_seed",generated.Seed,
            "bulk",generated.BulkName,"data",generated.Data,"clothing_colors",generated.ClothingColors,
            "aggression_min",generated.AggressionMin,"aggression_max",generated.AggressionMax,"reaction_time",generated.ReactionTime,
            "draws",generated.Trace,"reference_name","Willow Murphy","reference_name_matches",matches,
            "runtime_parity_verified",false);
        File.WriteAllText(Path.Combine(output,"campaign-first-enemy.json"),report.ToString());
        Debug.Log("ORIGINAL_CAMPAIGN_FIRST_ENEMY "+generated.Data["Name"].S+" reference_match="+matches);
        if(!matches)throw new InvalidOperationException("First Campaign enemy did not match the observed APK name; inspect the native RNG path");
    }
}
