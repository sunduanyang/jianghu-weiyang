using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using UnityEngine;
using Unity.U2D.Physics;
using Jianghu;

// The existing Components are authoring/save/render state. In this backend no
// PhysicsScene2D is simulated: bodies, constraints, contacts and triggers are
// advanced exclusively by Physics Core 2D, then copied back to the shared rig.
public sealed class DuelCoreWorld : IDisposable, PhysicsCallbacks.IContactFilterCallback {
    readonly PhysicsDuel arena;
    PhysicsWorld world;
    PhysicsBody ground;
    sealed class Body {
        public Rigidbody2D proxy; public PhysicsBody native;
        public Vector2 position,velocity,preVelocity,preCenter; public float angle,spin,preSpin;
        public float mass,inertia; public Vector2 center;
        public Quaternion outputRotation;
        public float damping=float.NaN,angularDamping=float.NaN,gravity=float.NaN;
        public RigidbodyConstraints2D constraints=(RigidbodyConstraints2D)(-1);
    }
    sealed class Shape {
        public Collider2D proxy; public int id; public readonly List<PhysicsShape> natives=new List<PhysicsShape>();
        public int layer,include,exclude; public bool trigger;
        public PhysicsMaterial2D material; public float friction,bounce; public int frictionMode,bounceMode;
        public Matrix4x4 localMatrix;
    }
    sealed class Joint {
        public Joint2D proxy; public PhysicsJoint native; public Rigidbody2D other;
        public Vector2 anchor,connected; public float reference;
        public bool configured,motor,limit;public float speed,torque,lower,upper,frequency,damping;
    }
    struct Effort {public Rigidbody2D body;public Vector2 force,point;public float torque;public bool atPoint,angular,impulse;}
    struct Target {public Vector2 position;public float angle;public bool move,rotate;}
    readonly Dictionary<Rigidbody2D,Body> bodies=new Dictionary<Rigidbody2D,Body>();
    readonly Dictionary<Collider2D,Shape> shapes=new Dictionary<Collider2D,Shape>();
    readonly Dictionary<PhysicsShape,Shape> shapeOwners=new Dictionary<PhysicsShape,Shape>();
    readonly Dictionary<Collider2D,PhysicsBody> oneWayBodies=new Dictionary<Collider2D,PhysicsBody>();
    readonly Dictionary<Joint2D,Joint> joints=new Dictionary<Joint2D,Joint>();
    readonly HashSet<ulong> ignored=new HashSet<ulong>();
    readonly List<Rigidbody2D> bodyBuffer=new List<Rigidbody2D>();
    readonly List<Collider2D> shapeBuffer=new List<Collider2D>();
    readonly List<Joint2D> jointBuffer=new List<Joint2D>();
    int hierarchyCount=-1;
    int solverSubsteps=64;
    float jointHertz=1000;
    readonly List<Effort> efforts=new List<Effort>();
    readonly Dictionary<Rigidbody2D,Target> targets=new Dictionary<Rigidbody2D,Target>();
    readonly Dictionary<ulong,DuelCollision> contacts=new Dictionary<ulong,DuelCollision>();
    HashSet<ulong> previousContacts=new HashSet<ulong>(),nextContacts=new HashSet<ulong>();
    readonly HashSet<ulong> sensorPairs=new HashSet<ulong>();
    readonly PhysicsShapeGroup2D geometry=new PhysicsShapeGroup2D();
    readonly List<Vector2> vertices=new List<Vector2>();
    public long Steps {get;private set;}
    public double SimulationMilliseconds {get;private set;}
    public double TotalMilliseconds {get;private set;}
    public int DeliveredContacts {get;private set;}
    public int DeliveredTriggers {get;private set;}
    public int BodyCount=>bodies.Count;
    public int ShapeCount=>shapeOwners.Count;
    public int JointCount=>joints.Count;
    public DuelCoreWorld(PhysicsDuel arena){this.arena=arena;}
    public void Dispose(){if(world.isValid)world.Destroy();bodies.Clear();shapes.Clear();shapeOwners.Clear();oneWayBodies.Clear();joints.Clear();efforts.Clear();targets.Clear();hierarchyCount=-1;}
    public void Reset(){Dispose();ignored.Clear();previousContacts.Clear();nextContacts.Clear();sensorPairs.Clear();Steps=0;SimulationMilliseconds=TotalMilliseconds=0;DeliveredContacts=DeliveredTriggers=0;}
    static ulong Pair(int a,int b){uint x=unchecked((uint)a),y=unchecked((uint)b);return x<y?((ulong)x<<32)|y:((ulong)y<<32)|x;}
    public void Ignore(Collider2D a,Collider2D b,bool ignore){if(a==null||b==null)return;ulong key=Pair(a.GetEntityId().GetHashCode(),b.GetEntityId().GetHashCode());if(ignore)ignored.Add(key);else ignored.Remove(key);}
    public bool OnContactFilter2D(PhysicsEvents.ContactFilterEvent e){
        // Called by worker threads. Dictionaries/sets are immutable during
        // Simulate; this callback never reads GameObjects or calls UnityEngine.
        return !shapeOwners.TryGetValue(e.shapeA,out var a)||!shapeOwners.TryGetValue(e.shapeB,out var b)||!ignored.Contains(Pair(a.id,b.id));
    }
    void CreateWorld(){
        if(world.isValid)return;
        // Fast hand drives and heavy contacts use the same fine solve.
        // Preserve single-hand compliance for kick/fall trajectories;
        // the high-inertia two-hand loop needs the higher frequency.
        bool twoHanded=arena.player!=null&&arena.player.weapon.TwoHanded||arena.enemy!=null&&arena.enemy.weapon.TwoHanded;
        solverSubsteps=64;jointHertz=twoHanded?2000:1000;
        if(int.TryParse(GameClient.Arg("-coreSolverSteps"),out int substeps))solverSubsteps=Mathf.Clamp(substeps,1,128);
        if(float.TryParse(GameClient.Arg("-coreJointHertz"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float hertz))jointHertz=Mathf.Clamp(hertz,30,5000);
        var d=PhysicsWorldDefinition.defaultDefinition;
        d.simulationType=PhysicsWorld.SimulationType.Script;d.transformWriteMode=PhysicsWorld.TransformWriteMode.Off;
        d.simulationSubSteps=solverSubsteps;d.simulationWorkers=1;d.gravity=Physics2D.gravity;
        d.contactFilterCallbacks=true;d.autoContactCallbacks=false;d.autoTriggerCallbacks=false;
        d.sleepingAllowed=true;d.continuousAllowed=true;d.maximumLinearSpeed=100;
        world=PhysicsWorld.Create(d);
        var g=PhysicsBodyDefinition.defaultDefinition;g.type=PhysicsBody.BodyType.Static;ground=world.CreateBody(g);
    }
    Body GetBody(Rigidbody2D rb){
        if(bodies.TryGetValue(rb,out var entry))return entry;
        var d=PhysicsBodyDefinition.defaultDefinition;
        d.type=Type(rb.bodyType);d.position=rb.position;d.rotation=PhysicsRotate.FromDegrees(rb.rotation);
        d.transformWriteMode=PhysicsBody.TransformWriteMode.Off;d.sleepingAllowed=rb.sleepMode!=RigidbodySleepMode2D.NeverSleep;d.awake=rb.sleepMode!=RigidbodySleepMode2D.StartAsleep;d.fastRotationAllowed=true;
        d.fastCollisionsAllowed=rb.collisionDetectionMode==CollisionDetectionMode2D.Continuous;
        entry=new Body{proxy=rb,native=world.CreateBody(d),position=rb.position,angle=rb.rotation,
            velocity=rb.linearVelocity,spin=rb.angularVelocity,mass=-1,inertia=-1};
        bodies.Add(rb,entry);if(rb.bodyType!=RigidbodyType2D.Static){entry.native.linearVelocity=entry.velocity;entry.native.angularVelocity=entry.spin;}return entry;
    }
    static PhysicsBody.BodyType Type(RigidbodyType2D type)=>type==RigidbodyType2D.Dynamic?PhysicsBody.BodyType.Dynamic:type==RigidbodyType2D.Kinematic?PhysicsBody.BodyType.Kinematic:PhysicsBody.BodyType.Static;
    void SyncBody(Body e){
        var p=e.proxy;var b=e.native;bool active=p!=null&&p.simulated&&p.gameObject.activeInHierarchy;
        if(b.enabled!=active)b.enabled=active;if(!active)return;
        if(b.type!=Type(p.bodyType))b.type=Type(p.bodyType);
        if(p.position!=e.position)b.position=p.position;
        if(p.rotation!=e.angle)b.rotation=PhysicsRotate.FromDegrees(p.rotation);
        if(p.bodyType!=RigidbodyType2D.Static){if(p.linearVelocity!=e.velocity)b.linearVelocity=p.linearVelocity;if(p.angularVelocity!=e.spin)b.angularVelocity=p.angularVelocity;}
        if(e.gravity!=p.gravityScale){b.gravityScale=p.gravityScale;e.gravity=p.gravityScale;}
        if(e.damping!=p.linearDamping){b.linearDamping=p.linearDamping;e.damping=p.linearDamping;}
        if(e.angularDamping!=p.angularDamping){b.angularDamping=p.angularDamping;e.angularDamping=p.angularDamping;}
        if(e.constraints!=p.constraints){b.constraints=(PhysicsBody.BodyConstraints)(int)p.constraints;e.constraints=p.constraints;}
        if(p.bodyType==RigidbodyType2D.Dynamic&&(e.mass!=p.mass||e.inertia!=p.inertia||e.center!=p.centerOfMass)){
            e.mass=p.mass;e.inertia=p.inertia;e.center=p.centerOfMass;
            b.massConfiguration=new PhysicsBody.MassConfiguration{mass=Mathf.Max(.0001f,p.mass),center=p.centerOfMass,rotationalInertia=Mathf.Max(.000001f,p.inertia)};
        }
        e.position=p.position;e.angle=p.rotation;e.velocity=p.linearVelocity;e.spin=p.angularVelocity;
    }
    PhysicsBody ShapeBody(Collider2D p){
        if(p.attachedRigidbody==null)return ground;
        var body=GetBody(p.attachedRigidbody).native;
        if(p.forceReceiveLayers.value!=0)return body;
        // The source corpse/item collider sends forces but never receives them.
        // A kinematic collision proxy preserves that one-way response while the
        // original dynamic body's mass and joint remain in the character graph.
        if(!oneWayBodies.TryGetValue(p,out var collisionBody)){
            var d=PhysicsBodyDefinition.defaultDefinition;d.type=PhysicsBody.BodyType.Kinematic;d.position=body.position;d.rotation=body.rotation;d.transformWriteMode=PhysicsBody.TransformWriteMode.Off;
            collisionBody=world.CreateBody(d);oneWayBodies.Add(p,collisionBody);
        }
        return collisionBody;
    }
    Shape AddShape(Collider2D p){
        var e=new Shape{proxy=p,id=p.GetEntityId().GetHashCode(),layer=-1,include=int.MinValue,exclude=int.MinValue,friction=-1,bounce=-1};
        e.localMatrix=LocalMatrix(p);
        shapes.Add(p,e);var body=ShapeBody(p);
        // GetShapes supplies Unity's actual decomposed, scaled physics geometry,
        // including mirrored source rigs. No art bounds or guessed limb boxes.
        geometry.Clear();p.GetShapes(geometry);
        var d=PhysicsShapeDefinition.defaultDefinition;d.startMassUpdate=false;d.isTrigger=p.isTrigger;
        d.contactEvents=true;d.triggerEvents=true;d.hitEvents=true;d.contactFilterCallbacks=true;
        for(int i=0;i<geometry.shapeCount;i++){
            var s=geometry.GetShape(i);vertices.Clear();geometry.GetShapeVertices(i,vertices);
            if(GameClient.Arg("-coreContactTrace")=="1"&&p.name.StartsWith("kinetic"))UnityEngine.Debug.Log("CORE_GEOMETRY_TRACE "+p.name+" body="+body.position+" matrix="+geometry.localToWorldMatrix+" original="+string.Join(",",vertices));
            for(int n=0;n<vertices.Count;n++)vertices[n]=body.GetLocalPoint(geometry.localToWorldMatrix.MultiplyPoint3x4(vertices[n]));
            PhysicsShape native;
            if(s.shapeType==PhysicsShapeType2D.Circle)native=body.CreateShape(CircleGeometry.Create(s.radius,vertices[0]),d);
            else if(s.shapeType==PhysicsShapeType2D.Capsule)native=body.CreateShape(CapsuleGeometry.Create(vertices[0],vertices[1],s.radius),d);
            else if(s.shapeType==PhysicsShapeType2D.Polygon&&p is BoxCollider2D&&vertices.Count==4){
                // Construct rectangles directly. The hull-welding constructor
                // rejects the source's 1 cm COM boxes at its default tolerance.
                // This retains the exact four corners without enlarging them.
                Vector2 edge=vertices[1]-vertices[0],edge2=vertices[2]-vertices[1];
                Vector2 center=(vertices[0]+vertices[1]+vertices[2]+vertices[3])*.25f;
                native=body.CreateShape(PolygonGeometry.CreateBox(new Vector2(edge.magnitude,edge2.magnitude),s.radius,new PhysicsTransform(center,PhysicsRotate.FromDegrees(Mathf.Atan2(edge.y,edge.x)*Mathf.Rad2Deg))),d);
            }
            else if(s.shapeType==PhysicsShapeType2D.Polygon)native=body.CreateShape(PolygonGeometry.Create(vertices.ToArray(),s.radius),d);
            else throw new NotSupportedException("Combat collider geometry: "+s.shapeType);
            if(!native.isValid)throw new InvalidOperationException("Invalid Core geometry "+p.name+" "+s.shapeType+" radius="+s.radius+" vertices="+string.Join(",",vertices));
            native.callbackTarget=this;e.natives.Add(native);shapeOwners.Add(native,e);
        }
        if(e.natives.Count==0)throw new InvalidOperationException("No native geometry for active combat collider "+p.name);
        return e;
    }
    void RemoveShape(Shape e){foreach(var s in e.natives){shapeOwners.Remove(s);if(s.isValid)s.Destroy(false);}if(oneWayBodies.TryGetValue(e.proxy,out var body)){body.Destroy();oneWayBodies.Remove(e.proxy);}shapes.Remove(e.proxy);}
    static Matrix4x4 LocalMatrix(Collider2D p){var rb=p.attachedRigidbody;return rb==null?p.transform.localToWorldMatrix:Matrix4x4.TRS(rb.position,Quaternion.Euler(0,0,rb.rotation),Vector3.one).inverse*p.transform.localToWorldMatrix;}
    static bool GeometryMoved(Shape e){var m=LocalMatrix(e.proxy);var p=e.localMatrix;return Mathf.Abs(m.m00-p.m00)>1e-5f||Mathf.Abs(m.m01-p.m01)>1e-5f||Mathf.Abs(m.m10-p.m10)>1e-5f||Mathf.Abs(m.m11-p.m11)>1e-5f||Mathf.Abs(m.m03-p.m03)>1e-5f||Mathf.Abs(m.m13-p.m13)>1e-5f;}
    void SyncShape(Shape e){
        var p=e.proxy;bool trigger=p.isTrigger;
        int include=p.includeLayers.value,exclude=p.excludeLayers.value;
        if(p.attachedRigidbody!=null){include|=p.attachedRigidbody.includeLayers.value;exclude|=p.attachedRigidbody.excludeLayers.value;}
        if(e.layer!=p.gameObject.layer||e.include!=include||e.exclude!=exclude||e.trigger!=trigger){
            uint mask=unchecked((uint)((Physics2D.GetLayerCollisionMask(p.gameObject.layer)|include)&~exclude));
            var filter=new PhysicsShape.ContactFilter(new PhysicsMask((LayerMask)(1<<p.gameObject.layer)),new PhysicsMask((LayerMask)unchecked((int)mask)),0);
            foreach(var s0 in e.natives){var s=s0;s.contactFilter=filter;s.isTrigger=trigger;}
            e.layer=p.gameObject.layer;e.include=include;e.exclude=exclude;e.trigger=trigger;
        }
        var m=p.sharedMaterial??(p.attachedRigidbody!=null?p.attachedRigidbody.sharedMaterial:null);
        float friction=m!=null?m.friction:.4f;
        float bounce=m!=null?m.bounciness:0;int fm=m!=null?(int)m.frictionCombine:0,bm=m!=null?(int)m.bounceCombine:0;
        if(e.material!=m||e.friction!=friction||e.bounce!=bounce||e.frictionMode!=fm||e.bounceMode!=bm){
            var material=PhysicsShape.SurfaceMaterial.defaultMaterial;material.friction=friction;material.bounciness=bounce;
            material.frictionMixing=(PhysicsShape.SurfaceMaterial.MixingMode)fm;material.bouncinessMixing=(PhysicsShape.SurfaceMaterial.MixingMode)bm;
            foreach(var s0 in e.natives){var s=s0;s.surfaceMaterial=material;}
            e.material=m;e.friction=friction;e.bounce=bounce;e.frictionMode=fm;e.bounceMode=bm;
        }
    }
    Vector2 Anchor(Rigidbody2D body,Vector2 p)=>body==null?p:Vector2.Scale(p,(Vector2)body.transform.lossyScale);
    Joint AddJoint(Joint2D p){
        var e=new Joint{proxy=p,other=p.connectedBody};
        if(p is AnchoredJoint2D a){e.anchor=a.anchor;e.connected=a.connectedAnchor;}
        e.reference=p.attachedRigidbody.rotation-(p.connectedBody!=null?p.connectedBody.rotation:0);
        var own=GetBody(p.attachedRigidbody).native;var other=p.connectedBody!=null?GetBody(p.connectedBody).native:ground;
        var la=new PhysicsTransform(Anchor(p.attachedRigidbody,e.anchor));
        var lb=new PhysicsTransform(Anchor(p.connectedBody,e.connected),PhysicsRotate.FromDegrees(e.reference));
        if(p is HingeJoint2D){
            var d=PhysicsHingeJointDefinition.defaultDefinition;d.bodyA=own;d.bodyB=other;d.localAnchorA=la;d.localAnchorB=lb;d.collideConnected=p.enableCollision;
            e.native=world.CreateJoint(d);
        }else if(p is FixedJoint2D fixedJoint){
            var d=PhysicsFixedJointDefinition.defaultDefinition;d.bodyA=own;d.bodyB=other;d.localAnchorA=la;d.localAnchorB=lb;d.collideConnected=p.enableCollision;
            d.linearFrequency=d.angularFrequency=fixedJoint.frequency;d.linearDamping=d.angularDamping=fixedJoint.dampingRatio;
            e.native=world.CreateJoint(d);
        }else if(p is TargetJoint2D target){
            var d=PhysicsRelativeJointDefinition.defaultDefinition;d.bodyA=ground;d.bodyB=own;
            d.localAnchorA=new PhysicsTransform(target.target);d.localAnchorB=new PhysicsTransform(Anchor(p.attachedRigidbody,target.anchor));
            d.maxForce=d.maxTorque=.000001f;d.springAngularFrequency=0;
            d.springLinearFrequency=target.frequency;d.springLinearDamping=target.dampingRatio;d.springMaxForce=target.maxForce;
            e.native=world.CreateJoint(d);
        }else throw new NotSupportedException("Combat joint: "+p.GetType().Name);
        joints.Add(p,e);return e;
    }
    void SyncJoint(Joint e){
        var p=e.proxy;
        if(!e.configured){e.native.tuningFrequency=jointHertz;e.native.tuningDamping=1;}
        if(p is HingeJoint2D hinge){var j=new PhysicsHingeJoint(e.native);
            if(!e.configured||e.motor!=hinge.useMotor){j.enableMotor=hinge.useMotor;e.motor=hinge.useMotor;}
            if(!e.configured||e.speed!=hinge.motor.motorSpeed){j.motorSpeed=hinge.motor.motorSpeed;e.speed=hinge.motor.motorSpeed;}
            if(!e.configured||e.torque!=hinge.motor.maxMotorTorque){j.maxMotorTorque=hinge.motor.maxMotorTorque;e.torque=hinge.motor.maxMotorTorque;}
            float lower=Mathf.Min(hinge.limits.min,hinge.limits.max),upper=Mathf.Max(hinge.limits.min,hinge.limits.max);
            if(!e.configured||e.limit!=hinge.useLimits){j.enableLimit=hinge.useLimits;e.limit=hinge.useLimits;}
            if(!e.configured||e.lower!=lower||e.upper!=upper){
                // Each native setter clamps against the other endpoint. Expand
                // first, then contract, so [20,20] cannot silently become [0,20].
                if(lower>j.upperAngleLimit)j.upperAngleLimit=upper;
                if(upper<j.lowerAngleLimit)j.lowerAngleLimit=lower;
                j.lowerAngleLimit=lower;j.upperAngleLimit=upper;e.lower=lower;e.upper=upper;
            }
        }else if(p is FixedJoint2D fixedJoint){var j=new PhysicsFixedJoint(e.native);
            if(!e.configured||e.frequency!=fixedJoint.frequency){j.linearFrequency=j.angularFrequency=fixedJoint.frequency;e.frequency=fixedJoint.frequency;}
            if(!e.configured||e.damping!=fixedJoint.dampingRatio){j.linearDamping=j.angularDamping=fixedJoint.dampingRatio;e.damping=fixedJoint.dampingRatio;}
        }else if(p is TargetJoint2D target){var j=new PhysicsRelativeJoint(e.native);j.localAnchorA=new PhysicsTransform(target.target);j.springMaxForce=target.maxForce;j.springLinearFrequency=target.frequency;j.springLinearDamping=target.dampingRatio;}
        e.configured=true;
    }
    public void Synchronize(){
        CreateWorld();Physics2D.SyncTransforms();world.gravity=Physics2D.gravity;
        int count=arena.transform.hierarchyCount;
        if(hierarchyCount!=count){arena.GetComponentsInChildren(true,bodyBuffer);arena.GetComponentsInChildren(true,shapeBuffer);arena.GetComponentsInChildren(true,jointBuffer);hierarchyCount=count;}
        foreach(var rb in bodyBuffer)if(rb.gameObject.activeInHierarchy||bodies.ContainsKey(rb))SyncBody(GetBody(rb));
        var removed=new List<Shape>();foreach(var e in shapes.Values)if(e.proxy==null||!e.proxy.enabled||!e.proxy.gameObject.activeInHierarchy||GeometryMoved(e))removed.Add(e);
        foreach(var e in removed)RemoveShape(e);
        foreach(var p in shapeBuffer)if(p.enabled&&p.gameObject.activeInHierarchy){if(!shapes.TryGetValue(p,out var e))e=AddShape(p);SyncShape(e);}
        var removeJoints=new List<Joint>();
        foreach(var e in joints.Values){var p=e.proxy;
            if(p==null||!p.enabled||!p.gameObject.activeInHierarchy||p.connectedBody!=e.other||p.attachedRigidbody==null||!p.attachedRigidbody.simulated||p is TargetJoint2D t&&t.maxForce<=0||p is AnchoredJoint2D a&&(a.anchor!=e.anchor||a.connectedAnchor!=e.connected))removeJoints.Add(e);
        }
        foreach(var e in removeJoints){if(e.native.isValid)e.native.Destroy();joints.Remove(e.proxy);}
        foreach(var p in jointBuffer)if(p.enabled&&p.gameObject.activeInHierarchy&&p.attachedRigidbody!=null&&p.attachedRigidbody.simulated&&(!(p is TargetJoint2D target)||target.maxForce>0)){
            if(!joints.TryGetValue(p,out var e))e=AddJoint(p);SyncJoint(e);
        }
    }
    void PublishVelocity(Body e){e.velocity=e.proxy.bodyType==RigidbodyType2D.Static?Vector2.zero:e.native.linearVelocity;e.spin=e.proxy.bodyType==RigidbodyType2D.Static?0:e.native.angularVelocity;e.proxy.linearVelocity=e.velocity;e.proxy.angularVelocity=e.spin;}
    public void Force(Rigidbody2D b,Vector2 f,Vector2 point,bool atPoint,bool impulse){
        if(b.bodyType!=RigidbodyType2D.Dynamic)return;
        if(impulse&&world.isValid&&bodies.TryGetValue(b,out var e)){SyncBody(e);if(atPoint)e.native.ApplyLinearImpulse(f,point);else e.native.ApplyLinearImpulseToCenter(f);PublishVelocity(e);return;}
        efforts.Add(new Effort{body=b,force=f,point=point,atPoint=atPoint,impulse=impulse});
    }
    public void Torque(Rigidbody2D b,float t,bool impulse){
        if(b.bodyType!=RigidbodyType2D.Dynamic)return;
        if(impulse&&world.isValid&&bodies.TryGetValue(b,out var e)){SyncBody(e);e.native.ApplyAngularImpulse(t);PublishVelocity(e);return;}
        efforts.Add(new Effort{body=b,torque=t,angular=true,impulse=impulse});
    }
    public void Move(Rigidbody2D b,Vector2 p){targets.TryGetValue(b,out var t);t.position=p;t.move=true;targets[b]=t;}
    public void Rotate(Rigidbody2D b,float a){targets.TryGetValue(b,out var t);t.angle=a;t.rotate=true;targets[b]=t;}
    public float Angle(HingeJoint2D j){if(joints.TryGetValue(j,out var e)&&e.native.isValid)return new PhysicsHingeJoint(e.native).angle;return j.jointAngle;}
    public J JointDiagnostic(HingeJoint2D j){if(!joints.TryGetValue(j,out var e))return J.O("native",false);var h=new PhysicsHingeJoint(e.native);return J.O("native",true,"core_angle",h.angle,"core_lower",h.lowerAngleLimit,"core_upper",h.upperAngleLimit,"core_error",h.currentAngularSeparationError,"reference",e.reference,"legacy_reference",j.referenceAngle,"legacy_angle",j.jointAngle,"body_relative",j.connectedBody!=null?j.connectedBody.rotation-j.attachedRigidbody.rotation:0);}
    public float Reference(AnchoredJoint2D j){if(joints.TryGetValue(j,out var e))return e.reference;return j is HingeJoint2D h?h.referenceAngle:j is FixedJoint2D f?f.referenceAngle:0;}
    public Vector2 Reaction(Joint2D j)=>joints.TryGetValue(j,out var e)&&e.native.isValid?e.native.currentConstraintForce:Vector2.zero;
    public float ReactionTorque(Joint2D j)=>joints.TryGetValue(j,out var e)&&e.native.isValid?e.native.currentConstraintTorque:0;
    Vector2 Velocity(Shape s,Vector2 point){
        var rb=s.proxy.attachedRigidbody;if(rb==null||!bodies.TryGetValue(rb,out var e))return Vector2.zero;
        Vector2 r=point-e.preCenter;return e.preVelocity+new Vector2(-r.y,r.x)*e.preSpin;
    }
    void ApplyEffectors(){
        // Preserve the authored push/damping volumes, using Core overlaps.
        foreach(var shape in shapes.Values){var p=shape.proxy;if(!p.usedByEffector)continue;var effector=p.GetComponent<AreaEffector2D>();if(effector==null||!effector.enabled)continue;
            var seen=new HashSet<Rigidbody2D>();
            foreach(var native in shape.natives)using(var visitors=native.GetTriggerVisitors(Allocator.Temp))foreach(var visitor in visitors){
                if(!shapeOwners.TryGetValue(visitor,out var target))continue;var rb=target.proxy.attachedRigidbody;
                if(rb==null||rb.bodyType!=RigidbodyType2D.Dynamic||!seen.Add(rb)||effector.useColliderMask&&(effector.colliderMask&(1<<target.proxy.gameObject.layer))==0)continue;
                var body=GetBody(rb).native;float angle=effector.forceAngle+(effector.useGlobalAngle?0:effector.transform.eulerAngles.z);
                Vector2 force=new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad))*effector.forceMagnitude;
                if(effector.forceTarget==EffectorSelection2D.Collider)body.ApplyForce(force,visitor.transform.TransformPoint(visitor.localCenter));else body.ApplyForceToCenter(force);
                body.ApplyForceToCenter(-body.linearVelocity*body.mass*effector.linearDamping);
                body.ApplyTorque(-body.angularVelocity*Mathf.Deg2Rad*body.rotationalInertia*effector.angularDamping);
            }
        }
    }
    public void Step(float dt){
        // Broadphase/contact creation happens once per Simulate call, rather
        // than once per solver substep. Bound externally requested long steps
        // too (contact probes and restore initialization use this same entry).
        int count=Mathf.Max(1,Mathf.CeilToInt(dt*(dt>1f/240?960:480)-1e-4f));
        if(count==1){StepOnce(dt);return;}
        var applied=efforts.ToArray();var requested=new Dictionary<Rigidbody2D,Target>(targets);
        var starts=new Dictionary<Rigidbody2D,Target>();foreach(var pair in requested)starts[pair.Key]=new Target{position=pair.Key.position,angle=pair.Key.rotation};
        efforts.Clear();targets.Clear();
        for(int i=0;i<count;i++){
            foreach(var effort in applied)if(i==0||!effort.impulse)efforts.Add(effort);
            foreach(var pair in requested){var t=pair.Value;var start=starts[pair.Key];t.position=Vector2.Lerp(start.position,t.position,(i+1)/(float)count);t.angle=Mathf.LerpAngle(start.angle,t.angle,(i+1)/(float)count);targets[pair.Key]=t;}
            StepOnce(dt/count);
        }
    }
    void StepOnce(float dt){
        long totalStart=Stopwatch.GetTimestamp();Synchronize();
        foreach(var op in efforts){if(op.body==null||op.body.bodyType!=RigidbodyType2D.Dynamic||!op.body.simulated||!op.body.gameObject.activeInHierarchy)continue;var b=GetBody(op.body).native;
            if(op.angular){if(op.impulse)b.ApplyAngularImpulse(op.torque);else b.ApplyTorque(op.torque);}
            else if(op.atPoint){if(op.impulse)b.ApplyLinearImpulse(op.force,op.point);else b.ApplyForce(op.force,op.point);}
            else {if(op.impulse)b.ApplyLinearImpulseToCenter(op.force);else b.ApplyForceToCenter(op.force);}
        }efforts.Clear();
        foreach(var pair in targets){if(pair.Key==null||!pair.Key.simulated)continue;var b=GetBody(pair.Key).native;var t=pair.Value;
            b.SetTransformTarget(new PhysicsTransform(t.move?t.position:b.position,t.rotate?PhysicsRotate.FromDegrees(t.angle):b.rotation),dt);
            // Match MovePosition/MoveRotation's per-tick velocity drive.
            if(t.move){b.gravityScale=0;b.linearDamping=0;bodies[pair.Key].gravity=bodies[pair.Key].damping=float.NaN;}if(t.rotate){b.angularDamping=0;bodies[pair.Key].angularDamping=float.NaN;}
        }targets.Clear();
        ApplyEffectors();
        foreach(var pair in oneWayBodies){var proxy=pair.Key;var own=GetBody(proxy.attachedRigidbody).native;var body=pair.Value;bool isStatic=proxy.attachedRigidbody.bodyType==RigidbodyType2D.Static;body.position=own.position;body.rotation=own.rotation;body.linearVelocity=isStatic?Vector2.zero:own.linearVelocity;body.angularVelocity=isStatic?0:own.angularVelocity;body.enabled=own.enabled;}
        foreach(var e in bodies.Values){e.preVelocity=e.proxy.bodyType==RigidbodyType2D.Static?Vector2.zero:e.native.linearVelocity;e.preSpin=e.proxy.bodyType==RigidbodyType2D.Static?0:e.native.angularVelocity*Mathf.Deg2Rad;e.preCenter=e.native.worldCenterOfMass;}
        long simStart=Stopwatch.GetTimestamp();world.Simulate(dt);SimulationMilliseconds+=(Stopwatch.GetTimestamp()-simStart)*1000.0/Stopwatch.Frequency;Steps++;
        if(GameClient.Arg("-coreContactTrace")=="1"&&Steps<=5)foreach(var e in bodies.Values)if(e.proxy!=null&&e.proxy.name=="kinetic probe")UnityEngine.Debug.Log("CORE_STEP_TRACE "+J.O("step",Steps,"position_x",e.native.position.x,"velocity_x",e.native.linearVelocity.x,"pre_x",e.preVelocity.x,"bullet",e.native.fastCollisionsAllowed,"type",e.native.type.ToString()));
        // Capture all world poses before moving parents. Rigidbody2D.position
        // alone does not flush the Transform during tight/manual simulation.
        // Explicitly publish both states so child colliders and art see Core's
        // current pose, rather than stale transforms from the last Unity tick.
        foreach(var rb in bodyBuffer)if(rb!=null&&bodies.TryGetValue(rb,out var e)&&e.native.enabled){
            float delta=Mathf.DeltaAngle(rb.rotation,e.native.rotation.degrees);
            e.outputRotation=Quaternion.AngleAxis(delta,Vector3.forward)*rb.transform.rotation;
            e.position=e.native.position;e.angle=rb.rotation+delta;
            e.velocity=rb.bodyType==RigidbodyType2D.Static?Vector2.zero:e.native.linearVelocity;e.spin=rb.bodyType==RigidbodyType2D.Static?0:e.native.angularVelocity;
        }
        foreach(var rb in bodyBuffer)if(rb!=null&&bodies.TryGetValue(rb,out var e)&&e.native.enabled){
            rb.transform.SetPositionAndRotation(new Vector3(e.position.x,e.position.y,rb.transform.position.z),e.outputRotation);
            rb.position=e.position;rb.rotation=e.angle;if(rb.bodyType!=RigidbodyType2D.Static){rb.linearVelocity=e.velocity;rb.angularVelocity=e.spin;}
        }
        Physics2D.SyncTransforms();
        foreach(var rb in bodyBuffer)if(rb!=null&&bodies.TryGetValue(rb,out var e)){e.position=rb.position;e.angle=rb.rotation;e.velocity=rb.linearVelocity;e.spin=rb.angularVelocity;}
        DeliverContacts();DeliverTriggers();
        TotalMilliseconds+=(Stopwatch.GetTimestamp()-totalStart)*1000.0/Stopwatch.Frequency;
    }
    void DeliverContacts(){
        contacts.Clear();nextContacts.Clear();
        foreach(var pair in shapeOwners){if(pair.Value.trigger||!pair.Key.isValid)continue;
            using(var list=pair.Key.GetContacts(Allocator.Temp))foreach(var c in list){
                if(!shapeOwners.TryGetValue(c.shapeA,out var a)||!shapeOwners.TryGetValue(c.shapeB,out var b))continue;
                ulong key=Pair(a.id,b.id);if(contacts.ContainsKey(key))continue;
                // A speculative manifold can keep an existing physical
                // contact alive while the soft solver holds a tiny gap. It
                // must not turn sustained pressure into repeated enter hits.
                if(previousContacts.Contains(key))nextContacts.Add(key);
                var m=c.manifold;int point=-1;for(int i=0;i<m.pointCount;i++)if(!m[i].speculative){point=i;break;}if(point<0)continue;
                var p=m[point];Vector2 relative=Velocity(a,p.point)-Velocity(b,p.point);
                Vector2 linearA=a.proxy.attachedRigidbody!=null&&bodies.TryGetValue(a.proxy.attachedRigidbody,out var bodyA)?bodyA.preVelocity:Vector2.zero;
                Vector2 linearB=b.proxy.attachedRigidbody!=null&&bodies.TryGetValue(b.proxy.attachedRigidbody,out var bodyB)?bodyB.preVelocity:Vector2.zero;
                contacts[key]=new DuelCollision(a.proxy,b.proxy,new DuelCollision.Point{point=p.point,normal=-m.normal,relativeVelocity=relative,linearRelativeVelocity=linearA-linearB});nextContacts.Add(key);
            }
        }
        foreach(var pair in contacts){bool entering=!previousContacts.Contains(pair.Key);Deliver(pair.Value,entering);Deliver(pair.Value.Reverse(),entering);}
        var old=previousContacts;previousContacts=nextContacts;nextContacts=old;
    }
    void Deliver(DuelCollision c,bool entering){
        if(c.otherCollider==null||c.collider==null)return;
        var contact=c.otherCollider.GetComponent<DuelContact>();if(contact!=null)contact.DeliverCore(c,entering);
        if(entering&&GameClient.Arg("-coreContactTrace")=="1"&&c.otherCollider.name=="kinetic probe")UnityEngine.Debug.Log("CORE_DELIVERY_TRACE "+J.O("other",c.collider.name,"own_kind",contact!=null?contact.kind:"missing","other_kind",c.collider.GetComponent<DuelContact>()?.kind??"missing","speed",c.GetContact(0).relativeVelocity.magnitude));
        if(!entering)return;DeliveredContacts++;
        var trace=c.otherCollider.GetComponent<DuelImpactContactTrace>();if(trace!=null)trace.RecordCore(c);
        var probe=c.otherCollider.GetComponent<DuelProbe>();if(probe!=null)probe.contacts++;
    }
    void DeliverTriggers(){
        foreach(var e in world.triggerBeginEvents)if(shapeOwners.TryGetValue(e.triggerShape,out var a)&&shapeOwners.TryGetValue(e.visitorShape,out var b)){
            a.proxy.SendMessage("OnTriggerEnter2D",b.proxy,SendMessageOptions.DontRequireReceiver);
            b.proxy.SendMessage("OnTriggerEnter2D",a.proxy,SendMessageOptions.DontRequireReceiver);DeliveredTriggers++;
        }
        // Box2D v3 sensors do not report sensor/sensor overlaps. The shipping
        // kick trigger and its receiver are both sensors. Query their actual
        // Core geometry and preserve enter-only semantics, with the same masks.
        var sensors=new List<Shape>();foreach(var shape in shapes.Values)if(shape.trigger)sensors.Add(shape);
        var overlaps=new HashSet<ulong>();
        for(int i=0;i<sensors.Count;i++)for(int j=i+1;j<sensors.Count;j++){
            var a=sensors[i];var b=sensors[j];ulong key=Pair(a.id,b.id);
            if(ignored.Contains(key)||a.proxy.attachedRigidbody==b.proxy.attachedRigidbody)continue;
            bool hit=false;foreach(var sa in a.natives){foreach(var sb in b.natives)if(sa.contactFilter.CanContact(sb.contactFilter)&&sa.Intersect(sb).pointCount>0){hit=true;break;}if(hit)break;}
            if(!hit)continue;overlaps.Add(key);if(sensorPairs.Contains(key))continue;
            a.proxy.SendMessage("OnTriggerEnter2D",b.proxy,SendMessageOptions.DontRequireReceiver);b.proxy.SendMessage("OnTriggerEnter2D",a.proxy,SendMessageOptions.DontRequireReceiver);DeliveredTriggers++;
        }
        sensorPairs.Clear();foreach(var pair in overlaps)sensorPairs.Add(pair);
    }
    public float Distance(Collider2D a,Collider2D b){
        if(!shapes.TryGetValue(a,out var x)||!shapes.TryGetValue(b,out var y))return float.PositiveInfinity;
        float distance=float.PositiveInfinity;
        foreach(var sa in x.natives)foreach(var sb in y.natives){
            // Core's GJK distance is zero for overlap. Preserve the signed
            // penetration depth expected by the existing withdrawal probes.
            float d=sa.Distance(sb,true).distance;var manifold=sa.Intersect(sb);
            for(int i=0;i<manifold.pointCount;i++)d=Mathf.Min(d,manifold[i].separation);
            distance=Mathf.Min(distance,d);
        }
        return distance;
    }
    public J Metrics()=>J.O("backend","physics-core-2d","native_steps",Steps,"solver_substeps",solverSubsteps,"joint_hertz",jointHertz,"bodies",BodyCount,"one_way_collision_bodies",oneWayBodies.Count,"shapes",ShapeCount,"joints",JointCount,
        "simulation_ms",SimulationMilliseconds,"total_ms",TotalMilliseconds,"contact_deliveries",DeliveredContacts,"trigger_entries",DeliveredTriggers,"legacy_simulation_steps",0);
}

