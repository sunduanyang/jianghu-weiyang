using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

// Numeric source-prefab importer for the FORMAL PhysicsDuel. This has no
// MonoBehaviour, render loop, mover, AI, or independent combat entry point.
public sealed class DuelSourceRig {
    static J catalog;
    static J Catalog {
        get {
            if(catalog==null){var asset=Resources.Load<TextAsset>("CombatSource/rig");if(asset==null)throw new InvalidOperationException("CombatSource/rig.json is missing");catalog=J.Parse(asset.text);}
            return catalog;
        }
    }
    public string SourceId {get;private set;}
    public J Data {get;private set;}
    public J Controllers=>Data["controllers"];
    float initialSpawnX;
    public GameObject Root {get;private set;}
    public Rigidbody2D RootBody {get;private set;}
    public readonly Dictionary<int,GameObject> Objects=new Dictionary<int,GameObject>();
    public readonly Dictionary<int,Transform> Transforms=new Dictionary<int,Transform>();
    public readonly Dictionary<int,Component> Components=new Dictionary<int,Component>();
    public readonly Dictionary<int,Rigidbody2D> Bodies=new Dictionary<int,Rigidbody2D>();
    public readonly Dictionary<int,Collider2D> Colliders=new Dictionary<int,Collider2D>();
    public readonly Dictionary<string,PhysicsMaterial2D> Materials=new Dictionary<string,PhysicsMaterial2D>();
    // Source hand placeholders connect a joint to its own Rigidbody until
    // ConfigApplier equips an item. Unity's runtime setter rejects self-links.
    public readonly HashSet<int> PendingSelfJoints=new HashSet<int>();
    public J Source(string controllerName)=>Controllers[controllerName][0]["data"];
    public Transform Transform(int sourceId)=>sourceId==0?null:Transforms.TryGetValue(sourceId,out var value)?value:null;
    public Component Component(int sourceId)=>sourceId==0?null:Components.TryGetValue(sourceId,out var value)?value:null;
    public T Resolve<T>(J reference) where T:Component=>Component(Id(reference)) as T;
    public Transform ResolveTransform(J reference){int id=Id(reference);if(Transforms.TryGetValue(id,out var t))return t;return Objects.TryGetValue(id,out var go)?go.transform:Component(id)?.transform;}
    public static int Id(J reference)=>reference["m_PathID"].Has?reference["m_PathID"].I:reference["id"].I;
    static float F(J value)=>value.S=="+Infinity"?float.PositiveInfinity:value.S=="-Infinity"?float.NegativeInfinity:(float)value.N;
    static Vector2 V2(J value)=>new Vector2(F(value["x"]),F(value["y"]));
    static Vector3 V3(J value)=>new Vector3(F(value["x"]),F(value["y"]),F(value["z"]));
    static Quaternion Q(J value)=>new Quaternion(F(value["x"]),F(value["y"]),F(value["z"]),F(value["w"]));
    static int Mask(J value)=>unchecked((int)(uint)value["m_Bits"].N);
    static uint Matrix(int layer)=>unchecked((uint)Catalog["collision_matrix"][layer].N);
    public static void ApplyLayerCollision(Collider2D collider){ApplyLayerCollision(collider,0,0);}
    static void ApplyLayerCollision(Collider2D collider,int originalInclude,int originalExclude){
        uint allowed=Matrix(collider.gameObject.layer);
        // Explicit per-shape includes allow the SOURCE matrix's pairs even if
        // the host project's default matrix disables them. Excludes preserve
        // all disallowed source pairs. No global layer matrix is changed.
        collider.includeLayers=unchecked((int)(allowed|(uint)originalInclude));
        collider.excludeLayers=unchecked((int)(~allowed|(uint)originalExclude));
    }
    public static void ApplyItemLayer(Collider2D collider,bool human,bool dropped=false){collider.gameObject.layer=dropped?17:human?15:16;ApplyLayerCollision(collider);}
    public PhysicsMaterial2D Material(J reference){
        string key=reference["material_key"].S;
        if(key.Length==0){if(Id(reference)==0)return null;throw new InvalidOperationException("Unresolved source physics material "+reference);}
        if(Materials.TryGetValue(key,out var existing))return existing;
        J raw=Catalog["materials"][key];if(!raw.Has)throw new InvalidOperationException("Missing source physics material "+key);
        var material=new PhysicsMaterial2D(raw["m_Name"].S){friction=F(raw["friction"]),bounciness=F(raw["bounciness"]),frictionCombine=(PhysicsMaterialCombine2D)raw["m_FrictionCombine"].I,bounceCombine=(PhysicsMaterialCombine2D)raw["m_BounceCombine"].I};
        Materials[key]=material;return material;
    }
    public void DestroyMaterials(){foreach(var material in Materials.Values)UnityEngine.Object.Destroy(material);Materials.Clear();}
    public static DuelSourceRig Create(Transform container,bool human,float spawnX){
        var result=new DuelSourceRig{SourceId=Catalog[human?"player_source":"enemy_source"].S};
        result.Data=Catalog["rigs"][result.SourceId];result.Build(container,spawnX);return result;
    }
    void Build(Transform container,float spawnX){
        initialSpawnX=spawnX;
        // Entire hierarchy is inactive until every exact component ID and
        // connected reference exists. No simulation can observe half a rig.
        foreach(var pair in Data["objects"].D){int id=int.Parse(pair.Key);var go=new GameObject(pair.Value["m_Name"].S);go.SetActive(false);go.layer=pair.Value["m_Layer"].I;Objects[id]=go;}
        foreach(var pair in Data["transforms"].D){int id=int.Parse(pair.Key);var t=Objects[Id(pair.Value["m_GameObject"])].transform;Transforms[id]=t;Components[id]=t;}
        int rootId=Data["root_transform_id"].I;
        Root=Transforms[rootId].gameObject;
        foreach(var pair in Data["transforms"].D){
            int id=int.Parse(pair.Key);J raw=pair.Value;var t=Transforms[id];int parent=Id(raw["m_Father"]);
            t.SetParent(parent==0?container:Transforms[parent],false);
            Vector3 position=V3(raw["m_LocalPosition"]);if(id==rootId)position.x=spawnX;
            t.localPosition=position;t.localRotation=Q(raw["m_LocalRotation"]);t.localScale=V3(raw["m_LocalScale"]);
        }
        foreach(var pair in Data["transforms"].D){int index=0;foreach(var child in pair.Value["m_Children"].A)Transforms[Id(child)].SetSiblingIndex(index++);}
        // Preserve component ordering WITHIN each phase and bind references by
        // raw ID. In particular two HingeJoint2D on Head remain distinct.
        for(int phase=0;phase<3;phase++)foreach(var raw in Data["physics"].A){
            string type=raw["type"].S;int desired=type=="Rigidbody2D"?0:type.EndsWith("Collider2D",StringComparison.Ordinal)?1:2;if(phase!=desired)continue;
            int id=raw["id"].I;var go=Objects[Id(raw["data"]["m_GameObject"])];
            var component=go.AddComponent(PhysicsType(type));Components[id]=component;
            if(component is Rigidbody2D body){body.simulated=false;Bodies[id]=body;}
            if(component is Collider2D collider){collider.enabled=false;Colliders[id]=collider;}
            if(component is Behaviour behaviour)behaviour.enabled=false;
        }
        foreach(var raw in Data["physics"].A)Configure(Components[raw["id"].I],raw["data"]);
        RootBody=Resolve<Rigidbody2D>(Source("CharacterMover")["baseRigidbody2D"]);
        if(RootBody==null)throw new InvalidOperationException("Source CharacterMover root rigidbody is unresolved");
        foreach(var raw in Data["physics"].A){var c=Components[raw["id"].I];if(c is Rigidbody2D b)b.simulated=raw["data"]["m_Simulated"].B;else if(c is Behaviour behaviour)behaviour.enabled=raw["data"]["m_Enabled"].B&&!PendingSelfJoints.Contains(raw["id"].I);}
        int rootGo=Id(Data["transforms"][rootId.ToString()]["m_GameObject"]);
        foreach(var pair in Data["objects"].D){int id=int.Parse(pair.Key);if(id!=rootGo)Objects[id].SetActive(pair.Value["m_IsActive"].B);}
        Root.SetActive(Data["objects"][rootGo.ToString()]["m_IsActive"].B);
    }
    static Type PhysicsType(string name){
        switch(name){
            case "Rigidbody2D":return typeof(Rigidbody2D);case "BoxCollider2D":return typeof(BoxCollider2D);case "CircleCollider2D":return typeof(CircleCollider2D);case "CapsuleCollider2D":return typeof(CapsuleCollider2D);case "PolygonCollider2D":return typeof(PolygonCollider2D);case "FixedJoint2D":return typeof(FixedJoint2D);case "HingeJoint2D":return typeof(HingeJoint2D);case "AreaEffector2D":return typeof(AreaEffector2D);
            default:throw new InvalidOperationException("Unsupported source physics type "+name);
        }
    }
    void Configure(Component c,J d){
        if(c is Rigidbody2D body){
            body.bodyType=(RigidbodyType2D)d["m_BodyType"].I;body.useFullKinematicContacts=d["m_UseFullKinematicContacts"].B;body.useAutoMass=d["m_UseAutoMass"].B;
            body.mass=F(d["m_Mass"]);body.linearDamping=F(d["m_LinearDamping"]);body.angularDamping=F(d["m_AngularDamping"]);body.gravityScale=F(d["m_GravityScale"]);body.sharedMaterial=Material(d["m_Material"]);
            body.includeLayers=Mask(d["m_IncludeLayers"]);body.excludeLayers=Mask(d["m_ExcludeLayers"]);body.interpolation=(RigidbodyInterpolation2D)d["m_Interpolate"].I;body.sleepMode=(RigidbodySleepMode2D)d["m_SleepingMode"].I;body.collisionDetectionMode=(CollisionDetectionMode2D)d["m_CollisionDetection"].I;body.constraints=(RigidbodyConstraints2D)d["m_Constraints"].I;
        }
        if(c is Collider2D collider){
            float density=F(d["m_Density"]);
            if(collider.density!=density){var rb=collider.attachedRigidbody;if(rb==null)throw new InvalidOperationException("Source collider density requires a rigidbody");var kind=rb.bodyType;bool auto=rb.useAutoMass;float mass=rb.mass;rb.bodyType=RigidbodyType2D.Dynamic;rb.useAutoMass=true;collider.density=density;rb.useAutoMass=auto;rb.bodyType=kind;rb.mass=mass;}
            collider.sharedMaterial=Material(d["m_Material"]);collider.layerOverridePriority=d["m_LayerOverridePriority"].I;
            ApplyLayerCollision(collider,Mask(d["m_IncludeLayers"]),Mask(d["m_ExcludeLayers"]));
            collider.forceSendLayers=Mask(d["m_ForceSendLayers"]);collider.forceReceiveLayers=Mask(d["m_ForceReceiveLayers"]);collider.contactCaptureLayers=Mask(d["m_ContactCaptureLayers"]);collider.callbackLayers=Mask(d["m_CallbackLayers"]);
            collider.isTrigger=d["m_IsTrigger"].B;collider.usedByEffector=d["m_UsedByEffector"].B;collider.compositeOperation=(Collider2D.CompositeOperation)d["m_CompositeOperation"].I;collider.compositeOrder=d["m_CompositeOrder"].I;collider.offset=V2(d["m_Offset"]);
            if(c is BoxCollider2D box){box.size=V2(d["m_Size"]);box.edgeRadius=F(d["m_EdgeRadius"]);box.autoTiling=d["m_AutoTiling"].B;}
            if(c is CircleCollider2D circle)circle.radius=F(d["m_Radius"]);
            if(c is CapsuleCollider2D capsule){capsule.size=V2(d["m_Size"]);capsule.direction=(CapsuleDirection2D)d["m_Direction"].I;}
            if(c is PolygonCollider2D polygon){var paths=d["m_Points"]["m_Paths"].A;polygon.pathCount=paths.Count;for(int i=0;i<paths.Count;i++)polygon.SetPath(i,paths[i].A.Select(V2).ToArray());polygon.autoTiling=d["m_AutoTiling"].B;polygon.useDelaunayMesh=d["m_UseDelaunayMesh"].B;}
        }
        if(c is Joint2D joint){
            int connected=Id(d["m_ConnectedRigidBody"]);
            if(connected!=0&&!Bodies.ContainsKey(connected))throw new InvalidOperationException("Missing connectedBody "+connected);
            bool self=connected!=0&&Bodies[connected]==joint.attachedRigidbody;
            if(self){int ownId=Components.First(p=>p.Value==c).Key;PendingSelfJoints.Add(ownId);}
            joint.connectedBody=connected==0||self?null:Bodies[connected];joint.enableCollision=d["m_EnableCollision"].B;joint.breakForce=F(d["m_BreakForce"]);joint.breakTorque=F(d["m_BreakTorque"]);joint.breakAction=(JointBreakAction2D)d["m_BreakAction"].I;
            var anchored=(AnchoredJoint2D)joint;anchored.autoConfigureConnectedAnchor=false;anchored.anchor=V2(d["m_Anchor"]);anchored.connectedAnchor=V2(d["m_ConnectedAnchor"]);anchored.autoConfigureConnectedAnchor=!self&&d["m_AutoConfigureConnectedAnchor"].B;
            if(joint is FixedJoint2D fixedJoint){fixedJoint.dampingRatio=F(d["m_DampingRatio"]);fixedJoint.frequency=F(d["m_Frequency"]);}
            if(joint is HingeJoint2D hinge){hinge.useConnectedAnchor=d["m_UseConnectedAnchor"].B;hinge.motor=new JointMotor2D{motorSpeed=F(d["m_Motor"]["m_MotorSpeed"]),maxMotorTorque=F(d["m_Motor"]["m_MaximumMotorForce"])};hinge.limits=new JointAngleLimits2D{min=F(d["m_AngleLimits"]["m_LowerAngle"]),max=F(d["m_AngleLimits"]["m_UpperAngle"])};hinge.useMotor=d["m_UseMotor"].B;hinge.useLimits=d["m_UseLimits"].B;}
        }
        if(c is AreaEffector2D effector){effector.useColliderMask=d["m_UseColliderMask"].B;effector.colliderMask=Mask(d["m_ColliderMask"]);effector.useGlobalAngle=d["m_UseGlobalAngle"].B;effector.forceAngle=F(d["m_ForceAngle"]);effector.forceMagnitude=F(d["m_ForceMagnitude"]);effector.forceVariation=F(d["m_ForceVariation"]);effector.forceTarget=(EffectorSelection2D)d["m_ForceTarget"].I;effector.linearDamping=F(d["m_LinearDamping"]);effector.angularDamping=F(d["m_AngularDamping"]);}
    }
    // Call immediately after Create, before equipment/configuration changes.
    // This checks actual Unity objects field by field, not merely node counts.
    public J ValidateSource(){
        int checks=0;var errors=new List<J>();
        void Check(bool ok,string key){checks++;if(!ok)errors.Add(new J(key));}
        bool Near(float a,float b)=>a==b||!float.IsNaN(a)&&!float.IsNaN(b)&&!float.IsInfinity(a)&&!float.IsInfinity(b)&&Math.Abs(a-b)<=.00002f*Math.Max(1,Math.Max(Math.Abs(a),Math.Abs(b)));
        bool Equal(object actual,J expected){
            if(actual is bool boolean)return boolean==expected.B;
            if(actual is Vector2 v2)return Near(v2.x,F(expected["x"]))&&Near(v2.y,F(expected["y"]));
            if(actual is Vector3 v3)return Near(v3.x,F(expected["x"]))&&Near(v3.y,F(expected["y"]))&&Near(v3.z,F(expected["z"]));
            if(actual is Quaternion q)return Near(q.x,F(expected["x"]))&&Near(q.y,F(expected["y"]))&&Near(q.z,F(expected["z"]))&&Near(q.w,F(expected["w"]));
            if(actual is LayerMask mask)return mask.value==Mask(expected);
            if(actual is float number)return Near(number,F(expected));
            if(expected["m_Bits"].Has)return Convert.ToInt64(actual)==Mask(expected);
            return Convert.ToInt64(actual)==(long)expected.N;
        }
        object Get(object instance,string path){foreach(string part in path.Split('.')){var type=instance.GetType();var property=type.GetProperty(part);instance=property!=null?property.GetValue(instance,null):type.GetField(part).GetValue(instance);}return instance;}
        void Properties(Component c,J raw,string pairs,int id){foreach(var entry in pairs.Split(' ')){string[] fields=entry.Split(':');Check(Equal(Get(c,fields[1]),raw[fields[0]]),id+"."+fields[0]);}}
        string commonCollider="m_Enabled:enabled m_Density:density m_LayerOverridePriority:layerOverridePriority m_ForceSendLayers:forceSendLayers m_ForceReceiveLayers:forceReceiveLayers m_ContactCaptureLayers:contactCaptureLayers m_CallbackLayers:callbackLayers m_IsTrigger:isTrigger m_UsedByEffector:usedByEffector m_CompositeOperation:compositeOperation m_CompositeOrder:compositeOrder m_Offset:offset";
        foreach(var pair in Data["objects"].D){int id=int.Parse(pair.Key);var go=Objects[id];Check(go.name==pair.Value["m_Name"].S,id+".name");Check(go.layer==pair.Value["m_Layer"].I,id+".layer");Check(go.activeSelf==pair.Value["m_IsActive"].B,id+".activeSelf");}
        foreach(var pair in Data["transforms"].D){
            int id=int.Parse(pair.Key);J raw=pair.Value;var t=Transforms[id];J position=J.Parse(raw["m_LocalPosition"].ToString());if(id==Data["root_transform_id"].I)position["x"]=new J(initialSpawnX);
            Check(Equal(t.localPosition,position),id+".localPosition");Check(Equal(t.localRotation,raw["m_LocalRotation"]),id+".localRotation");Check(Equal(t.localScale,raw["m_LocalScale"]),id+".localScale");
            int parent=Id(raw["m_Father"]);if(parent!=0)Check(t.parent==Transforms[parent],id+".parent");
            int index=0;foreach(var child in raw["m_Children"].A)Check(Transforms[Id(child)].GetSiblingIndex()==index++,id+".childOrder");
        }
        foreach(var record in Data["physics"].A){
            int id=record["id"].I;J d=record["data"];var c=Components[id];Check(c.GetType().Name==record["type"].S,id+".componentType");Check(c.gameObject==Objects[Id(d["m_GameObject"])],id+".owner");
            if(c is Rigidbody2D body){
                Properties(c,d,"m_BodyType:bodyType m_Simulated:simulated m_UseFullKinematicContacts:useFullKinematicContacts m_UseAutoMass:useAutoMass m_Mass:mass m_LinearDamping:linearDamping m_AngularDamping:angularDamping m_GravityScale:gravityScale m_IncludeLayers:includeLayers m_ExcludeLayers:excludeLayers m_Interpolate:interpolation m_SleepingMode:sleepMode m_CollisionDetection:collisionDetectionMode m_Constraints:constraints",id);
                Check(body.sharedMaterial==Material(d["m_Material"]),id+".material");
            }
            if(c is Collider2D collider){
                Properties(c,d,commonCollider,id);Check(collider.sharedMaterial==Material(d["m_Material"]),id+".material");
                uint row=Matrix(c.gameObject.layer);Check(collider.includeLayers.value==unchecked((int)(row|(uint)Mask(d["m_IncludeLayers"]))),id+".includeLayers+sourceMatrix");Check(collider.excludeLayers.value==unchecked((int)(~row|(uint)Mask(d["m_ExcludeLayers"]))),id+".excludeLayers+sourceMatrix");
                if(c is BoxCollider2D)Properties(c,d,"m_Size:size m_EdgeRadius:edgeRadius m_AutoTiling:autoTiling",id);
                if(c is CircleCollider2D)Properties(c,d,"m_Radius:radius",id);
                if(c is CapsuleCollider2D)Properties(c,d,"m_Size:size m_Direction:direction",id);
                if(c is PolygonCollider2D polygon){Properties(c,d,"m_AutoTiling:autoTiling m_UseDelaunayMesh:useDelaunayMesh",id);var paths=d["m_Points"]["m_Paths"].A;Check(polygon.pathCount==paths.Count,id+".pathCount");for(int path=0;path<paths.Count;path++){var actual=polygon.GetPath(path);Check(actual.Length==paths[path].A.Count,id+".pathLength"+path);for(int i=0;i<Math.Min(actual.Length,paths[path].A.Count);i++)Check(Equal(actual[i],paths[path][i]),id+".point"+path+"/"+i);}}
            }
            if(c is Joint2D joint){
                Properties(c,d,"m_EnableCollision:enableCollision m_BreakForce:breakForce m_BreakTorque:breakTorque m_BreakAction:breakAction m_Anchor:anchor m_ConnectedAnchor:connectedAnchor",id);
                int target=Id(d["m_ConnectedRigidBody"]);bool pending=PendingSelfJoints.Contains(id);
                Check(joint.connectedBody==(pending||target==0?null:Bodies[target]),id+".connectedBody:"+target);Check(joint.enabled==(d["m_Enabled"].B&&!pending),id+".enabled");
                Check(((AnchoredJoint2D)joint).autoConfigureConnectedAnchor==(d["m_AutoConfigureConnectedAnchor"].B&&!pending),id+".autoConfigureConnectedAnchor");
                if(pending)Check(target!=0&&Bodies[target]==joint.attachedRigidbody,id+".pendingSelfReference");
                if(c is FixedJoint2D)Properties(c,d,"m_DampingRatio:dampingRatio m_Frequency:frequency",id);
                if(c is HingeJoint2D hinge){Properties(c,d,"m_UseConnectedAnchor:useConnectedAnchor m_UseMotor:useMotor m_UseLimits:useLimits",id);Check(Near(hinge.motor.motorSpeed,F(d["m_Motor"]["m_MotorSpeed"])),id+".motorSpeed");Check(Near(hinge.motor.maxMotorTorque,F(d["m_Motor"]["m_MaximumMotorForce"])),id+".maxMotorTorque");Check(Near(hinge.limits.min,F(d["m_AngleLimits"]["m_LowerAngle"])),id+".lowerLimit");Check(Near(hinge.limits.max,F(d["m_AngleLimits"]["m_UpperAngle"])),id+".upperLimit");}
            }
            if(c is AreaEffector2D)Properties(c,d,"m_Enabled:enabled m_UseColliderMask:useColliderMask m_ColliderMask:colliderMask m_UseGlobalAngle:useGlobalAngle m_ForceAngle:forceAngle m_ForceMagnitude:forceMagnitude m_ForceVariation:forceVariation m_ForceTarget:forceTarget m_LinearDamping:linearDamping m_AngularDamping:angularDamping",id);
        }
        foreach(var pair in Materials){J raw=Catalog["materials"][pair.Key];var material=pair.Value;Check(Near(material.friction,F(raw["friction"])),pair.Key+".friction");Check(Near(material.bounciness,F(raw["bounciness"])),pair.Key+".bounce");Check((int)material.frictionCombine==raw["m_FrictionCombine"].I,pair.Key+".frictionCombine");Check((int)material.bounceCombine==raw["m_BounceCombine"].I,pair.Key+".bounceCombine");}
        Check(RootBody==Resolve<Rigidbody2D>(Source("CharacterMover")["baseRigidbody2D"]),"RootBody");
        return J.O("source",SourceId,"checks",checks,"failures",errors.Count,"errors",errors,"pending_self_joints",new List<J>(PendingSelfJoints.Select(id=>new J(id))));
    }
}
