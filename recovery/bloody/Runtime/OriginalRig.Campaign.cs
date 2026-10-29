using System;
using Jianghu;
using UnityEngine;

public sealed partial class OriginalRig {
    J campaignAppearanceTemplate;
    public OriginalGeneratedEnemy OriginalCampaignResult {get;private set;}
    public Vector2 OriginalAIAggressionRange=>aiAggressionRange;

    // RunStartLogic/PreCustomize set the original AI traits before ApplyAll.
    // Call on the freshly initialized enemy before the first frame is yielded;
    // InitializeAI retains the original .5s equipment and 1s loop schedules.
    public void ApplyOriginalCampaignResult(OriginalGeneratedEnemy result) {
        if(result==null)throw new ArgumentNullException(nameof(result));
        if(human)throw new InvalidOperationException("Campaign NPC result requires the original enemy rig");
        if(appearanceSource==null||visuals==null||aiSource==null)throw new InvalidOperationException("Initialize the original enemy rig before applying its campaign result");
        if(aiStarted)throw new InvalidOperationException("Apply the campaign result before the original AI starts");
        if(!result.Data.Has||!result.Bulk.Has)throw new ArgumentException("Original campaign result has no config or bulk source",nameof(result));
        OriginalCampaignResult=result;
        aiAggressionRange=new Vector2(result.AggressionMin,result.AggressionMax);
        OriginalAIReactionTime=result.ReactionTime;

        // Each rig owns its overrides. OriginalVisualCatalog.Data is shared by
        // both fighters, so never change its CharacterConfigApplier record.
        if(campaignAppearanceTemplate==null)campaignAppearanceTemplate=J.Parse(appearanceSource.ToString());
        appearanceSource=J.Parse(campaignAppearanceTemplate.ToString());
        J appearance=appearanceSource["data"];
        // cey always caches all four bulk gradients; ApplySkin/Hair/BeardColor
        // select them only for a generated, non-special NPC (55c && !55d).
        // Special profiles use the original prefab gradients instead.
        if(!result.IsSpecialBoss) {
            appearance["skinColorGradient"]=result.Bulk["SkinColor"];
            appearance["skinColorSpookyGradient"]=result.Bulk["SkinColor_Spooky"];
            appearance["hairColorGradient"]=result.Bulk["HairColor"];
            appearance["beardColorGradient"]=result.Bulk["BeardColor"];
        }
        ApplyOriginalConfig(result.Data);
        if(result.IsSpecialBoss)return;

        // ApplyOriginalConfig has already selected the exact armor textures,
        // including tucked variants. Replace only the original tint here.
        foreach(string slot in new[]{"Hat","TopL1","TopL2","TopL3","Gloves","BottomL1","BottomL2","Shoes"}) {
            string field=OriginalCampaignClothingField(slot);
            int type=Array.IndexOf(new[]{"Hat","TopL1","TopL2","TopL3","TopL4","Gloves","BottomL1","BottomL2","BottomL3","Shoes"},slot)+8;
            var armor=Armor(type,result.Data[slot+"Id"].I);
            if(!armor.Has||armor["flags"]["NotTintable"].B)continue;
            var color=result.ClothingColors[field];
            if(!color.Has)throw new InvalidOperationException("Original campaign clothing color missing: "+field);
            string lower=char.ToLowerInvariant(slot[0])+slot.Substring(1);
            var paint=AppearancePaint(lower+"PaintableTexture");
            if(paint!=null)paint.Replace(paint.sourceTexture,OriginalVisualCatalog.Color(color));
        }
        // ApplyTopL1 0x3423894 / ApplyBottomL3 0x342502c: generated NPCs use
        // BottomL3Id rather than Config.TuckTopL1 for the L1 erasing shader.
        var topL1=AppearancePaint("topL1PaintableTexture");
        if(topL1!=null)topL1.target.sharedMaterial.SetFloat("_Erases_Bottom_of_L1",result.Data["BottomL3Id"].I!=0?1:0);
    }

    // Native ApplyShoes 0x3425368 deliberately reads jlo, like BottomL2.
    // jlp is still generated (and consumes RNG); TopL4/BottomL3 retain their
    // armor asset tint and have no generated-color read in the source.
    public static string OriginalCampaignClothingField(string slot) {
        switch(slot) {
            case "Hat":return "jli";
            case "TopL1":return "jlj";
            case "TopL2":return "jlk";
            case "TopL3":return "jll";
            case "Gloves":return "jlm";
            case "BottomL1":return "jln";
            case "BottomL2":case "Shoes":return "jlo";
            default:return null;
        }
    }
}
