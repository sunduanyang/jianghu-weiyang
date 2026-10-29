using System.Collections;
using UnityEngine;

// CharacterAnimationController.Start/cef/bv, 0x341bbcc..0x341bdd4.
// Even though speed is repeatedly set to 1, the integer random draws are
// observable by all the other original users of UnityEngine.Random.
public sealed class OriginalAnimationController : MonoBehaviour {
    Animator animator;
    public void Initialize(Animator target){animator=target;}
    void Start(){animator.speed=1;StartCoroutine(Repeat());}
    IEnumerator Repeat() {
        while(true){yield return new WaitForSeconds(Random.Range(0,5));animator.speed=1;}
    }
}
