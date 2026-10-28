using System.Collections;
using UnityEngine;
namespace Jianghu {
public sealed class CharacterModulesShowcase:MonoBehaviour {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(GameClient.Arg("-characterShowcase").Length>0)new GameObject("Character appearance gallery").AddComponent<CharacterModulesShowcase>();}
    IEnumerator Start(){
        GameClient client;JianghuApp app;
        do{yield return null;client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();}while(client==null||app==null||!client.Ready||!client.World.Has);
        bool interaction=GameClient.Arg("-characterShowcase")=="interaction";
        if(interaction){
            app.CharacterInteractionForSmoke(client.World["npcs"][0]["id"].I);
            while(!app.CharacterInteractionReadyForSmoke)yield return null;
        }else app.AppearancePreview();
        for(int i=0;i<3;i++)yield return new WaitForEndOfFrame();
        Debug.Log("CHARACTER_SHOWCASE_READY "+(interaction?"interaction":"gallery"));
    }
}
}
