using UnityEngine;

// Each button picks up its remembered hand target without a jump. A shared
// cursor delta can drive either hand or both, without two floating sticks.
public sealed class DuelMouseHands {
    public sealed class Target {
        public bool hasTarget;
        public Vector2 value;
        public float angularSpeed,radialSpeed,travel,angleOffset;
        public float Axis=>Mathf.Atan2(value.y,value.x)*Mathf.Rad2Deg+angleOffset;
        public bool Coasting=>Mathf.Abs(angularSpeed)>1||Mathf.Abs(radialSpeed)>.01f;
        public void Stop(){angularSpeed=radialSpeed=0;}
    }
    public readonly Target main=new Target(),off=new Target();
    int heldHands;
    public int ActiveHand=>heldHands==3?2:heldHands==1?0:heldHands==2?1:-1;
    public bool IsControlled(bool primary)=>(heldHands&(primary?1:2))!=0;
    Vector2 cursor;
    float sampledAt;
    public const float MinimumReach=.44f;
    public const float AngularDrag=14,RadialDrag=10;
    public const float ReleasedAngularSpeed=240;
    public Target Hand(bool primary)=>primary?main:off;
    public static Vector2 Reach(Vector2 value){
        float length=value.magnitude;
        return (length>.0001f?value/length:Vector2.right)*Mathf.Clamp(length,MinimumReach,1);
    }
    public void Seed(bool primary,Vector2 value,float axis=float.NaN){var hand=Hand(primary);if(hand.hasTarget)return;hand.value=Reach(value);hand.angleOffset=float.IsNaN(axis)?0:Mathf.DeltaAngle(Mathf.Atan2(hand.value.y,hand.value.x)*Mathf.Rad2Deg,axis);hand.hasTarget=true;hand.Stop();}
    public void Press(bool primary,Vector2 point,Vector2 mainSeed,Vector2 offSeed,float at,float mainAxis=float.NaN,float offAxis=float.NaN){
        Seed(true,mainSeed,mainAxis);Seed(false,offSeed,offAxis);
        heldHands|=primary?1:2;cursor=point;sampledAt=at;
        Hand(primary).Stop();
    }
    public void Move(Vector2 point,float mainPixels,float offPixels,float at){
        if(heldHands==0)return;
        Vector2 movement=point-cursor;cursor=point;
        float dt=Mathf.Max(.001f,at-sampledAt);sampledAt=at;
        if(IsControlled(true))MoveHand(main,movement,mainPixels,dt);
        if(IsControlled(false))MoveHand(off,movement,offPixels,dt);
    }
    static void MoveHand(Target hand,Vector2 movement,float radius,float dt){
        if(movement.sqrMagnitude<.0001f){
            // Velocity estimation does not delay the controlled target. A
            // deliberate stop before release must not resurrect an old cut.
            hand.angularSpeed*=Mathf.Exp(-dt*24);hand.radialSpeed*=Mathf.Exp(-dt*24);return;
        }
        Vector2 previous=hand.value;
        float pixels=Mathf.Max(20,radius);
        hand.value=Reach(previous+new Vector2(movement.x,-movement.y)/pixels);
        float turn=Mathf.DeltaAngle(Mathf.Atan2(previous.y,previous.x)*Mathf.Rad2Deg,Mathf.Atan2(hand.value.y,hand.value.x)*Mathf.Rad2Deg);
        // Polar speed keeps an outward thrust straight and a circular cut
        // circular. Only the estimate is filtered, never pointer position.
        float blend=1-Mathf.Exp(-dt/.045f);
        hand.angularSpeed=Mathf.Lerp(hand.angularSpeed,Mathf.Clamp(turn/dt,-900,900),blend);
        hand.radialSpeed=Mathf.Lerp(hand.radialSpeed,Mathf.Clamp((hand.value.magnitude-previous.magnitude)/dt,-5,5),blend);
    }
    public void Release(bool primary,float at){
        if(!IsControlled(primary))return;
        if(at-sampledAt>.085f)Hand(primary).Stop();
        Hand(primary).angularSpeed=Mathf.Clamp(Hand(primary).angularSpeed,-ReleasedAngularSpeed,ReleasedAngularSpeed);
        Hand(primary).radialSpeed=Mathf.Clamp(Hand(primary).radialSpeed,-1.8f,1.8f);
        heldHands&=~(primary?1:2);
    }
    public void Advance(float dt,float mainLoad=1,float offLoad=1){
        if(!IsControlled(true))Coast(main,dt,mainLoad);
        if(!IsControlled(false))Coast(off,dt,offLoad);
    }
    static void Coast(Target hand,float dt,float load){
        if(!hand.hasTarget||dt<=0)return;
        float drag=AngularDrag*Mathf.Clamp(load,.7f,4);
        float decay=Mathf.Exp(-drag*dt);
        float turn=hand.angularSpeed*(1-decay)/drag;
        float angle=Mathf.Atan2(hand.value.y,hand.value.x)*Mathf.Rad2Deg+turn;
        hand.travel+=Mathf.Abs(turn);
        hand.angularSpeed=Mathf.MoveTowards(hand.angularSpeed*decay,0,600*dt);
        float radialDecay=Mathf.Exp(-RadialDrag*dt);
        float reach=hand.value.magnitude+hand.radialSpeed*(1-radialDecay)/RadialDrag;
        hand.radialSpeed=Mathf.MoveTowards(hand.radialSpeed*radialDecay,0,.6f*dt);
        if(reach>=1&&hand.radialSpeed>0||reach<=MinimumReach&&hand.radialSpeed<0)hand.radialSpeed=0;
        hand.value=new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad))*Mathf.Clamp(reach,MinimumReach,1);
    }
    public void Brake(){main.Stop();off.Stop();}
    public void Freeze(){heldHands=0;Brake();}
    public void Forget(bool primary){Hand(primary).hasTarget=false;Hand(primary).Stop();heldHands&=~(primary?1:2);}
    public void Clear(){Freeze();main.hasTarget=off.hasTarget=false;main.travel=off.travel=0;}
}
