using UnityEngine;

// LightningFlash Awake 0x3444ca4, OnEnable 0x3444d10 and Disable 0x3444d78.
// Its first activation has the native .01-second startup disable as well as
// its configured lifetime; subsequent activations use the serialized lifetime.
public sealed class OriginalLightningFlash : MonoBehaviour {
    float disableAfterXSeconds=2;
    bool initialized;
    public void Initialize(float seconds){disableAfterXSeconds=seconds;initialized=true;if(gameObject.activeInHierarchy)Invoke(nameof(Disable),disableAfterXSeconds);}
    void Awake(){Invoke(nameof(Disable),.01f);}
    void OnEnable(){if(initialized)Invoke(nameof(Disable),disableAfterXSeconds);}
    public void Disable()=>gameObject.SetActive(false);
}
