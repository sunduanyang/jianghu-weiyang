using System;
using UnityEngine;

public sealed partial class PhysicsDuel {
    // Hinge servos are solved together with contacts and joint limits. Their
    // reaction torque loads the parent; only the root uses external balance PD.
    public sealed class Limb {
        public string key;
        public Rigidbody2D rb;
        public Transform frame;
        public Collider2D shape;
        public HingeJoint2D joint;
        public float length, targetAngle,driveInertia;
        public DuelWeapon strikeProfile;
        public bool sourceEndpoints;
        public Vector2 sourceA,sourceB;
        public Vector2 preVelocity;
        public float preSpin;
        public float lastGroundContact=-10;
        public Transform Frame=>frame==null?rb.transform:frame;
        public float Rotation=>rb.rotation+(frame!=null&&frame!=rb.transform?frame.localEulerAngles.z:0);
        static Vector2 Rotate(Vector2 p,float angle) {float a=angle*Mathf.Deg2Rad,c=Mathf.Cos(a),s=Mathf.Sin(a);return new Vector2(c*p.x-s*p.y,s*p.x+c*p.y);}
        public static Vector2 BodyPoint(Rigidbody2D body,Vector2 local)=>body.position+Rotate(local,body.rotation);
        public static Vector2 BodyLocalPoint(Rigidbody2D body,Vector2 world)=>Rotate(world-body.position,-body.rotation);
        public Vector2 WorldPoint(Vector2 local) {
            if(frame!=null&&frame!=rb.transform)local=(Vector2)frame.localPosition+Rotate(local,frame.localEulerAngles.z);
            return BodyPoint(rb,local);
        }
        public Vector2 LocalPoint(Vector2 world) {
            Vector2 local=BodyLocalPoint(rb,world);
            return frame!=null&&frame!=rb.transform?Rotate(local-(Vector2)frame.localPosition,-frame.localEulerAngles.z):local;
        }
        public Vector2 A => sourceEndpoints?BodyPoint(rb,sourceA):WorldPoint(new Vector2(0,-length/2));
        public Vector2 B => sourceEndpoints?BodyPoint(rb,sourceB):WorldPoint(new Vector2(0,length/2));
        public Vector2 VelocityAt(Vector2 point) {
            Vector2 r=point-rb.worldCenterOfMass;
            return preVelocity+new Vector2(-r.y,r.x)*preSpin;
        }
        public void SampleVelocity() { preVelocity=rb.linearVelocity;preSpin=rb.angularVelocity*Mathf.Deg2Rad; }
        public void Drive(float target,float frequency,float torqueLimit,float strength=1,float speedScale=1) {
            targetAngle=target;
            if(strength<=0)return;
            if(joint!=null) {
                float servoError=Mathf.DeltaAngle(rb.rotation,target);
                float parentSpeed=joint.connectedBody==null?0:joint.connectedBody.angularVelocity;
                if(joint.useLimits) {
                    float desiredJoint=joint.CombatJointAngle()-servoError;
                    desiredJoint=Mathf.Clamp(desiredJoint,joint.limits.min+1,joint.limits.max-1);
                    servoError=joint.CombatJointAngle()-desiredJoint;
                }
                joint.useMotor=true;
                // Unity's positive 2D hinge speed rotates its owning body
                // clockwise. This sign is checked by DuelBuild.Calibrate.
                float speedLimit=360*Mathf.Clamp(speedScale,.15f,1.6f);
                float speed=Mathf.Clamp(parentSpeed-servoError*frequency*3.5f,-speedLimit,speedLimit);
                if(joint.useLimits) {
                    speed=Mathf.Clamp(speed,-Mathf.Max(0,joint.CombatJointAngle()-joint.limits.min-1)*.75f/Time.fixedDeltaTime,Mathf.Max(0,joint.limits.max-joint.CombatJointAngle()-1)*.75f/Time.fixedDeltaTime);
                }
                joint.motor=new JointMotor2D {motorSpeed=speed,maxMotorTorque=torqueLimit*strength};
                return;
            }
            float dt=Time.fixedDeltaTime,w=2*Mathf.PI*frequency,kp=w*w,kd=2*w;
            float error=Mathf.DeltaAngle(rb.rotation,target)*Mathf.Deg2Rad;
            float velocity=rb.angularVelocity*Mathf.Deg2Rad;
            float acceleration=(kp*(error-velocity*dt)-kd*velocity)/(1+kd*dt+kp*dt*dt);
            float torque=Mathf.Clamp(Mathf.Max(rb.inertia,driveInertia)*acceleration,-torqueLimit,torqueLimit)*strength;
            rb.CombatAddTorque(torque);
        }
    }

