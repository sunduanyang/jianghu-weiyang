using UnityEngine;

// CharacterEnemyAI had a distinct coroutine scope. DOTween ran from its
// global component, including when a hand relayer was disabled for a 2H clip.
public sealed class OriginalAIExecution : MonoBehaviour {
    public OriginalHandAnimations[] relays;
    void Update() {
        if(relays==null)return;
        foreach(var relay in relays)if(relay!=null)relay.AdvanceOriginalTweens();
    }
}
