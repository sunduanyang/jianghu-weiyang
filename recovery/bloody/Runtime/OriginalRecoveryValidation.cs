using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;

// Opt-in engineering probe. A successful result establishes that recovered
// assets can be instantiated/bound and traverse the recovered contact path.
// It does not establish equivalence to an original APK replay.
public sealed partial class OriginalRecoveryValidation : MonoBehaviour {
    OriginalPreview preview;
    J catalog;
    string output;
    bool assemblyOnly,lifecycleOnly,finished;
    readonly List<J> assemblies=new List<J>(),contacts=new List<J>(),issues=new List<J>();
    readonly List<OriginalRig> fixtures=new List<OriginalRig>();
    int expectedCases,checkedResources;
    SimulationMode2D previousSimulation;

    // Preview.Start calls this before SpawnOriginalRound, then yield-breaks
    // when true. Nothing is attached and no settings change without the flag.
    public static bool TryStart(OriginalPreview host) {
        var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-validateOriginalEquipment");
        int lifecycleAt=Array.IndexOf(args,"-validateOriginalLifecycle");if(lifecycleAt>=0)at=lifecycleAt;
        if(at<0)return false;
        var probe=host.gameObject.AddComponent<OriginalRecoveryValidation>();probe.preview=host;
        probe.output=at+1<args.Length&&!args[at+1].StartsWith("-",StringComparison.Ordinal)?args[at+1]:Path.Combine(Application.persistentDataPath,"OriginalEquipmentValidation");
        probe.assemblyOnly=Array.IndexOf(args,"-originalEquipmentAssemblyOnly")>=0;
        probe.lifecycleOnly=lifecycleAt>=0;
        host.enabled=false;if(host.hud!=null)host.hud.gameObject.SetActive(false);
        probe.StartCoroutine(probe.Run());return true;
    }
    IEnumerator Run() {
        Directory.CreateDirectory(output);
        previousSimulation=Physics2D.simulationMode;Physics2D.simulationMode=SimulationMode2D.Script;
        Time.timeScale=1;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        Application.logMessageReceived+=ObserveLog;
        Exception setupError=null;
        try {
            var asset=Resources.Load<TextAsset>("OriginalItemCatalog");
            if(asset==null)throw new InvalidOperationException("OriginalItemCatalog resource is missing");
            catalog=J.Parse(asset.text);
            if(catalog["items"].A.Count!=342)Issue("catalog","expected_342_roots",catalog["items"].A.Count.ToString());
            expectedCases=lifecycleOnly?0:catalog["items"].A.Sum(r=>r["two_handed"].B?2:4);
        }catch(Exception ex){setupError=ex;}
        if(setupError!=null){Issue("setup","exception",setupError.ToString());Complete();yield break;}
        if(lifecycleOnly){yield return RunLifecycle();Complete();yield break;}
        foreach(bool human in new[]{true,false}) {
            OriginalRig rig=null;
            try{rig=CreateFixture(human,new Vector3(0,20,0));}catch(Exception ex){Issue(human?"player":"enemy","fixture_exception",ex.ToString());}
            if(rig==null)continue;
            var pose=new FixturePose(rig);var settings=EquipmentSettings(rig);
            foreach(var row in catalog["items"].A) {
                int[] hands=row["two_handed"].B?new[]{2}:new[]{0,1};
                foreach(int hand in hands) {
                    List<string> errors=new List<string>();
                    J result;
                    try {
                        ResetFixture(rig,settings,pose);
                        string resource=row["resource"].S;
                        rig.EquipOriginal(hand==1?null:resource,hand==1?resource:null);
                        var item=hand==1?rig.rightItem:rig.leftItem;
                        result=InspectAssembly(rig,row,item,hand,settings,errors);
                        Physics2D.SyncTransforms();Physics2D.Simulate(Time.fixedDeltaTime);Physics2D.Simulate(Time.fixedDeltaTime);
                        InspectFinite(rig.transform,"fighter_after_solver",errors);
                        if(item!=null)InspectFinite(item.transform,"item_after_solver",errors);
                    } catch(Exception ex) {
                        errors.Add("exception: "+ex);
                        result=J.O("name",row["name"].S,"resource",row["resource"].S,"item_id",row["item_id"].I,"category",row["category"].I,"two_handed",row["two_handed"].B,"human",human,"hand",hand);
                    }
                    result["passed"]=new J(errors.Count==0);result["errors"]=Strings(errors);assemblies.Add(result);
                    foreach(string error in errors)Issue(row["resource"].S,"assembly",(human?"player":"enemy")+" hand "+hand+": "+error);
                    ClearEquipment(rig,settings);
                    if(assemblies.Count%25==0)Write(false);
                    // Let Unity complete Destroy before reusing the fixture.
                    yield return null;
                }
                if(human)checkedResources++;
            }
            rig.gameObject.SetActive(false);
        }
        if(!assemblyOnly) {
            // A source collider is launched at an isolated original torso
            // collider. Damage is only accepted from actual collision events;
            // this probe never calls ProcessImpact/ApplyDamage itself.
            var representatives=new List<J>();
            for(int category=0;category<9;category++) {
                var row=catalog["items"].A.FirstOrDefault(r=>r["category"].I==category);
                if(row!=null)representatives.Add(row);else Issue("category:"+category,"missing_representative","");
            }
            foreach(var row in representatives)yield return ContactProbe(row,false);
            var cannon=catalog["items"].A.FirstOrDefault(r=>r["cannon"].Has);
            if(cannon!=null)yield return ContactProbe(cannon,true);else Issue("cannon","missing_representative","");
        }
        Complete();
    }
    OriginalRig CreateFixture(bool human,Vector3 position) {
        var prefab=human?preview.playerPrefab:preview.enemyPrefab;
        if(prefab==null)throw new InvalidOperationException("Missing "+(human?"player":"enemy")+" prefab");
        var go=Instantiate(prefab,position,prefab.transform.rotation);go.name="Equipment probe "+(human?"player":"enemy");go.SetActive(true);
        var rig=go.AddComponent<OriginalRig>();fixtures.Add(rig);rig.Initialize(human);rig.StopOriginalAI();rig.enabled=false;
        rig.movement=0;rig.SetOriginalHandInputs(Vector2.zero,Vector2.zero,false,false);
        foreach(var renderer in go.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
        return rig;
    }
    static J EquipmentSettings(OriginalRig rig) {
        return rig.source["controllers"]["CharacterConfigApplier"][0]["data"];
    }
    static void ClearEquipment(OriginalRig rig,J settings) {
        foreach(string side in new[]{"L","R"}) {
            var joint=rig.Resolve<FixedJoint2D>(settings["handFixedJoint2D"+side]);
            if(joint!=null){joint.enabled=false;joint.connectedBody=null;}
        }
        var roots=rig.GetComponentsInChildren<OriginalItem>(true).Where(i=>!i.flailHead).ToArray();
        foreach(var item in roots){item.gameObject.SetActive(false);item.transform.SetParent(null,true);Destroy(item.gameObject);}
        rig.leftItem=rig.rightItem=null;rig.doubleHanded=false;
    }
    static void ResetFixture(OriginalRig rig,J settings,FixturePose pose) {
        ClearEquipment(rig,settings);pose.Restore();
        rig.health=rig.healthMax;rig.stamina=100;
    }
    J InspectAssembly(OriginalRig rig,J row,OriginalItem item,int hand,J settings,List<string> errors) {
        if(Resources.Load<GameObject>(row["resource"].S)==null)errors.Add("resource cannot load as GameObject");
        if(item==null){errors.Add("EquipOriginal returned no item");return Identity(row,rig,hand);}
        if(item.itemId!=row["item_id"].I||item.category!=row["category"].I||item.twoHanded!=row["two_handed"].B)errors.Add("runtime item identity differs from catalog");
        if(item.owner!=rig)errors.Add("item owner mismatch");
        if(hand==2&&(!rig.doubleHanded||rig.leftItem!=item||rig.rightItem!=item))errors.Add("two-hand item does not share both grips");
        var opposite=hand==0?rig.rightItem:rig.leftItem;
        // The source catalog contains a physical itemId=0 None placeholder.
        if(hand!=2&&(rig.doubleHanded||(opposite!=null&&opposite.itemId!=0)))errors.Add("single-hand equipment populated opposite slot with a non-empty item");
        ValidateItemReferences(item,row,errors,"root");
        foreach(int side in hand==2?new[]{0,1}:new[]{hand}) {
            var joint=rig.Resolve<FixedJoint2D>(settings["handFixedJoint2D"+(side==0?"L":"R")]);
            if(joint==null)errors.Add("missing hand "+side+" FixedJoint2D");
            else {
                if(!joint.enabled||joint.connectedBody!=item.itemBody)errors.Add("hand "+side+" grip is not connected to item body");
                if(!Finite(joint.anchor)||!Finite(joint.connectedAnchor))errors.Add("non-finite hand "+side+" anchors");
                var handle=hand==2&&side==1?item.handle2:item.handle1;
                if(handle!=null&&Vector2.Distance(joint.connectedAnchor,handle.localPosition)>.00001f)errors.Add("hand "+side+" connected anchor differs from original handle localPosition");
            }
        }
        int nested=item.GetComponentsInChildren<OriginalItem>(true).Count(i=>i!=item);
        if(nested!=row["flail_heads"].A.Count)errors.Add("flail head instance count differs: "+nested+" / "+row["flail_heads"].A.Count);
        foreach(var head in item.GetComponentsInChildren<OriginalItem>(true).Where(i=>i!=item)) {
            var source=row["flail_heads"].A.FirstOrDefault(h=>h["fields"]["m_GameObject"]["path"].S==head.config["m_GameObject"]["path"].S);
            if(source==null)errors.Add("unexpected nested item "+head.name);else ValidateItemReferences(head,source,errors,"flail "+head.name);
        }
        int bodies=item.GetComponentsInChildren<Rigidbody2D>(true).Length,colliders=item.GetComponentsInChildren<Collider2D>(true).Length;
        if(bodies==0)errors.Add("item contains no rigidbodies");if(colliders==0)errors.Add("item contains no colliders");
        InspectFinite(rig.transform,"fighter_before_solver",errors);InspectFinite(item.transform,"item_before_solver",errors);
        var result=Identity(row,rig,hand);result["rigidbodies"]=new J(bodies);result["colliders"]=new J(colliders);result["nested_items"]=new J(nested);
        result["declared_null_flail_link_slots"]=new J(item.GetComponentsInChildren<OriginalItem>(true).Sum(i=>i.config["FlailLinks"].A.Count(r=>r["id"].S=="0")));
        result["solver_steps"]=new J(2);return result;
    }
    static J Identity(J row,OriginalRig rig,int hand)=>J.O("name",row["name"].S,"resource",row["resource"].S,"item_id",row["item_id"].I,"category",row["category"].I,"two_handed",row["two_handed"].B,"human",rig.human,"hand",hand,"serialized_source",row["source"],"runtime_source",row["runtime_source"],"resource_alias",row["resource_alias"].B);
    static void ValidateItemReferences(OriginalItem item,J row,List<string> errors,string label) {
        if(item.itemBody==null)errors.Add(label+": ItemBaseRigidbody2D unresolved");
        if(!item.flailHead&&item.counterweightBody==null)errors.Add(label+": CounterweightRigidbody2D unresolved");
        if(!item.flailHead&&item.handle1==null)errors.Add(label+": Handle_1 unresolved");
        if(item.twoHanded&&!item.flailHead&&item.handle2==null)errors.Add(label+": two-handed Handle_2 unresolved");
        if(!Finite(item.damage)||!Finite(item.defense)||!Finite(item.weight)||!Finite(item.length))errors.Add(label+": non-finite item parameters");
        foreach(var pair in row["component_bindings"].D) {
            var binding=pair.Value;var t=item.RefTransform(J.O("path",binding["path"].S));
            if(t==null){errors.Add(label+": missing component path "+binding["path"].S);continue;}
            var components=t.GetComponents<Component>().Where(c=>c!=null&&c.GetType().Name==binding["kind"].S).ToArray();
            if(binding["index"].I>=components.Length)errors.Add(label+": missing "+binding["kind"].S+"["+binding["index"].I+"] at "+binding["path"].S);
        }
        foreach(var reference in item.config["renderers"].A)if(item.Ref<Renderer>(reference)==null)errors.Add(label+": source renderer unresolved at "+reference["path"].S);
        // Original Awake tests Object == null and continues before traversing
        // each FlailLinks entry (0x363d6bc onward). Several source head arrays
        // deliberately contain one null PPtr rather than an empty array.
        foreach(var reference in item.config["FlailLinks"].A)if(reference["id"].S!="0"&&item.RefTransform(reference)==null)errors.Add(label+": source flail link unresolved at "+reference["path"].S);
        CheckNormalizedReferences(item,item.config,errors,label);
        if(row["cannon"].Has) {
            if(item.GetComponent<OriginalHandCannon>()==null)errors.Add(label+": cannon behavior missing");
            CheckNormalizedReferences(item,row["cannon"],errors,label+" cannon");
            CheckComponent<LineRenderer>(item,row["cannon"],"tracer",errors,label);
            CheckComponent<Rigidbody2D>(item,row["cannon"],"throwerRigidbody2D",errors,label);
            CheckComponent<ParticleSystem>(item,row["cannon"],"muzzleBlastVFX",errors,label);
            CheckComponent<ParticleSystem>(item,row["cannon"],"impactVFX",errors,label);
            CheckComponent<AudioSource>(item,row["cannon"],"fireSFX",errors,label);
            CheckComponent<Animation>(item,row["cannon"],"fireAnimation",errors,label);
        }
    }
    static void CheckComponent<T>(OriginalItem item,J source,string key,List<string> errors,string label)where T:Component {
        var reference=source[key];if(reference["id"].Has&&reference["id"].S!="0"&&item.Ref<T>(reference)==null)errors.Add(label+": "+key+" does not resolve to "+typeof(T).Name);
    }
    static void CheckNormalizedReferences(OriginalItem item,J value,List<string> errors,string label) {
        if(value["id"].Has&&value["id"].S!="0"&&value["path"].Has) {
            var t=item.RefTransform(value);
            if(t==null)errors.Add(label+": missing normalized reference "+value["id"].S+" at "+value["path"].S);
            else if(!Finite(t.position)||!Finite(t.localScale))errors.Add(label+": non-finite normalized reference "+value["id"].S);
        }
        foreach(var pair in value.D)CheckNormalizedReferences(item,pair.Value,errors,label);
        foreach(var element in value.A)CheckNormalizedReferences(item,element,errors,label);
    }
    static void InspectFinite(Transform root,string label,List<string> errors) {
        foreach(var t in root.GetComponentsInChildren<Transform>(true))if(!Finite(t.position)||!Finite(t.localScale)||!Finite(t.rotation.x)||!Finite(t.rotation.y)||!Finite(t.rotation.z)||!Finite(t.rotation.w))errors.Add(label+": non-finite transform "+t.name);
        foreach(var b in root.GetComponentsInChildren<Rigidbody2D>(true)) {
            if(!Finite(b.position)||!Finite(b.rotation)||!Finite(b.linearVelocity)||!Finite(b.angularVelocity)||!Finite(b.mass)||b.mass<=0||!Finite(b.gravityScale)||!Finite(b.linearDamping)||!Finite(b.angularDamping))errors.Add(label+": invalid rigidbody "+b.name);
        }
        foreach(var j in root.GetComponentsInChildren<AnchoredJoint2D>(true))if(!Finite(j.anchor)||!Finite(j.connectedAnchor))errors.Add(label+": non-finite joint "+j.name);
    }
    IEnumerator ContactProbe(J row,bool cannonShot) {
        var errors=new List<string>();J result=J.O("name",row["name"].S,"resource",row["resource"].S,"category",row["category"].I,"kind",cannonShot?"original_cannon_circlecast":"original_collider_contact");
        OriginalRig owner=null,victim=null;OriginalItem item=null;OriginalBodyPart part=null;Collider2D targetCollider=null,striker=null;OriginalValidationCollisionObserver observer=null;
        OriginalImpact observed=default;int impactEvents=0;float before=0,partBefore=0,contactThreshold=0,launchSpeed=0;bool setupSucceeded=false;
        try {
            owner=CreateFixture(true,new Vector3(-10,40,0));victim=CreateFixture(false,new Vector3(0,40,0));
            owner.opponent=victim;victim.opponent=owner;
            owner.EquipOriginal(row["resource"].S,null);item=owner.leftItem;
            int cannonMask=row["cannon"][owner.OriginalFlipped?"targetMask_ShooterIsCharacter2":"targetMask_ShooterIsCharacter1"]["m_Bits"].I;
            foreach(var candidate in victim.BodyParts.Where(p=>p.type==1&&p.overallMultiplier>0)) {
                // Torso source parts have empty aliveColliders arrays because
                // their normal collider is never swapped on death.
                foreach(var collider in victim.GetComponentsInChildren<Collider2D>(true)) {
                    if(collider!=null&&collider.enabled&&!collider.isTrigger&&collider.attachedRigidbody==candidate.body) {
                        // The native cannon mask excludes the pelvis's layer
                        // 12. Select an original mask-eligible chest/belly
                        // collider, rather than changing its layer or mask.
                        int sourceLayer=collider.gameObject==candidate.gameObject?candidate.OriginalCollisionLayer:collider.gameObject.layer;
                        if(cannonShot&&(cannonMask&(1<<sourceLayer))==0)continue;
                        if(targetCollider==null||Area(collider.bounds)>Area(targetCollider.bounds)){targetCollider=collider;part=candidate;}
                    }
                }
            }
            if(item==null||part==null||targetCollider==null)throw new InvalidOperationException("Missing equipped source item or original torso collision target");
            foreach(var b in victim.GetComponentsInChildren<Rigidbody2D>(true)){b.bodyType=RigidbodyType2D.Kinematic;b.linearVelocity=Vector2.zero;b.angularVelocity=0;}
            foreach(var c in victim.GetComponentsInChildren<Collider2D>(true))c.enabled=c==targetCollider;
            foreach(var c in owner.GetComponentsInChildren<Collider2D>(true))if(!c.transform.IsChildOf(item.transform))c.enabled=false;
            foreach(var j in owner.GetComponentsInChildren<Joint2D>(true))j.enabled=false;
            item.transform.SetParent(null,true);
            foreach(var j in item.GetComponentsInChildren<Joint2D>(true))j.enabled=false;
            victim.ImpactOccurred+=impact=>{if(impact.item!=null&&impact.item.owner==owner){observed=impact;impactEvents++;}};
            before=victim.health;partBefore=part.health;
            if(!cannonShot) {
                striker=item.GetComponentsInChildren<Collider2D>(true)
                    .Where(c=>c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger&&c.CompareTag("HandItem")&&c.attachedRigidbody!=null)
                    .OrderByDescending(c=>OriginalItem.From(c)!=null&&OriginalItem.From(c).flailHead?100:0)
                    .ThenByDescending(c=>SurfacePriority(OriginalCombatRules.Surface(c.sharedMaterial)))
                    .FirstOrDefault();
                if(striker==null)throw new InvalidOperationException("No enabled original HandItem strike collider");
                foreach(var c in item.GetComponentsInChildren<Collider2D>(true))c.enabled=c==striker;
                foreach(var b in item.GetComponentsInChildren<Rigidbody2D>(true)){b.simulated=b==striker.attachedRigidbody;b.gravityScale=0;b.linearDamping=0;b.angularDamping=0;b.linearVelocity=Vector2.zero;b.angularVelocity=0;}
                var body=striker.attachedRigidbody;body.bodyType=RigidbodyType2D.Dynamic;body.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
                Physics2D.SyncTransforms();
                Vector2 desired=(Vector2)targetCollider.bounds.center+Vector2.left*(targetCollider.bounds.extents.x+striker.bounds.extents.x+.08f);
                body.position+=desired-(Vector2)striker.bounds.center;
                observer=body.gameObject.AddComponent<OriginalValidationCollisionObserver>();observer.target=targetCollider;
                result["striker"]=new J(striker.name);result["surface"]=new J(OriginalCombatRules.Surface(striker.sharedMaterial).ToString());
                var strikeItem=OriginalItem.From(striker);
                contactThreshold=victim.DamageNumber(strikeItem.blunt?"DamageSpeedThresholdBlunt":"DamageSpeedThresholdSharp");
                if(OriginalCombatRules.Handle(OriginalCombatRules.Surface(striker.sharedMaterial)))contactThreshold*=.5f;
                // A glancing source shape can have contact-point speed below
                // its center-of-mass speed. The fixture must exceed the native
                // gate; do not weaken the gate or inject a successful impact.
                launchSpeed=Mathf.Max(40,contactThreshold*3);
                result["native_contact_speed_threshold"]=Number(contactThreshold);
                result["serialized_source"]=row["source"];result["runtime_source"]=row["runtime_source"];
                result["runtime_item_base_reference_id"]=item.config["ItemBaseRigidbody2D"]["id"];
            } else {
                foreach(var b in item.GetComponentsInChildren<Rigidbody2D>(true))b.simulated=false;
                foreach(var c in item.GetComponentsInChildren<Collider2D>(true))c.enabled=false;
                var cannon=item.GetComponent<OriginalHandCannon>();if(cannon==null)throw new InvalidOperationException("OriginalHandCannon missing");
                var muzzle=item.RefTransform(row["cannon"]["muzzle"]);var aim=item.RefTransform(row["cannon"]["projectileTargetPoint"]);
                if(muzzle==null||aim==null)throw new InvalidOperationException("Cannon muzzle/target reference missing");
                Vector2 direction=aim.position-muzzle.position;
                item.transform.Rotate(0,0,-Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg,Space.World);
                item.transform.position+=(Vector3)((Vector2)targetCollider.bounds.center+Vector2.left*3-(Vector2)muzzle.position);
            }
            setupSucceeded=true;
        }catch(Exception ex){errors.Add("fixture: "+ex);}
        if(setupSucceeded) {
            // BodyPart's original .5 second startup layer gate must expire.
            yield return new WaitForSeconds(.55f);
            try {
                Physics2D.SyncTransforms();
                if(cannonShot) {
                    var cannon=item.GetComponent<OriginalHandCannon>();cannon.Fire();var shot=cannon.LastShot;
                    result["cast"]=J.O("origin_x",Number(shot.origin.x),"origin_y",Number(shot.origin.y),"direction_x",Number(shot.direction.x),"direction_y",Number(shot.direction.y),"layer_mask",shot.layerMask,"blocker_layers",shot.blockerLayers,"radius",Number(shot.radius),"range",Number(shot.range),"hit_count",shot.hitCount,"selected_part",shot.bodyPart==null?0:shot.bodyPart.sourceId);
                    if((shot.layerMask&(1<<targetCollider.gameObject.layer))==0)errors.Add("Fixture selected a source target outside native cannon mask");
                }
                else {
                    striker.attachedRigidbody.linearVelocity=Vector2.right*launchSpeed;
                    result["launch_speed"]=Number(launchSpeed);
                    for(int step=0;step<120&&impactEvents==0;step++){Physics2D.SyncTransforms();Physics2D.Simulate(1f/120);}
                }
                if(impactEvents==0)errors.Add("No OriginalImpact event from physical "+(cannonShot?"circlecast":"collision"));
                if(!cannonShot&&(observer==null||observer.targetContacts==0))errors.Add("No actual OnCollisionEnter2D against selected original target");
                if(!cannonShot&&observer!=null&&observer.targetContacts>0&&observer.relativeSpeed<contactThreshold)errors.Add("Actual contact-point speed did not reach the original damage threshold");
                if(!(victim.health<before)||!(part.health<partBefore))errors.Add("Original physical hit did not reduce both global and local health");
                if(impactEvents>0&&(!Finite(observed.localDamage)||!Finite(observed.globalDamage)||observed.localDamage<=0||observed.globalDamage<=0))errors.Add("Impact reported invalid damage");
                InspectFinite(victim.transform,"victim_after_contact",errors);
                result["impact_events"]=new J(impactEvents);result["health_before"]=Number(before);result["health_after"]=Number(victim.health);
                result["part_health_before"]=Number(partBefore);result["part_health_after"]=Number(part.health);
                result["damage_local"]=Number(observed.localDamage);result["damage_global"]=Number(observed.globalDamage);
                result["target_contacts"]=new J(observer==null?0:observer.targetContacts);result["target_part_id"]=new J(part.sourceId);
                result["target_layer"]=new J(targetCollider.gameObject.layer);result["target_collider"]=new J(targetCollider.name);
                result["contact_relative_speed"]=Number(observer==null?0:observer.relativeSpeed);
                result["fixture"]=new J("Isolated original torso collider, kinematic target; real item collider launched at max(40, native speed threshold * 3) m/s, or original cannon CircleCast toward an original mask-eligible torso collider. Movement/AI and unrelated collisions disabled. No direct damage injection.");
            }catch(Exception ex){errors.Add("contact: "+ex);}
        }
        result["passed"]=new J(errors.Count==0);result["errors"]=Strings(errors);contacts.Add(result);
        foreach(var error in errors)Issue(row["resource"].S,"contact",error);
        if(item!=null)Destroy(item.gameObject);
        if(owner!=null)owner.gameObject.SetActive(false);if(victim!=null)victim.gameObject.SetActive(false);
        Write(false);yield return null;
    }
    static float Area(Bounds b)=>b.size.x*b.size.y;
    static int SurfacePriority(OriginalSurface surface)=>surface==OriginalSurface.MetalBlade||surface==OriginalSurface.MetalSpike||surface==OriginalSurface.MetalBlunt||surface==OriginalSurface.WoodBlunt?2:OriginalCombatRules.Handle(surface)?0:1;
    static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    static bool Finite(Vector2 v)=>Finite(v.x)&&Finite(v.y);
    static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
    static J Number(float value)=>Finite(value)?new J(value):new J(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    static J Strings(IEnumerable<string> values)=>new J(values.Select(v=>new J(v)).ToList());
    void Issue(string resource,string kind,string detail){issues.Add(J.O("resource",resource,"kind",kind,"detail",detail));}
    void ObserveLog(string message,string stack,LogType type) {
        if(type==LogType.Exception||type==LogType.Assert||type==LogType.Error)Issue("engine_log",type.ToString(),message+"\n"+stack);
    }
    void Write(bool complete) {
        var report=J.O("scope",lifecycleOnly?"Recovered physical combat lifecycle validation":"Recovered asset assembly and isolated physical damage validation","unity",Application.unityVersion,"source_apk_sha256",catalog==null?"":catalog["source_apk_sha256"].S,
            "complete",complete,"assembly_only",assemblyOnly,"lifecycle_only",lifecycleOnly,"catalog_roots",catalog==null?0:catalog["items"].A.Count,"distinct_resource_paths",catalog==null?0:catalog["items"].A.Select(r=>r["resource"].S).Distinct().Count(),"source_rows_checked",checkedResources,
            "resource_aliases",catalog==null?new J():catalog["resource_aliases"],
            "assembly_cases_expected",expectedCases,"assembly_cases_run",assemblies.Count,"assembly_cases_passed",assemblies.Count(a=>a["passed"].B),
            "contact_cases_run",contacts.Count,"contact_cases_passed",contacts.Count(a=>a["passed"].B),"issue_count",issues.Count,
            "assemblies",new J(assemblies),"contacts",new J(contacts),"lifecycles",new J(lifecycleCases),"issues",new J(issues),
            "passed",complete&&ValidationPassed,
            "runtime_parity_verified",false,"fixture_pose_and_grip_anchors_restored_each_case",!lifecycleOnly,"cross_equipment_transition_verified",false,
            "reference_lifecycle",J.O("cannon_impact_vfx","Original Awake caches Transform and detaches at 0x350e778; validate cached component after detach.","null_flail_link","Source head FlailLinks contains id=0; original Awake null guard at 0x363d6bc onward skips it. Non-null entries are still required."),
            "limits",Strings(new[]{"342 serialized root rows map to 340 distinct resource paths: two equipped scene instances alias their standalone resource prefabs; both source rows are retained.","Before every assembly case the probe actively restores fixture pose, rigidbody settings and original grip anchors; this does not test sequential equipment switching.","Assembly uses two solver steps per case and does not assert a gameplay trajectory.","Contact fixtures isolate original colliders and disable unrelated motion/AI; they validate callback/damage integration only.","No original APK input/pose/trajectory comparison is performed."}));
        File.WriteAllText(Path.Combine(output,"original-equipment-validation.json"),report.ToString());
        File.WriteAllText(Path.Combine(output,"original-equipment-missing.json"),J.O("complete",complete,"issues",new J(issues)).ToString());
        if(lifecycleOnly){File.WriteAllText(Path.Combine(output,"original-lifecycle-validation.json"),report.ToString());File.WriteAllText(Path.Combine(output,"original-lifecycle-missing.json"),J.O("complete",complete,"issues",new J(issues)).ToString());}
    }
    bool ValidationPassed=>issues.Count==0&&(lifecycleOnly?lifecycleCases.Count==3&&lifecycleCases.All(r=>r["passed"].B):assemblies.Count==expectedCases&&(assemblyOnly||contacts.Count==10));
    void Complete() {
        finished=true;Write(true);Application.logMessageReceived-=ObserveLog;Physics2D.simulationMode=previousSimulation;
        Debug.Log("Original recovery validation: "+assemblies.Count+" assembly cases, "+contacts.Count+" physical cases, "+lifecycleCases.Count+" lifecycle cases, "+issues.Count+" issues. "+output);
        Application.Quit(ValidationPassed?0:1);
    }
    void OnDestroy(){Application.logMessageReceived-=ObserveLog;if(!finished)Physics2D.simulationMode=previousSimulation;}
    sealed class FixturePose {
        readonly Rigidbody2D[] bodies;
        readonly Vector2[] positions;
        readonly float[] rotations,masses,drag,angularDrag,gravity;
        readonly Transform[] transforms;
        readonly Vector3[] localPositions,localScales;
        readonly Quaternion[] localRotations;
        readonly AnchoredJoint2D[] joints;
        readonly Vector2[] anchors,connectedAnchors;
        readonly Rigidbody2D[] connections;
        readonly bool[] jointEnabled,autoAnchors;
        public FixturePose(OriginalRig rig) {
            bodies=rig.GetComponentsInChildren<Rigidbody2D>(true);positions=bodies.Select(b=>b.position).ToArray();rotations=bodies.Select(b=>b.rotation).ToArray();
            masses=bodies.Select(b=>b.mass).ToArray();drag=bodies.Select(b=>b.linearDamping).ToArray();angularDrag=bodies.Select(b=>b.angularDamping).ToArray();gravity=bodies.Select(b=>b.gravityScale).ToArray();
            transforms=rig.GetComponentsInChildren<Transform>(true);localPositions=transforms.Select(t=>t.localPosition).ToArray();localScales=transforms.Select(t=>t.localScale).ToArray();localRotations=transforms.Select(t=>t.localRotation).ToArray();
            joints=rig.GetComponentsInChildren<AnchoredJoint2D>(true);anchors=joints.Select(j=>j.anchor).ToArray();connectedAnchors=joints.Select(j=>j.connectedAnchor).ToArray();connections=joints.Select(j=>j.connectedBody).ToArray();jointEnabled=joints.Select(j=>j.enabled).ToArray();autoAnchors=joints.Select(j=>j.autoConfigureConnectedAnchor).ToArray();
        }
        public void Restore() {
            for(int i=0;i<transforms.Length;i++)if(transforms[i]!=null){transforms[i].localPosition=localPositions[i];transforms[i].localRotation=localRotations[i];transforms[i].localScale=localScales[i];}
            for(int i=0;i<bodies.Length;i++)if(bodies[i]!=null){var b=bodies[i];b.position=positions[i];b.rotation=rotations[i];b.mass=masses[i];b.linearDamping=drag[i];b.angularDamping=angularDrag[i];b.gravityScale=gravity[i];b.linearVelocity=Vector2.zero;b.angularVelocity=0;b.WakeUp();}
            for(int i=0;i<joints.Length;i++)if(joints[i]!=null){var j=joints[i];j.enabled=false;j.connectedBody=connections[i];j.autoConfigureConnectedAnchor=false;j.anchor=anchors[i];j.connectedAnchor=connectedAnchors[i];j.autoConfigureConnectedAnchor=autoAnchors[i];j.enabled=jointEnabled[i];}
        }
    }
}

// Independent witness of Unity's actual contact dispatch. This component
// records events only and has no reference to any damage application API.
public sealed class OriginalValidationCollisionObserver : MonoBehaviour {
    public Collider2D target;
    public int targetContacts;
    public float relativeSpeed;
    void OnCollisionEnter2D(Collision2D collision) {
        if(collision.collider!=target&&collision.otherCollider!=target)return;
        targetContacts++;if(collision.contactCount>0)relativeSpeed=collision.GetContact(0).relativeVelocity.magnitude;
    }
}
