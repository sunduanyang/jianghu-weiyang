using System;
using System.Collections;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

public sealed partial class OriginalRig {
    public OriginalItem leftItem,rightItem;
    public bool doubleHanded,dead,passedOut,headDisabled,leftArmDisabled,rightArmDisabled,leftLegDisabled,rightLegDisabled;
    public float healthMax,staminaMax,totalWeight,totalDefense;
    public Transform OriginalEquipmentWeaponCOM {get;private set;}
    public Transform OriginalEquipmentShieldCOM {get;private set;}
    public Transform OriginalEquipmentChestCOM {get;private set;}
    public bool OriginalFlipped=>flipped;
    public bool GettingUp=>gettingUpUntil>Time.time;
    public event Action<OriginalImpact> ImpactOccurred;
    public event Action<OriginalRig> Died,PassedOut,Recovered;
    public event Action<OriginalBodyPart,OriginalPartMode> PartModeChanged;
    // Read-only lifecycle diagnostics; no production listener is required.
    public event Action<int> OriginalDeathStageCompleted;
    J combatSource,vitality,damageSettings,equipmentSettings;
    static J itemCatalog;
    readonly Dictionary<int,OriginalBodyPart> combatParts=new Dictionary<int,OriginalBodyPart>();
    readonly Dictionary<int,OriginalArmorPart> combatArmor=new Dictionary<int,OriginalArmorPart>();
    readonly Dictionary<string,Vector2> armorStats=new Dictionary<string,Vector2>();
    readonly List<OriginalStaminaDrainer> drainers=new List<OriginalStaminaDrainer>();
    float recharge,passOutUntil,gettingUpUntil,bleedAt=-1,impactUntil,accumulatedImpact;
    int impactCount;
    Coroutine impaling;
    public IReadOnlyCollection<OriginalBodyPart> BodyParts=>combatParts.Values;
    public OriginalBodyPart BodyPart(int id)=>combatParts.TryGetValue(id,out var part)?part:null;
    public OriginalArmorPart ArmorPart(int id)=>combatArmor.TryGetValue(id,out var part)?part:null;
    public float DamageNumber(string key)=>(float)damageSettings[key].N;
    public PhysicsMaterial2D OriginalDeadMaterial(J reference) {
        var catalog=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog");
        return catalog==null?null:catalog.Get<PhysicsMaterial2D>("sharedassets0.assets:"+reference["id"].S);
    }
    partial void InitializeCombat() {
        var text=Resources.Load<TextAsset>("OriginalCombatSource");
        if(text==null)throw new InvalidOperationException("OriginalCombatSource.json is missing");
        combatSource=J.Parse(text.text)[human?PlayerSource:EnemySource];
        var controllers=combatSource["controllers"];
        vitality=controllers["CharacterVitality"][0]["data"];
        damageSettings=controllers["CharacterDamageCalculator"][0]["data"];
        equipmentSettings=controllers["CharacterConfigApplier"][0]["data"];
        // Keep the source applier's serialized initial references. Equipment
        // changes overwrite individual caches; they never reset both first.
        OriginalEquipmentWeaponCOM=ResolveTransform(equipmentSettings["PlayerWeaponCOM"]);
        OriginalEquipmentShieldCOM=ResolveTransform(equipmentSettings["PlayerShieldCOM"]);
        OriginalEquipmentChestCOM=ResolveTransform(equipmentSettings["PlayerChestCOM"]);
        health=healthMax=(float)vitality["MaxHealth"].N;stamina=staminaMax=(float)vitality["MaxStamina"].N;
        recharge=(float)vitality["staminaRechargePerSecond"].N;
        foreach(var row in controllers["bt"].A) {
            var data=row["data"];var node=ResolveTransform(data["m_GameObject"]);if(node==null)continue;
            var armor=node.gameObject.AddComponent<OriginalArmorPart>();armor.sourceId=row["id"].I;
            // ApplyAll replaces the serialized editor placeholder defense 50
            // with the actual equipped slot's decoded defense, including 0.
            armor.defense=0;armor.material=0;combatArmor[armor.sourceId]=armor;
        }
        foreach(var row in controllers["BodyPart"].A) {
            var node=ResolveTransform(row["data"]["m_GameObject"]);if(node==null)continue;
            var part=node.gameObject.AddComponent<OriginalBodyPart>();combatParts[row["id"].I]=part;
        }
        foreach(var row in controllers["BodyPart"].A)if(combatParts.TryGetValue(row["id"].I,out var part))part.Initialize(this,row);
        foreach(var row in controllers["InternalOrgan"].A) {
            var node=ResolveTransform(row["data"]["m_GameObject"]);if(node==null)continue;
            node.gameObject.AddComponent<OriginalInternalOrgan>().Initialize(this,row["data"]);
        }
        foreach(var row in controllers["StaminaDrainer"].A)drainers.Add(new OriginalStaminaDrainer(this,row["data"]));
        // WeaponSelfColliderInitializer 0x368f414 ignores its exact collider
        // against every source body collider (including inactive variants).
        foreach(var row in controllers["WeaponSelfColliderInitializer"].A) {
            var c=Resolve<Collider2D>(row["data"]["thisCollider"]);if(c==null)continue;
            foreach(var r in row["data"]["bodyPartColliders"].A){var other=Resolve<Collider2D>(r);if(other!=null&&other!=c)Physics2D.IgnoreCollision(c,other);}
        }
    }
    partial void StepCombat(float dt) {
        if(vitality==null||dead)return;
        // CharacterVitality.FixedUpdateNetwork 0x350d424.
        if(passedOut&&Time.time>=passOutUntil) {
            passedOut=false;stamina=10;gettingUpUntil=Time.time+1;
            foreach(var p in combatParts.Values)p.SetPhysicsPassedOut(false);
            SetOriginalObjects(vitality["ExtraColliders"],true);Recovered?.Invoke(this);
        }
        if(stamina<=0&&!passedOut) {
            passedOut=true;passOutUntil=Time.time+(float)vitality["staminaEmptyTimeUntilRecover"].N;
            foreach(var p in combatParts.Values)p.SetPhysicsPassedOut(true);
            SetOriginalObjects(vitality["ExtraColliders"],false);PassedOut?.Invoke(this);
        }
        stamina=passedOut?0:Mathf.Min(staminaMax,stamina+recharge*dt);
        foreach(var d in drainers)d.Step();
        if(bleedAt>=0&&Time.time>=bleedAt) {bleedAt=Time.time+.25f;ApplyOriginalGlobalDamage(.5f);}
    }
    public void ConsumeOriginalStamina(float amount) {
        if(!dead&&!passedOut&&!GettingUp&&amount>0)stamina=Mathf.Max(0,stamina-amount);
    }
    public void ApplyOriginalGlobalDamage(float amount) {
        if(dead||amount<=0)return;
        ConsumeOriginalStamina(amount/2.5f);health=Mathf.Max(0,health-amount);
        if(health<=0)DieOriginal();
    }
    public void StartOriginalBleeding(){bleedAt=Time.time+.65f;}
    public bool RegisterOriginalImpact(){if(Time.time>impactUntil)accumulatedImpact=0;return OriginalCombatRules.RegisterImpact(Time.time,ref impactCount,ref impactUntil);}
    public void AccumulateOriginalImpact(float amount){accumulatedImpact+=amount;}
    public void PublishOriginalImpact(OriginalImpact impact)=>ImpactOccurred?.Invoke(impact);
    public void PublishOriginalPartMode(OriginalBodyPart part,OriginalPartMode mode)=>PartModeChanged?.Invoke(part,mode);
    public void DisableOriginalPart(OriginalBodyPart part) {
        switch(part.type) {
            case 0:headDisabled=true;break;
            // The original fields are cross-named: bodyPartType ArmLeft (2)
            // sets anyHandPartRSevered and vice versa (0x341a914/0x341aa54).
            case 2:rightArmDisabled=true;DropOriginalHand(0);break;
            case 3:leftArmDisabled=true;DropOriginalHand(1);break;
            case 4:leftLegDisabled=true;break;
            case 5:rightLegDisabled=true;break;
        }
        if(part.mode!=OriginalPartMode.Bashed) {
            if(leftHandCOM!=null&&leftHandCOM.transform.IsChildOf(part.transform))leftHandCOM.gameObject.SetActive(false);
            if(rightHandCOM!=null&&rightHandCOM.transform.IsChildOf(part.transform))rightHandCOM.gameObject.SetActive(false);
        }
        if(part.config["triggersAnyLegSevered"].B&&pelvisCOM!=null)pelvisCOM.mass=5;
    }
    void DieOriginal() {
        if(dead)return;dead=true;
        DisableOriginalAIUpdates();
        rootBody.bodyType=RigidbodyType2D.Static;gameObject.layer=22;
        var kick=Resolve<Collider2D>(vitality["extraColliderKick"]);if(kick!=null)kick.enabled=false;
        // cmu 0x350d948 invokes expression first, starts the coroutine
        // (whose first stage runs synchronously), then publishes OnDeath.
        if(OriginalExpression!=null)OriginalExpression.Died();
        StartCoroutine(OriginalDeathStages());Died?.Invoke(this);
    }
    IEnumerator OriginalDeathStages() {
        var overlap=Resolve<Collider2D>(vitality["overlapCollider"]);if(overlap!=null)overlap.enabled=false;
        SetOriginalObject(vitality["vaporContainer"],false);SetOriginalObjects(vitality["ExtraColliders"],false);
        OriginalDeathStageCompleted?.Invoke(0);
        yield return new WaitForSeconds(.025f);
        foreach(var p in combatParts.Values)p.SetPhysicsDead();
        OriginalDeathStageCompleted?.Invoke(1);
        yield return new WaitForSeconds(.025f);
        foreach(var r in vitality["bodyFixedJoints2D"].A) {var joint=Resolve<FixedJoint2D>(r);if(joint!=null){joint.frequency=10;joint.dampingRatio=0;}}
        OriginalDeathStageCompleted?.Invoke(2);
        yield return new WaitForSeconds(.025f);
        SetOriginalObject(vitality["weaponSelfColliderWhenDead"],true);DropOriginalHand(0);DropOriginalHand(1);
        OriginalDeathStageCompleted?.Invoke(3);
        yield return new WaitForSeconds(.025f);
        foreach(var r in vitality["fixedJoints2DToKill"].A){var joint=Resolve<FixedJoint2D>(r);if(joint!=null)joint.enabled=false;}
        OriginalDeathStageCompleted?.Invoke(4);
        yield return new WaitForSeconds(.025f);
        SetOriginalObjects(vitality["gameObjectsToDeactivateUponDeath"],false);
        OriginalDeathStageCompleted?.Invoke(5);
        yield return new WaitForSeconds(.025f);
        SetOriginalObjects(vitality["gameObjectsToActivateUponDeath"],true);
        OriginalDeathStageCompleted?.Invoke(6);
    }
    void SetOriginalObject(J r,bool active){var t=ResolveTransform(r);if(t!=null)t.gameObject.SetActive(active);}
    void SetOriginalObjects(J refs,bool active){foreach(var r in refs.A)SetOriginalObject(r,active);}
    public void DropOriginalHand(int hand) {
        // Native DropHandItem disables each hand's joint even when the same
        // two-hand item was already marked Dropped by the other hand.
        var item=hand==0?leftItem:rightItem;if(item==null)return;
        var joint=Resolve<FixedJoint2D>(equipmentSettings[hand==0?"handFixedJoint2DL":"handFixedJoint2DR"]);if(joint!=null)joint.enabled=false;
        item.transform.SetParent(null,true);
        foreach(var d in drainers)if(d.item==item&&d.isLeft==(hand==0))d.disabled=true;
        if(item.twoHanded&&!dead&&!(leftArmDisabled&&rightArmDisabled)) {
            // DropHandItem retains the other grip until both arms are lost.
            // Losing the right grip moves the visual hierarchy to left mount.
            if(hand==1){var mount=ResolveTransform(equipmentSettings["handItemL_TransformWeapon"]);if(mount!=null)item.transform.SetParent(mount,true);}
        } else item.SetDropped();
        if(item.twoHanded){var com=hand==0?leftHandCOM:rightHandCOM;if(com!=null)com.mass=.55f;}
    }
    public static J FindOriginalItem(string resource) {
        if(itemCatalog==null)itemCatalog=J.Parse(Resources.Load<TextAsset>("OriginalItemCatalog").text);
        foreach(var row in itemCatalog["items"].A)if(string.Equals(row["resource"].S,resource,StringComparison.OrdinalIgnoreCase)||string.Equals(row["name"].S,resource,StringComparison.OrdinalIgnoreCase)||row["item_id"].S==resource)return row;
        return new J();
    }
    public void EquipOriginal(string leftResource,string rightResource) {
        if(leftItem!=null)Destroy(leftItem.gameObject);if(rightItem!=null&&rightItem!=leftItem)Destroy(rightItem.gameObject);
        leftItem=rightItem=null;doubleHanded=false;
        var l=FindOriginalItem(string.IsNullOrEmpty(leftResource)?"None":leftResource);var r=FindOriginalItem(string.IsNullOrEmpty(rightResource)?"None":rightResource);
        if(l.Has&&l["two_handed"].B){leftItem=rightItem=CreateOriginalItem(l,2);doubleHanded=true;}
        else if(r.Has&&r["two_handed"].B){leftItem=rightItem=CreateOriginalItem(r,2);doubleHanded=true;}
        else {if(l.Has)leftItem=CreateOriginalItem(l,0);if(r.Has)rightItem=CreateOriginalItem(r,1);}
        foreach(var d in drainers)d.Equip(d.isLeft?leftItem:rightItem);
        UpdateOriginalEquipmentTotals();
        SetOriginalAIWeaponProperties();
    }
    public void BeginOriginalRound() {
        float delay=(float)combatSource["controllers"]["HandCannonSync"][0]["data"]["fuseDelay"].N;
        if(delay<=0)delay=5;
        var cannon=leftItem==null?null:leftItem.GetComponent<OriginalHandCannon>();if(cannon!=null)cannon.BeginRound(delay);
        if(rightItem!=null&&rightItem!=leftItem){cannon=rightItem.GetComponent<OriginalHandCannon>();if(cannon!=null)cannon.BeginRound(delay);}
    }
    OriginalItem CreateOriginalItem(J record,int hand) {
        var prefab=Resources.Load<GameObject>(record["resource"].S);
        if(prefab==null)throw new InvalidOperationException("Missing original item prefab: "+record["resource"].S);
        bool shield=record["fields"]["Type"].I==7;
        string mount=hand==2?"handItemL_TransformDoubleHandedWeapon":hand==0?(shield?"handItemL_TransformShield":"handItemL_TransformWeapon"):(shield?"handItemR_TransformShield":"handItemR_TransformWeapon");
        var parent=ResolveTransform(equipmentSettings[mount]);
        var go=Instantiate(prefab,parent,false);go.SetActive(true);
        var item=go.AddComponent<OriginalItem>();item.Initialize(this,record,hand);
        if(item.handle1!=null)go.transform.localPosition-=item.handle1.localPosition;
        if(hand==2) {
            // The original two-hand mount adds z .001 and fixes both
            // original hand joints to the two serialized handle anchors.
            var p=go.transform.localPosition;p.z-=.001f;go.transform.localPosition=p;
            BindOriginalHand(item,0,item.handle1,true);BindOriginalHand(item,1,item.handle2,true);
        } else BindOriginalHand(item,hand,item.handle1,false);
        CacheOriginalItemCOM(item,hand==2);
        item.ShieldWasHit+=ConsumeOriginalStamina;item.WeaponHitShield+=ConsumeOriginalStamina;
        return item;
    }
    void CacheOriginalItemCOM(OriginalItem item,bool two) {
        if(item.counterweightBody==null)throw new InvalidOperationException("Source equipped item has no CounterweightRigidbody2D COM: "+item.name);
        var counter=item.counterweightBody.transform;
        // Apply2H0x34231a8 writes only WeaponCOM. Single-hand Type6/7
        // writes its own cache, then0x3421ba4/0x3422a78 tests BOTH with
        // Unity Object==null and assigns BOTH Chest when either is null.
        if(two||item.OriginalEquipmentType==6)OriginalEquipmentWeaponCOM=counter;
        else if(item.OriginalEquipmentType==7)OriginalEquipmentShieldCOM=counter;
        if(!two&&(OriginalEquipmentWeaponCOM==null||OriginalEquipmentShieldCOM==null))
            OriginalEquipmentWeaponCOM=OriginalEquipmentShieldCOM=OriginalEquipmentChestCOM;
    }
    void BindOriginalHand(OriginalItem item,int hand,Transform handle,bool two) {
        string side=hand==0?"L":"R";
        var joint=Resolve<FixedJoint2D>(equipmentSettings["handFixedJoint2D"+side]);
        // ApplyHandItemLeft/Right also assign Handle_1, not only the two-hand
        // branch (native 0x3421c34 / 0x3422b08 / 0x342303c / 0x3423084).
        if(joint!=null){joint.enabled=false;joint.connectedBody=item.itemBody;if(handle!=null)joint.connectedAnchor=handle.localPosition;joint.enabled=true;}
        var hinge=Resolve<HingeJoint2D>(equipmentSettings["handHingeJoint2D"+side]);
        var dummy=Resolve<HingeJoint2D>(equipmentSettings["handHingeJoint2D"+side+(two?"_Dummy_TwoHanded":"_Dummy_OneHanded")]);
        if(hinge!=null&&dummy!=null)hinge.limits=dummy.limits;
        var com=hand==0?leftHandCOM:rightHandCOM;
        if(com!=null)com.mass=(float)equipmentSettings[item.OriginalUsesShieldMount?"handCOMShieldMass":"handCOMWeaponMass"].N;
        foreach(var r in equipmentSettings[hand==0?"armLeftRidibodies":"armRightRidibodies"].A) {var b=Resolve<Rigidbody2D>(r);if(b!=null)b.linearDamping=b.angularDamping=item.OriginalUsesShieldMount?3:5;}
    }
    // CharacterConfigApplier Apply{Slot} populates bt objects independently
    // from visual erasure/tucking. A hidden layer retains its source defense.
    public void SetOriginalArmorSlot(string slot,float defense,float weight,int material) {
        armorStats[slot]=new Vector2(defense,weight);
        foreach(var pair in equipmentSettings.D) {
            if(!pair.Key.StartsWith("ArmorPart_",StringComparison.Ordinal))continue;
            string suffix=pair.Key.Substring(pair.Key.LastIndexOf('_')+1);
            bool matches=suffix==slot||(slot.StartsWith("BottomL",StringComparison.Ordinal)&&suffix==slot.Replace("BottomL","Bottom"));
            if(matches&&combatArmor.TryGetValue(pair.Value["id"].I,out var part)){part.defense=defense;part.material=material;}
        }
        UpdateOriginalEquipmentTotals();
    }
    public void SetOriginalArmorPart(int sourceRigBtId,float defense,int material) {
        if(combatArmor.TryGetValue(sourceRigBtId,out var part)){part.defense=defense;part.material=material;}
    }
    public void SetOriginalArmorPart(string sourceField,float defense,int material) {
        SetOriginalArmorPart(equipmentSettings[sourceField]["id"].I,defense,material);
    }
    void UpdateOriginalEquipmentTotals() {
        float weight=0,defense=0;
        foreach(var pair in armorStats) {
            float w=pair.Key=="Hat"?2:pair.Key=="TopL4"?3:1;
            float d=pair.Key=="TopL1"||pair.Key=="TopL2"||pair.Key=="TopL4"||pair.Key=="BottomL1"||pair.Key=="BottomL2"?.5f:pair.Key=="TopL3"?2:1;
            weight+=pair.Value.y*w;defense+=pair.Value.x*d;
        }
        if(doubleHanded&&leftItem!=null){weight+=leftItem.weight*2;defense+=leftItem.defense*2;}
        else {if(leftItem!=null){weight+=leftItem.weight*2;defense+=leftItem.defense*2;}if(rightItem!=null){weight+=rightItem.weight*2;defense+=rightItem.defense*2;}}
        totalWeight=(float)Math.Round(weight/100*50,MidpointRounding.ToEven)/50;
        totalDefense=(float)Math.Round(defense/50*20,MidpointRounding.ToEven)/20;
        staminaMax=Mathf.Clamp01(1-totalWeight)*100;
    }
    public void TryOriginalImpalement(OriginalInternalOrgan organ,OriginalItem item) {
        if(organ.linkedPart==null||organ.linkedPart.severed||health>10)return;
        if(Time.time>impactUntil)accumulatedImpact=0;
        if(accumulatedImpact<DamageNumber("impaleDamageThreshold"))return;
        if(impaling!=null)StopCoroutine(impaling);
        impaling=StartCoroutine(OriginalImpalement(organ,item,DamageNumber("impaleDurationBase")+accumulatedImpact*DamageNumber("impaleDurationPerDamage")));
    }
    IEnumerator OriginalImpalement(OriginalInternalOrgan organ,OriginalItem item,float duration) {
        ApplyOriginalGlobalDamage(health);
        if(item.impaleJoint==null)yield break;
        item.impaleJoint.connectedBody=organ.body;item.impaleJoint.enabled=true;item.isImpaling=true;organ.Masks(true);
        yield return new WaitForSeconds(.025f);yield return new WaitForSeconds(duration);
        if(item!=null)item.EndImpalement();yield return new WaitForSeconds(.25f);
        if(organ!=null)organ.Masks(false);impaling=null;
    }
}

