using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootCharacterNavigationSmoke(){
        if(GameClient.Arg("-characterNavigationSmoke")=="")return;
        if(GameClient.Arg("-saveDir")==""){Debug.LogError("Character navigation acceptance requires an isolated save");Application.Quit(2);return;}
        new GameObject("Character navigation acceptance").AddComponent<CharacterNavigationSmokeRunner>();
    }
    internal IEnumerator VerifyCharacterNavigation(){
        var errors=new List<string>();
        Application.LogCallback capture=(s,t,k)=>{if(k==LogType.Error||k==LogType.Exception)errors.Add(s+"\n"+t);};
        Application.logMessageReceived+=capture;
        string output=GameClient.Arg("-characterNavigationSmoke");Directory.CreateDirectory(output);
        var checks=new List<J>();
        Action<string,bool> check=(name,passed)=>{checks.Add(J.O("check",name,"passed",passed));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());};
        float deadline=Time.realtimeSinceStartup+100;
        while((!Client.Ready||W["npcs"].A.Count<2)&&Time.realtimeSinceStartup<deadline)yield return null;
        check("isolated world ready",Client.Ready&&W["npcs"].A.Count>=2);
        if(Client.Ready&&W["npcs"].A.Count>=2){
            int a=W["npcs"][0]["id"].I,b=W["npcs"][1]["id"].I,month=W["month"].I;
            double wealth=W["player"]["wealth"].N,points=Hero["points"].N;
            J initialA=new J(),initialB=new J();
            yield return Client.Request("/api/npc/"+a,null,j=>initialA=j);
            yield return Client.Request("/api/npc/"+b,null,j=>initialB=j);
            yield return Client.Request("/api/inventory",null,j=>Inventory=j);
            yield return Client.Request("/api/martial",null,j=>Martial=j);
            OpenInteraction(a);
            check("clicking a person opens the interaction menu",interactWith==a&&!ViewOpen&&SelectedPerson==a);
            TalkTo(a);yield return WaitForCharacter(a);
            check("talking opens selected person's conversation",DialogueVisible&&interactWith<0&&SelectedPerson==a&&Person["id"].I==a);
            yield return CharacterPicture(output,"01-dialogue");
            ViewPerson(a,PersonTab.Overview);
            check("portrait opens same person's scroll above the conversation",TopIs(ViewKind.Person)&&FocusPersonId==a&&views.Count==2&&views[0].Kind==ViewKind.Dialogue);
            yield return CharacterPicture(output,"02-profile");
            inventoryMode="行囊";SetTab((int)PersonTab.Bag);
            check("inventory belongs to selected NPC",CurrentPersonTab==PersonTab.Bag&&CharacterInventory["owner"].S==a.ToString()&&CharacterInventory.ToString()==initialA["possessions"].ToString());
            check("NPC inventory contains no player instances",!CharacterInventory["items"].A.Any(i=>Inventory["items"].A.Any(p=>p["id"].S==i["id"].S)));
            yield return CharacterPicture(output,"03-npc-inventory");
            inventoryMode="装备";yield return CharacterPicture(output,"04-npc-equipment");
            SetTab((int)PersonTab.Martial);
            check("martial tab displays NPC's actual abilities",CharacterMartial.ToString()==initialA["martial_view"].ToString()&&!BattleVisible);
            yield return CharacterPicture(output,"05-npc-combat");
            SwitchSubject(b);yield return WaitForCharacter(b);
            check("switching NPC keeps martial tab and changes data",CurrentPersonTab==PersonTab.Martial&&FocusPersonId==b&&CharacterMartial.ToString()==initialB["martial_view"].ToString());
            SetTab((int)PersonTab.Bag);inventoryMode="行囊";
            check("switching NPC changes inventory owner",CharacterInventory["owner"].S==b.ToString());
            yield return CharacterPicture(output,"06-other-npc-inventory");
            SwitchSubject(a);yield return WaitForCharacter(a);
            check("related person links update scroll owner",FocusPersonId==a&&CharacterInventory["owner"].S==a.ToString());
            SwitchSubject(b);yield return WaitForCharacter(b);
            Back();yield return WaitForCharacter(a);
            check("closing scroll returns to original conversation",DialogueVisible&&views.Count==1&&SelectedPerson==a&&FocusPersonId==a);
            ViewPerson(a);OpenOwnInventory(a);
            check("gifting explicitly opens player's inventory",!InspectingNpc&&CurrentPersonTab==PersonTab.Bag&&CharacterInventory["owner"].S=="player"&&giftTarget==a);
            yield return CharacterPicture(output,"07-own-gift-inventory");
            SetTab((int)PersonTab.Overview);check("own scroll keeps player context",TopIs(ViewKind.Person)&&!InspectingNpc);
            yield return CharacterPicture(output,"08-own-profile");
            SetTab((int)PersonTab.Martial);yield return new WaitForSecondsRealtime(.5f);
            check("own martial keeps player context",!InspectingNpc&&CharacterMartial.ToString()==Martial["player"]["martial"].ToString());
            OpenJianghu(JianghuTab.Chronicle);
            check("cross links open above the scroll",views.Count==3&&TopIs(ViewKind.Jianghu)&&views[1].Kind==ViewKind.Person);
            ViewPerson(b);TalkTo(b);ViewPerson(a);
            check("navigation stack never repeats a view kind",views.Select(v=>v.Kind).Distinct().Count()==views.Count&&views.Count==2);
            Back();yield return WaitForCharacter(b);
            check("back returns to the conversation it came from",DialogueVisible&&FocusPersonId==b);
            Back();check("back from the last view returns to the map",!ViewOpen);
            GoStage(CurrentStage,false);TalkTo(a);ViewPerson(-1,PersonTab.Bag);Back();yield return WaitForCharacter(a);
            check("gifting scroll returns to original conversation",DialogueVisible&&FocusPersonId==a);
            TalkTo(a);TalkTo(b);yield return WaitForCharacter(b);
            yield return new WaitForSecondsRealtime(.7f);
            check("late response cannot replace newly selected NPC",SelectedPerson==b&&Person["id"].I==b&&FocusPersonId==b);
            ViewPerson(b,PersonTab.Bag);inventoryMode="行囊";
            itemQuery="不存在的物品";yield return CharacterPicture(output,"09-empty-search");itemQuery="";
            yield return Client.Refresh();
            check("browsing preserves month money and actions",W["month"].I==month&&Math.Abs(W["player"]["wealth"].N-wealth)<.001&&Math.Abs(Hero["points"].N-points)<.001);
            check("browsing preserves both NPC possessions",Person["possessions"].ToString()==initialB["possessions"].ToString());
            GoStage(Stage.World);rosterMode="名册";
            check("leaving to the map closes every view",!ViewOpen&&CurrentStage==Stage.World);
        }
        check("no native exceptions",errors.Count==0);
        Application.logMessageReceived-=capture;
        bool passed=checks.All(c=>c["passed"].B);
        File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("passed",passed,"checks",new J(checks),"errors",new J(errors.Select(s=>new J(s)).ToList())).ToString());
        if(GameClient.Arg("-smokeHold")!="true")Application.Quit(passed?0:2);
    }
    IEnumerator WaitForCharacter(int id){
        float deadline=Time.realtimeSinceStartup+15;
        while((!Person.Has||Person["id"].I!=id)&&Time.realtimeSinceStartup<deadline)yield return null;
        yield return null;
    }
    IEnumerator CharacterPicture(string output,string name){
        yield return new WaitForSecondsRealtime(.25f);yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return new WaitForSecondsRealtime(.25f);
    }
}
public class CharacterNavigationSmokeRunner : MonoBehaviour {
    IEnumerator Start(){
        yield return null;
        var app=FindAnyObjectByType<JianghuApp>();
        if(app!=null)yield return app.VerifyCharacterNavigation();
        else Application.Quit(2);
    }
}
}