    public sealed class FootPlant {
        public enum Phase { Planted, Swing, Landing }
        public Vector2 anchor,from,target,goal;
        public Phase state=Phase.Landing;
        public bool stepping=>state!=Phase.Planted;
        public bool grounded;
        public TargetJoint2D support;
        public float phase,soleHeight,slip,maxSlip,maxLandingError,slipSquared,plantedSeconds;
        public int completedSteps,airborneSteps;
        public float maxStepSeconds;
        float plantedAt,lostGround;
        float stepStarted,airSeconds;
        bool activeStep;
        public FootPlant(Vector2 start,float sole,TargetJoint2D support) {anchor=from=target=goal=new Vector2(start.x,sole);soleHeight=sole;this.support=support;support.enabled=false;}
        public void Start(Vector2 next,Vector2 physical) {
            if(stepping)return;
            state=Phase.Swing;phase=0;from=physical;target=new Vector2(next.x,soleHeight);support.enabled=false;activeStep=true;stepStarted=PhysicsDuel.Instance.Clock;airSeconds=0;
        }
        public void Release(){support.enabled=false;}
        public void Update(float dt,Vector2 physical,float verticalSpeed,float force,float strength,float movementScale=1) {
            if(activeStep&&!grounded)airSeconds+=dt;
            if(strength<.52f&&state!=Phase.Swing){activeStep=false;state=Phase.Landing;target=goal=new Vector2(physical.x,soleHeight);support.enabled=false;return;}
            if(state==Phase.Swing) {
                phase=Mathf.Min(1,phase+dt*movementScale/.24f);
                float t=phase*phase*(3-2*phase);
                goal=Vector2.Lerp(from,target,t)+Vector2.up*(Mathf.Sin(phase*Mathf.PI)*.16f);
                if(phase>=1)state=Phase.Landing;
            }
            if(state==Phase.Landing) {
                goal=target;support.enabled=false;
                if(grounded&&verticalSpeed<.5f&&(!activeStep||Mathf.Abs(physical.x-target.x)<.18f)) {
                    maxLandingError=Mathf.Max(maxLandingError,Mathf.Abs(physical.x-target.x));
                    if(activeStep){completedSteps++;if(airSeconds>.025f)airborneSteps++;maxStepSeconds=Mathf.Max(maxStepSeconds,PhysicsDuel.Instance.Clock-stepStarted);activeStep=false;}
                    // Latch the actual touchdown, never a still-unreached pose goal.
                    anchor=goal=physical;state=Phase.Planted;plantedAt=PhysicsDuel.Instance.Clock;lostGround=0;
                    support.target=anchor+Vector2.down*.006f;
                }
            }
            if(state==Phase.Planted) {
                goal=anchor;lostGround=grounded?0:lostGround+dt;
                support.enabled=grounded&&strength>.52f;
                support.maxForce=force*strength;
                if(PhysicsDuel.Instance.Clock-plantedAt>.08f&&grounded) {
                    slip=Mathf.Abs(physical.x-anchor.x);maxSlip=Mathf.Max(maxSlip,slip);
                    slipSquared+=slip*slip*dt;plantedSeconds+=dt;
                }
                if(lostGround>.05f) {state=Phase.Landing;target=new Vector2(physical.x,soleHeight);support.enabled=false;}
            }
        }
    }

    public sealed class HitPacket {
        public DuelContact weapon,other;
        public Vector2 point,normal,relativeVelocity;
        public float speed,bladeFraction;
        public int step,strokeId,strokeLow,strokeHigh;
        public ulong directedPair,guardPair;
        public bool blocked => other.kind=="shield"||other.kind=="sword"||other.kind=="hilt";
    }
}

// Used by the real high-speed contact regression, independent of combat damage.
public sealed class DuelProbe : MonoBehaviour {
    public int contacts;
    void OnCollisionEnter2D(Collision2D c) {contacts++;}
}