public sealed class OriginalInternalOrgan : MonoBehaviour {
    public OriginalRig rig;public OriginalBodyPart linkedPart;public Rigidbody2D body;J config;
    public void Initialize(OriginalRig owner,J source){rig=owner;config=source;linkedPart=rig.BodyPart(source["LinkedBodyPart"]["id"].I);body=rig.Resolve<Rigidbody2D>(source["Rigidbody2D"]);}
    void OnTriggerEnter2D(Collider2D collider) {
        if(!collider.CompareTag("HandItem"))return;
        var item=OriginalItem.From(collider);if(item==null||item.owner==rig)return;
        var material=OriginalCombatRules.Surface(collider.sharedMaterial);
        if(material==OriginalSurface.MetalBlade||material==OriginalSurface.MetalSpike)rig.TryOriginalImpalement(this,item);
    }
    public void Masks(bool enabled){foreach(var r in config["ImpaleMasks"].A){var t=rig.ResolveTransform(r);if(t!=null)t.gameObject.SetActive(enabled);}}
}

// StaminaDrainer: Invoke begins .25s after equip; original repeat .1666s.
public sealed class OriginalStaminaDrainer {
    public OriginalRig rig;public OriginalItem item;public bool isLeft,disabled;
    readonly J config;Vector2 anchor;float next,heat,drain,baseline;
    const float Period=.1666f;
    public OriginalStaminaDrainer(OriginalRig owner,J source){rig=owner;config=source;isLeft=source["isLeftHand"].B;}
    float N(string key)=>(float)config[key].N;
    public void Equip(OriginalItem value){item=value;disabled=false;heat=0;baseline=drain=item!=null&&item.twoHanded?3:4.5f;next=Time.time+.25f;if(item!=null&&item.counterweightBody!=null)anchor=item.counterweightBody.position-rig.rootBody.position;}
    public void Step() {
        if(item==null||disabled||item.dropped||item.counterweightBody==null||Time.time<next)return;
        next+=Period;
        var relative=item.counterweightBody.position-rig.rootBody.position;
        anchor=Vector2.MoveTowards(anchor,relative,N("anchorFollowSpeed")*Period);
        bool active=isLeft?rig.leftActive:rig.rightActive;
        if(active&&(item.counterweightBody.linearVelocity-rig.rootBody.linearVelocity).magnitude>N("speedThreshold")) {
            rig.ConsumeOriginalStamina(drain);
            if(Vector2.Distance(relative,anchor)<N("ampleSwingRadius")) {
                heat+=Period;if(heat>N("flurryGraceTime"))drain=Mathf.Min(baseline*N("maxDrainMultiplier"),drain+N("drainRampPerSecond")*Period);return;
            }
        }
        heat=Mathf.Max(0,heat-N("heatCooldownRate")*Period);if(heat==0&&drain>baseline)drain=baseline;
    }
}
