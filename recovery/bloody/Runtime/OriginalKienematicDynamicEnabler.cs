using System.Collections;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

// KienematicDynamicEnabler.OnEnable/dut/hc, 0x36cc4ac..0x36cc730.
public sealed class OriginalKienematicDynamicEnabler : MonoBehaviour {
    Rigidbody2D[] bodies;
    bool initialized;
    public void Initialize(OriginalRig rig,J settings) {
        var resolved=new List<Rigidbody2D>();
        foreach(J reference in settings["rigidbodies"].A)resolved.Add(rig.Resolve<Rigidbody2D>(reference));
        bodies=resolved.ToArray();initialized=true;
        if(isActiveAndEnabled)StartCoroutine(Switch());
    }
    void OnEnable(){if(initialized)StartCoroutine(Switch());}
    IEnumerator Switch() {
        foreach(var body in bodies)body.bodyType=RigidbodyType2D.Kinematic;
        yield return null;yield return null;yield return null;yield return null;
        foreach(var body in bodies)body.bodyType=RigidbodyType2D.Dynamic;
    }
}
