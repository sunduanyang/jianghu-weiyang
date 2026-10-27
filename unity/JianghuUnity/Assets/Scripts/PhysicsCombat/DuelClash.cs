using System.Collections.Generic;
using UnityEngine;

public sealed partial class PhysicsDuel {
    sealed class ClashState {public float eventAt=-10,contactAt=-10;public int low=-1,high=-1;}
    readonly Dictionary<ulong,ClashState> clashes=new Dictionary<ulong,ClashState>();
    readonly HashSet<ulong> queuedContacts=new HashSet<ulong>();
    readonly Dictionary<Collider2D,uint> colliderIds=new Dictionary<Collider2D,uint>();
    uint nextColliderId;
    int physicsStep;
    public int blockContacts,guardBreaks,separatingContacts;

    uint ColliderId(Collider2D collider){if(!colliderIds.TryGetValue(collider,out uint id)){id=++nextColliderId;colliderIds[collider]=id;}return id;}
    static ulong Pair(uint a,uint b)=>((ulong)a<<32)|b;
    void ResolveClash(HitPacket hit) {
        Fighter attacker=hit.weapon.owner,defender=hit.other.owner;
        blockContacts++;
        if(!clashes.TryGetValue(hit.guardPair,out var state)){state=new ClashState();clashes[hit.guardPair]=state;}
        int low=hit.other.kind=="sword"?hit.strokeLow:hit.strokeId;
        int high=hit.other.kind=="sword"?hit.strokeHigh:0;
        bool freshStroke=state.low!=low||state.high!=high;
        bool freshContact=Clock-state.contactAt>.08f;state.contactAt=Clock;
        // A tiny physical bounce does not turn one stroke into a new attack.
        bool impact=(freshStroke||freshContact)&&hit.speed>=.8f&&Clock-state.eventAt>.16f;
        if(!impact)return;
        state.eventAt=Clock;state.low=low;state.high=high;blocks++;
        bool broken=false;
        if(hit.other.kind=="shield")broken=defender.GuardImpact(hit.speed*(hit.weapon.limb.strikeProfile??attacker.weapon).pressure,false);
        else {attacker.GuardImpact(hit.speed,true);defender.GuardImpact(hit.speed,true);}
        if(broken){guardBreaks++;notice="架势被破 · 暂退一步恢复气力";noticeUntil=elapsed+1.7f;}
        else {notice="兵刃相交 · 换个角度进攻";noticeUntil=elapsed+1.3f;}
        Impact(hit.point,true,0);
    }
}
