using System;
using Jianghu;
using UnityEngine;

public sealed partial class OriginalRig {
    public string OriginalCharacterName {get;private set;}
    static J originalMatchConfigs;
    public static J OriginalMatchConfigs {
        get {
            if(originalMatchConfigs==null)originalMatchConfigs=J.Parse(Resources.Load<TextAsset>("OriginalMatchConfigs").text);
            return originalMatchConfigs;
        }
    }
    public void ApplyOriginalConfig(string profileName) {
        var profile=OriginalMatchConfigs["profiles"][profileName];
        if(!profile.Has)throw new ArgumentException("Original profile not found: "+profileName);
        ApplyOriginalConfig(J.Parse(profile["data"].ToString()));
    }
    public void ApplyOriginalNamedConfig(string name) {
        ApplyOriginalConfig(name.StartsWith("CharacterConfigDataSO_",StringComparison.Ordinal)?name:"CharacterConfigDataSO_"+name);
    }
    public void ApplyOriginalConfig(J data) {
        OriginalCharacterName=data["Name"].S;
        ApplyOriginalAppearance(data);
        if(data["DoubleHandedItemId"].I!=0)EquipOriginal(data["DoubleHandedItemId"].S,"None");
        else EquipOriginal(data["HandItemLeftId"].I==0?"None":data["HandItemLeftId"].S,data["HandItemRightId"].I==0?"None":data["HandItemRightId"].S);
    }
}
