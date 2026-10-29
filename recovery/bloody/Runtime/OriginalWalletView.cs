using System;
using System.Linq;
using Jianghu;
using TMPro;
using UnityEngine;

// WalletView.Refresh 0x36b821c and original OnEnable/OnDisable ownership.
public sealed class OriginalWalletView : MonoBehaviour {
    OriginalLocalProgress wallet;
    TMP_Text[] texts;
    bool listening;
    public static OriginalWalletView Attach(Transform hud,J source,OriginalLocalProgress progress) {
        var record=source["records"].A.Single(row=>row["script"].S=="WalletView");
        const string prefix="CanvasAndCamera/";
        var driver=hud.gameObject.AddComponent<OriginalWalletView>();driver.wallet=progress;
        // The other original reference belongs to Character_Player_Menu,
        // outside this arena hierarchy. It is bound by the menu owner.
        driver.texts=record["data"]["goldTexts"].A.Where(r=>r["path"].S.StartsWith(prefix,StringComparison.Ordinal)).Select(r=> {
            var node=hud.Find(r["path"].S.Substring(prefix.Length));
            return node==null?null:node.GetComponent<TMP_Text>();
        }).ToArray();
        if(driver.texts.Length==0||driver.texts.Any(t=>t==null))throw new InvalidOperationException("Original arena wallet text missing");
        driver.OnEnable();return driver;
    }
    void OnEnable(){if(wallet==null||listening)return;wallet.OnWalletChanged+=Refresh;listening=true;Refresh();}
    void OnDisable(){if(!listening)return;wallet.OnWalletChanged-=Refresh;listening=false;}
    public void Refresh(){foreach(var text in texts)text.text="<sprite index=0>"+wallet.Gold.ToString("N0");}
}