public sealed class DuelCollision {
    public struct Point {public Vector2 point,normal,relativeVelocity,linearRelativeVelocity;}
    readonly Point value;
    readonly Collision2D legacy;
    public readonly Collider2D otherCollider,collider;
    public int contactCount=>legacy!=null?legacy.contactCount:1;
    public Vector2 relativeVelocity=>legacy!=null?legacy.relativeVelocity:value.linearRelativeVelocity;
    public DuelCollision(Collider2D own,Collider2D other,Point p){otherCollider=own;collider=other;value=p;}
    public DuelCollision(Collision2D c){legacy=c;otherCollider=c.otherCollider;collider=c.collider;}
    public Point GetContact(int index){if(legacy==null)return value;var p=legacy.GetContact(index);return new Point{point=p.point,normal=p.normal,relativeVelocity=p.relativeVelocity};}
    public DuelCollision Reverse()=>new DuelCollision(collider,otherCollider,new Point{point=value.point,normal=-value.normal,relativeVelocity=-value.relativeVelocity,linearRelativeVelocity=-value.linearRelativeVelocity});
}

// Shared controllers issue the same commands to either backend. Legacy calls
// remain untouched in the comparison path; Core queues forces until its step.
public static class DuelPhysicsAPI {
    static DuelCoreWorld Core(Rigidbody2D rb)=>PhysicsDuel.Instance!=null&&PhysicsDuel.Instance.UseCorePhysics&&rb!=null&&rb.gameObject.scene==PhysicsDuel.Instance.gameObject.scene?PhysicsDuel.Instance.CoreWorld:null;
    public static void CombatAddForce(this Rigidbody2D rb,Vector2 force,ForceMode2D mode=ForceMode2D.Force){var c=Core(rb);if(c==null)rb.AddForce(force,mode);else c.Force(rb,force,Vector2.zero,false,mode==ForceMode2D.Impulse);}
    public static void CombatAddForceAtPosition(this Rigidbody2D rb,Vector2 force,Vector2 point,ForceMode2D mode=ForceMode2D.Force){var c=Core(rb);if(c==null)rb.AddForceAtPosition(force,point,mode);else c.Force(rb,force,point,true,mode==ForceMode2D.Impulse);}
    public static void CombatAddTorque(this Rigidbody2D rb,float torque,ForceMode2D mode=ForceMode2D.Force){var c=Core(rb);if(c==null)rb.AddTorque(torque,mode);else c.Torque(rb,torque,mode==ForceMode2D.Impulse);}
    public static void CombatMovePosition(this Rigidbody2D rb,Vector2 point){var c=Core(rb);if(c==null)rb.MovePosition(point);else c.Move(rb,point);}
    public static void CombatMoveRotation(this Rigidbody2D rb,float angle){var c=Core(rb);if(c==null)rb.MoveRotation(angle);else c.Rotate(rb,angle);}
    public static float CombatJointAngle(this HingeJoint2D j)=>Core(j.attachedRigidbody)?.Angle(j)??j.jointAngle;
    public static float CombatReferenceAngle(this AnchoredJoint2D j)=>Core(j.attachedRigidbody)?.Reference(j)??(j is HingeJoint2D h?h.referenceAngle:j is FixedJoint2D f?f.referenceAngle:0);
    public static Vector2 CombatReactionForce(this Joint2D j)=>Core(j.attachedRigidbody)?.Reaction(j)??j.reactionForce;
    public static float CombatReactionTorque(this Joint2D j)=>Core(j.attachedRigidbody)?.ReactionTorque(j)??j.reactionTorque;
    public static void IgnoreCollision(Collider2D a,Collider2D b,bool ignore=true){Physics2D.IgnoreCollision(a,b,ignore);PhysicsDuel.Instance?.CoreWorld?.Ignore(a,b,ignore);}
    public static float CombatDistance(this Collider2D a,Collider2D b)=>PhysicsDuel.Instance!=null&&PhysicsDuel.Instance.UseCorePhysics?PhysicsDuel.Instance.CoreWorld.Distance(a,b):a.Distance(b).distance;
}
