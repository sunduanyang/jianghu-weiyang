using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

// Port state and save contracts are documented in BLOODY_SOURCE_PORT_VALIDATION.md.
public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        const float SourceImpaleInitialWait=.025f,SourceImpaleMaskWait=.25f;
        static J sourceOrganData;
        readonly Dictionary<string,DuelSourceImpaleOrgan> sourceOrgans=new Dictionary<string,DuelSourceImpaleOrgan>();
        readonly Dictionary<string,FixedJoint2D> sourceImpaleJoints=new Dictionary<string,FixedJoint2D>();
        readonly Dictionary<Collider2D,SourceImpaleContact> sourceImpaleContacts=new Dictionary<Collider2D,SourceImpaleContact>();
        sealed class SourceImpaleContact {public Limb item;public string slot;public DuelReferenceRules.Surface surface;}
        float sourceAccumulatedDamage,sourceImpaleAt,sourceImpaleDuration;
        int sourceImpaleStage;
        string sourceImpaleOrgan="",sourceImpaleSlot="";
        Fighter sourceImpaleAttacker;
        J sourceImpalePending=new J();
        J sourceImpaleSettings;
        public int SourceImpalementStage=>sourceImpaleStage;
        public float SourceAccumulatedDamage=>sourceAccumulatedDamage;
        public int SourceOrganTriggerCount=>sourceOrgans.Count;
        public event Action<string,string,Vector2> SourceImpalementStarted;

        // Call after the source graph and hand items exist. IDs passed to the
        // delegates are raw component IDs / Transform IDs, never GameObject IDs.
        // The source path reuses its colliders. Geometry fallback is separate.
        public void BuildSourceImpalement(string sourceId=null,Func<int,Component> component=null,Func<int,Transform> transform=null){
            if(sourceOrgans.Count!=0)return;
            if(sourceOrganData==null){var text=Resources.Load<TextAsset>("CombatSource/organs");if(text==null)throw new InvalidOperationException("Missing extracted organ data");sourceOrganData=J.Parse(text.text);}
            sourceId=sourceId??"00af04d95140a4b44ac8453d91c032ea";
            var source=sourceOrganData["rigs"][sourceId];if(!source.Has)throw new InvalidOperationException("Unknown source organ rig "+sourceId);
            sourceImpaleSettings=source["settings"];
            foreach(var row in source["organs"].A){
                var c=component?.Invoke(row["collider"]["id"].I) as Collider2D;
                bool raw=c!=null;
                if(!raw)c=BuildAdaptedSourceOrgan(row);
                var organ=c.gameObject.AddComponent<DuelSourceImpaleOrgan>();
                organ.owner=this;organ.key=row["key"].S;organ.linked=row["linked"].S;organ.shape=c;
                organ.body=raw?component(row["rigidbody"].I) as Rigidbody2D:referenceBones[row["body"].S].rb;
                if(organ.body==null)throw new InvalidOperationException("Organ joint rigidbody is missing");
                organ.masks=row["masks"].A.Select(m=>transform?.Invoke(m.I)?.gameObject).Where(m=>m!=null).ToArray();
                organ.SetMasks(false);sourceOrgans[organ.key]=organ;
                if(!colliders.Contains(c))colliders.Add(c);
            }
            BindAdaptedSourceImpaleItem(sword,"Near",false);
            if(shield!=null)BindAdaptedSourceImpaleItem(shield,"Far",HasShield);
        }
        Collider2D BuildAdaptedSourceOrgan(J row){
            var bone=referenceBones[row["linked"].S];var matrix=row["local_matrix"].A;
            // Raw local geometry is relative to the linked body. Limb.Frame is
            // a render/end-point frame and must not replace the actual rb.
            var go=new GameObject("Source organ "+row["key"].S);go.layer=10;go.transform.SetParent(bone.rb.transform,false);
            float a=(float)matrix[0].N,b=(float)matrix[1].N,c=(float)matrix[2].N,d=(float)matrix[3].N;
            go.transform.localPosition=new Vector3(facing*(float)matrix[4].N,(float)matrix[5].N,0);
            go.transform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(b,a)*Mathf.Rad2Deg*facing);
            go.transform.localScale=new Vector3(Mathf.Sqrt(a*a+b*b),Mathf.Sqrt(c*c+d*d),1);
            var shape=go.AddComponent<CapsuleCollider2D>();var data=row["collider"]["data"];
            shape.size=new Vector2((float)data["m_Size"]["x"].N,(float)data["m_Size"]["y"].N);
            shape.offset=new Vector2(facing*(float)data["m_Offset"]["x"].N,(float)data["m_Offset"]["y"].N);
            shape.direction=(CapsuleDirection2D)data["m_Direction"].I;shape.isTrigger=true;return shape;
        }
        void BindAdaptedSourceImpaleItem(Limb item,string slot,bool isShield){
            if(item==null)return;
            var spec=item.strikeProfile??weapon;
            foreach(var shape in item.Frame.GetComponents<Collider2D>()){
                if(sourceImpaleContacts.ContainsKey(shape))continue;
                bool sharp=shape==item.shape&&!isShield&&spec.id!="unarmed"&&spec.id!="mace"&&spec.id!="warhammer";
                var surface=sharp?(spec.id=="spear"?DuelReferenceRules.Surface.MetalSpike:DuelReferenceRules.Surface.MetalBlade):DuelReferenceRules.Surface.MetalBlunt;
                BindSourceImpaleWeapon(shape,item,surface,slot);
            }
        }
        // A weapon importer may replace the compatibility material mapping per
        // collider. Handles, pommels and shields must not inherit a blade flag.
        public void BindSourceImpaleWeapon(Collider2D shape,Limb item,DuelReferenceRules.Surface surface,string slot="Near"){
            if(shape==null||item==null||slot!="Near"&&slot!="Far")throw new ArgumentException("Invalid impalement weapon binding");
            sourceImpaleContacts[shape]=new SourceImpaleContact{item=item,surface=surface,slot=slot};
            var bridge=shape.GetComponent<DuelSourceImpaleWeapon>();if(bridge==null)bridge=shape.gameObject.AddComponent<DuelSourceImpaleWeapon>();bridge.owner=this;
            if(sourceImpaleJoints.ContainsKey(slot))return;
            var joint=item.rb.gameObject.AddComponent<FixedJoint2D>();joint.enabled=false;
            joint.autoConfigureConnectedAnchor=true;joint.anchor=Vector2.zero;joint.enableCollision=false;joint.frequency=0;joint.dampingRatio=0;
            sourceImpaleJoints[slot]=joint;
        }
        // Source item importers should preserve the serialized impaling joint
        // itself. If only numeric data is imported, apply it with Configure.
        public void BindSourceImpaleJoint(string slot,FixedJoint2D sourceJoint){
            if(sourceJoint==null||slot!="Near"&&slot!="Far")throw new ArgumentException("Invalid source impalement joint");
            if(sourceImpaleJoints.TryGetValue(slot,out var previous)&&previous!=null&&previous!=sourceJoint){previous.enabled=false;UnityEngine.Object.Destroy(previous);}
            sourceJoint.enabled=false;sourceImpaleJoints[slot]=sourceJoint;
        }
        public void ConfigureSourceImpaleJoint(string slot,J data){
            if(!sourceImpaleJoints.TryGetValue(slot,out var joint)||joint==null||data==null||!data.Has)throw new ArgumentException("Missing source impalement joint data");
            joint.enabled=false;joint.anchor=new Vector2((float)data["m_Anchor"]["x"].N,(float)data["m_Anchor"]["y"].N);
            joint.autoConfigureConnectedAnchor=data["m_AutoConfigureConnectedAnchor"].B;joint.enableCollision=data["m_EnableCollision"].B;
            joint.frequency=(float)data["m_Frequency"].N;joint.dampingRatio=(float)data["m_DampingRatio"].N;
        }
        public bool SourceWeaponImpaling(string slot)=>sourceImpaleJoints.TryGetValue(slot,out var joint)&&joint!=null&&joint.enabled;
        void ResetSourceImpactWindow(){sourceAccumulatedDamage=0;}
        void AccumulateSourceImpact(float globalDamage){if(globalDamage>0)sourceAccumulatedDamage+=globalDamage;}
        public bool ReceiveSourceOrganOverlap(DuelSourceImpaleOrgan organ,Collider2D shape){
            if(!Reference||organ==null||organ.owner!=this||!game.Live)return false;
            var bridge=shape.GetComponent<DuelSourceImpaleWeapon>();var attacker=bridge==null?null:bridge.owner;
            if(attacker==null||attacker==this||!attacker.sourceImpaleContacts.TryGetValue(shape,out var contact))return false;
            if(contact.surface!=DuelReferenceRules.Surface.MetalBlade&&contact.surface!=DuelReferenceRules.Surface.MetalSpike)return false;
            if(severed.Contains(organ.linked)||health>10)return false;
            if(game.Clock>referenceImpactDeadline)ResetSourceImpactWindow();
            if(sourceAccumulatedDamage<(float)sourceImpaleSettings["impaleDamageThreshold"].N)return false;
            // The APK stops the previous coroutine without its cleanup. Resolve
            // its joint/masks here before replacing it so saves cannot strand a
            // permanent fixed joint. This lifecycle repair is explicit.
            ClearSourceImpalement();
            if(!attacker.sourceImpaleJoints.TryGetValue(contact.slot,out var joint)||joint==null)return false;
            foreach(var victim in new[]{game.player,game.enemy})if(victim!=null&&victim!=this&&victim.sourceImpaleAttacker==attacker&&victim.sourceImpaleSlot==contact.slot)victim.ClearSourceImpalement();
            sourceImpaleDuration=(float)sourceImpaleSettings["impaleDurationBase"].N+sourceAccumulatedDamage*(float)sourceImpaleSettings["impaleDurationPerDamage"].N;
            float remaining=health;ApplyReferenceGlobalDamage(remaining);BeginReferenceDeath();
            joint.autoConfigureConnectedAnchor=true;joint.connectedBody=organ.body;joint.enabled=true;
            sourceImpaleAttacker=attacker;sourceImpaleSlot=contact.slot;sourceImpaleOrgan=organ.key;
            sourceImpaleStage=1;sourceImpaleAt=game.Clock+SourceImpaleInitialWait;organ.SetMasks(true);
            game.Impact(organ.transform.position,false,Mathf.Max(1,remaining));
            SourceImpalementStarted?.Invoke(organ.key,contact.slot,organ.transform.position);return true;
        }
        // Runs before StepReference's dead/passed-out early return. Drop and
        // death do not cancel the original weapon joint; its timer still runs.
        public void TickSourceImpalement(){
            if(game.referenceSubstep!=0||sourceImpaleStage==0)return;
            if(sourceImpaleAttacker==null||!sourceOrgans.TryGetValue(sourceImpaleOrgan,out var organ)||organ==null||!organ.gameObject.activeInHierarchy){ClearSourceImpalement();return;}
            if(game.Clock<sourceImpaleAt)return;
            if(sourceImpaleStage==1){sourceImpaleStage=2;sourceImpaleAt=game.Clock+sourceImpaleDuration;}
            else if(sourceImpaleStage==2){EndSourceImpaleJoint();sourceImpaleStage=3;sourceImpaleAt=game.Clock+SourceImpaleMaskWait;}
            else ClearSourceImpalement();
        }
        void EndSourceImpaleJoint(){
            if(sourceImpaleAttacker!=null&&sourceImpaleAttacker.sourceImpaleJoints.TryGetValue(sourceImpaleSlot,out var joint)&&joint!=null){joint.enabled=false;joint.connectedBody=null;}
        }
        public void ClearSourceImpalement(){
            EndSourceImpaleJoint();foreach(var organ in sourceOrgans.Values)if(organ!=null)organ.SetMasks(false);
            sourceImpaleStage=0;sourceImpaleAt=sourceImpaleDuration=0;sourceImpaleOrgan=sourceImpaleSlot="";sourceImpaleAttacker=null;sourceImpalePending=new J();
        }
        public void ClearSourceImpalementLifecycle(){
            ClearSourceImpalement();foreach(var victim in new[]{game.player,game.enemy})if(victim!=null&&victim.sourceImpaleAttacker==this)victim.ClearSourceImpalement();
            foreach(var joint in sourceImpaleJoints.Values)if(joint!=null){joint.enabled=false;joint.connectedBody=null;}
        }
        public J CaptureSourceImpalement(){
            float accumulated=game.Clock>referenceImpactDeadline?0:sourceAccumulatedDamage;
            var result=J.O("version",1,"accumulated_damage",accumulated,"stage",sourceImpaleStage,"wait",sourceImpaleStage==0?0:Mathf.Max(0,sourceImpaleAt-game.Clock),"duration",sourceImpaleDuration,"organ",sourceImpaleOrgan,
                "attacker",sourceImpaleStage==0?-1:sourceImpaleAttacker==game.player?0:1,"slot",sourceImpaleSlot);
            if(sourceImpaleStage>0&&sourceImpaleAttacker.sourceImpaleJoints.TryGetValue(sourceImpaleSlot,out var joint))result["joint"]=J.O("anchor_x",joint.anchor.x,"anchor_y",joint.anchor.y,"connected_x",joint.connectedAnchor.x,"connected_y",joint.connectedAnchor.y);
            return result;
        }
        public void RestoreSourceImpalement(J state){
            ClearSourceImpalement();sourceAccumulatedDamage=(float)state["accumulated_damage"].N;
            if(state.Has&&state["stage"].I>0)sourceImpalePending=state;
        }
        // Both fighters and their complete body poses must be restored first.
        public void ResolveSourceImpalement(){
            if(!sourceImpalePending.Has)return;var state=sourceImpalePending;sourceImpalePending=new J();
            if(!sourceOrgans.TryGetValue(state["organ"].S,out var organ))throw new InvalidOperationException("Saved organ is missing");
            var attacker=state["attacker"].I==0?game.player:game.enemy;
            if(attacker==null||attacker==this||!attacker.sourceImpaleJoints.TryGetValue(state["slot"].S,out var joint))throw new InvalidOperationException("Saved impalement weapon is missing");
            sourceImpaleAttacker=attacker;sourceImpaleOrgan=organ.key;sourceImpaleSlot=state["slot"].S;sourceImpaleStage=state["stage"].I;
            sourceImpaleAt=game.Clock+(float)state["wait"].N;sourceImpaleDuration=(float)state["duration"].N;
            var anchors=state["joint"];joint.enabled=false;joint.connectedBody=organ.body;joint.autoConfigureConnectedAnchor=false;
            joint.anchor=new Vector2((float)anchors["anchor_x"].N,(float)anchors["anchor_y"].N);joint.connectedAnchor=new Vector2((float)anchors["connected_x"].N,(float)anchors["connected_y"].N);
            joint.enabled=sourceImpaleStage<3;organ.SetMasks(true);
        }
    }
}

public sealed class DuelSourceImpaleOrgan:MonoBehaviour {
    [NonSerialized] public PhysicsDuel.Fighter owner;
    [NonSerialized] public Rigidbody2D body;
    [NonSerialized] public Collider2D shape;
    [NonSerialized] public GameObject[] masks=Array.Empty<GameObject>();
    public string key,linked;
    public int TriggerEnterCount {get;private set;}
    public bool MasksVisible {get;private set;}
    public void SetMasks(bool visible){MasksVisible=visible;foreach(var mask in masks)if(mask!=null)mask.SetActive(visible);}
    void OnTriggerEnter2D(Collider2D other){TriggerEnterCount++;if(owner!=null&&shape!=null&&shape.enabled)owner.ReceiveSourceOrganOverlap(this,other);}
    void OnDisable(){owner?.ClearSourceImpalement();}
}
public sealed class DuelSourceImpaleWeapon:MonoBehaviour {
    [NonSerialized] public PhysicsDuel.Fighter owner;
    void OnDisable(){owner?.ClearSourceImpalementLifecycle();}
}
