using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Jianghu {
public partial class JianghuApp {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootUINavigationSmoke(){
        if(GameClient.Arg("-uiNavigationSmoke")=="")return;
        if(GameClient.Arg("-saveDir")==""){Debug.LogError("UI acceptance requires an isolated save");Application.Quit(2);return;}
        new GameObject("UI navigation acceptance").AddComponent<UINavigationSmokeRunner>();
    }
    internal IEnumerator VerifyUINavigation(){
        string output=GameClient.Arg("-uiNavigationSmoke");Directory.CreateDirectory(output);
        var checks=new List<J>();var errors=new List<string>();
        Application.LogCallback capture=(s,t,k)=>{if(k==LogType.Error||k==LogType.Exception)errors.Add(s+"\n"+t);};
        Application.logMessageReceived+=capture;
        Action<string,bool> check=(name,passed)=>{checks.Add(J.O("check",name,"passed",passed));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());};
        float deadline=Time.realtimeSinceStartup+100;
        while((!Client.Ready||W["npcs"].A.Count<2)&&Time.realtimeSinceStartup<deadline)yield return null;
        check("isolated world ready",Client.Ready&&W["npcs"].A.Count>=2);
        if(Client.Ready&&W["npcs"].A.Count>=2){
            yield return new WaitForSecondsRealtime(.5f);
            int month=W["month"].I,id=W["npcs"][0]["id"].I;
            double money=W["player"]["wealth"].N,points=Hero["points"].N;
            string heroBefore=Hero.ToString();J original=new J();
            yield return Client.Request("/api/npc/"+id,null,j=>original=j);
            Go(0);ResetRosterFilters();rosterMode="人物";showFilters=false;
            yield return CharacterPicture(output,"01-world-map");
            showFilters=true;yield return CharacterPicture(output,"02-roster-filters");
            choiceOptions=new[]{"全部城镇"}.Concat(W["locations"].A.Select(p=>p["name"].S)).ToArray();
            choiceKey=string.Join("\u001f",choiceOptions);choiceCurrent=choiceOptions[0];choiceAnchor=new Vector2(340,220);
            yield return CharacterPicture(output,"03-searchable-choice");
            CloseChoices();choiceCaption="";showFilters=false;
            TalkToPerson(id);yield return WaitForCharacter(id);OpenPersonScreen();personTab=4;
            yield return CharacterPicture(output,"04-person-profile");
            yield return Client.Request("/api/events",null,j=>Events=j);
            check("test world includes real event records",Events.A.Count>0);
            if(Events.A.Count>0){
                int record=Events[0]["id"].I;
                OpenEvent(record);yield return new WaitForSecondsRealtime(.6f);
                check("profile links load the requested real record",Page==6&&!personScreen&&EventDetail["id"].I==record&&bookTrail.Count==1);
                yield return CharacterPicture(output,"05-record");
                OpenLinkedPerson(id);yield return WaitForCharacter(id);
                check("event person link visibly opens that profile",personScreen&&bookPersonId==id&&bookTrail.Count==2);
                CloseBook();yield return new WaitForSecondsRealtime(.3f);
                check("back restores record rather than losing it",Page==6&&!personScreen&&EventDetail["id"].I==record);
                CloseBook();yield return WaitForCharacter(id);
                check("back restores original profile and section",personScreen&&bookPersonId==id&&personTab==4);
            }
            if(W["factions"].A.Count>0){
                int faction=W["factions"][0]["id"].I;
                OpenInstitution(false,faction);yield return new WaitForSecondsRealtime(.7f);
                check("profile links load real faction detail",Page==4&&Institution["id"].I==faction&&institutionKind=="门派");
                yield return CharacterPicture(output,"06-institution");
                CloseBook();yield return WaitForCharacter(id);
                check("faction back restores inspected character",personScreen&&bookPersonId==id);
            }
            OpenOwnInventory(id);yield return new WaitForSecondsRealtime(.7f);
            check("gift view explicitly owns player items",!InspectingNpc&&giftTarget==id&&CharacterInventory["owner"].S=="player");
            if(Inventory["items"].A.Count>0&&Events.A.Count>0){
                J item=Inventory["items"][0];selectedItem=item["id"].S;itemQuery=item["name"].S;itemCategory=item["category_label"].S;itemGrade=item["grade"].I;itemScroll=new Vector2(0,24);
                OpenEvent(Events[0]["id"].I);yield return new WaitForSecondsRealtime(.4f);
                OpenLinkedPerson(id);yield return WaitForCharacter(id);CloseBook();CloseBook();
                check("nested record browsing restores inventory owner and filters",Page==2&&!InspectingNpc&&selectedItem==item["id"].S&&itemQuery==item["name"].S&&itemCategory==item["category_label"].S&&itemGrade==item["grade"].I);
                itemQuery="";itemCategory="全部";itemGrade=0;itemScroll=Vector2.zero;
            }
            yield return CharacterPicture(output,"07-inventory");
            NavigateBookTab(7);aiTab=0;yield return CharacterPicture(output,"08-model-overview");
            aiTab=1;yield return CharacterPicture(output,"09-model-config");
            aiTab=2;yield return CharacterPicture(output,"10-model-records");
            NavigateBookTab(8);settingsTab=0;yield return CharacterPicture(output,"11-save-current");
            settingsTab=1;importPath="missing-world.sqlite";yield return CharacterPicture(output,"12-save-import");
            settingsTab=2;newPopulation="invalid";yield return CharacterPicture(output,"13-new-world-validation");newPopulation="1800";
            ReturnToStage();check("explicit map return clears contextual back targets",Page==0&&!personScreen&&!dialogueOpen&&bookTrail.Count==0&&dialogueReturnPerson<0);
            if(Hero["created"].B){
                SelectedPlace=Hero["place"].I;Go(1);
                deadline=Time.realtimeSinceStartup+20;while((cityLoading||!City.Has)&&Time.realtimeSinceStartup<deadline)yield return null;
                yield return CharacterPicture(output,"14-city-map");
                choiceKey="city-modal-test";
                check("open choice prevents keyboard city movement",!CityMovementAllowed);CloseChoices();
                SwitchBookPerson(-1);inventoryMode="货栈";OpenCharacterPage(2);yield return new WaitForSecondsRealtime(.6f);
                check("city trading is owned by player",!InspectingNpc&&inventoryMode=="货栈"&&CharacterInventory["owner"].S=="player");
                ReturnToStage();check("city book returns to city map",Page==1&&!personScreen);
            }
            Go(0);advanceOpen=true;yield return CharacterPicture(output,"15-world-time");advanceOpen=false;
            yield return Client.Refresh();yield return Client.Request("/api/npc/"+id,null,j=>Person=j);
            check("all browsing preserves world month money and actions",W["month"].I==month&&Math.Abs(W["player"]["wealth"].N-money)<.001&&Math.Abs(Hero["points"].N-points)<.001);
            check("browsing preserves full hero state",Hero.ToString()==heroBefore);
            check("browsing preserves NPC inventory and combat state",Person["possessions"].ToString()==original["possessions"].ToString()&&Person["martial_view"].ToString()==original["martial_view"].ToString());
        }
        check("no native UI exceptions",errors.Count==0);
        Application.logMessageReceived-=capture;bool passed=checks.All(c=>c["passed"].B);
        File.WriteAllText(Path.Combine(output,"acceptance.json"),J.O("passed",passed,"checks",new J(checks),"errors",new J(errors.Select(s=>new J(s)).ToList())).ToString());
        if(GameClient.Arg("-smokeHold")!="true")Application.Quit(passed?0:2);
    }
}
public class UINavigationSmokeRunner : MonoBehaviour {
    IEnumerator Start(){yield return null;var app=FindAnyObjectByType<JianghuApp>();if(app!=null)yield return app.VerifyUINavigation();else Application.Quit(2);}
}
}
