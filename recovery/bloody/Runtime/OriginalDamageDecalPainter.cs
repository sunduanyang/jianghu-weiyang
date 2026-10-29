using Jianghu;
using UnityEngine;

// DamageDecalPainter 0x36c4f7c..0x36c5184. Original inactive blood-gush
// nodes become active through BodyPart.objectsToActivate after severing.
public sealed class OriginalDamageDecalPainter : MonoBehaviour {
    OriginalEffects effects;
    float interval;
    int totalCycles,bloodOnCharacterCycles,remaining;
    public void Initialize(OriginalEffects owner,J config) {
        effects=owner;interval=(float)config["interval"].N;totalCycles=config["totalCycles"].I;bloodOnCharacterCycles=config["bloodOnCharacterCycles"].I;
        if(isActiveAndEnabled)Begin();
    }
    void OnEnable(){if(effects!=null)Begin();}
    void Begin(){CancelInvoke();remaining=totalCycles;InvokeRepeating(nameof(BloodCycles),0,interval);}
    void BloodCycles() {
        if(remaining<=0){CancelInvoke();return;}
        if(remaining>totalCycles-bloodOnCharacterCycles)effects.DoVfx(1,transform.position);
        effects.DoVfx(2,transform.position);remaining--;
    }
    void OnDisable(){CancelInvoke();}
}
