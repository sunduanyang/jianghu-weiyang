using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using TMPro;
using UnityEngine;

// The name-formatting portions of RoundEndView.ShowRoundEndPopup and
// MatchEndView.ShowMatchEndPopup. Ordinary MatchEnd has no winner-name list.
public sealed class OriginalVerdictText {
    readonly J library,round,match;
    readonly Dictionary<string,Transform> nodes=new Dictionary<string,Transform>();
    public string LanguageCode {get;set;}
    public OriginalVerdictText(Transform hudRoot,J uiSource,string languageCode="en") {
        if(hudRoot==null)throw new ArgumentNullException(nameof(hudRoot));
        library=OriginalHUDTextLibrary.Load();LanguageCode=languageCode;
        round=uiSource["records"].A.First(r=>r["script"].S=="RoundEndView")["data"];
        match=uiSource["records"].A.First(r=>r["script"].S=="MatchEndView")["data"];
        void Walk(Transform t,string path){nodes[path]=t;foreach(Transform c in t)Walk(c,path+"/"+c.name);}Walk(hudRoot,"CanvasAndCamera");
    }
    string Translation(string term) {
        int index=0;for(int i=0;i<library["languages"].A.Count;i++)if(string.Equals(library["languages"][i]["Code"].S,LanguageCode,StringComparison.OrdinalIgnoreCase)){index=i;break;}
        return library["terms"][term]["translations"][index].S;
    }
    void Write(J reference,string text) {
        if(!nodes.TryGetValue(reference["path"].S,out var node))throw new InvalidOperationException("Original verdict text binding missing");
        var target=node.GetComponent<TMP_Text>();if(target==null)throw new InvalidOperationException("Original verdict TMP text missing");target.text=text;
    }
    public string FormatRoundWinner(string name)=>string.Format(Translation("UI/ArenaMultiplayer/Player X Won The Round"),name);
    public string FormatPlayerLeft(string name)=>string.Format(Translation("UI/ArenaMultiplayer/Player X Left"),name);
    public void Apply(bool matchEnd,string displayedName,int gameMode=5,int matchEndReason=0) {
        if(!matchEnd) {
            foreach(var reference in round["playerXWonTheRoundTexts"].A)Write(reference,FormatRoundWinner(displayedName));
        } else if((matchEndReason==1||matchEndReason==2)&&gameMode!=4)Write(match["opponentForfeitedText"],FormatPlayerLeft(displayedName));
    }
}
