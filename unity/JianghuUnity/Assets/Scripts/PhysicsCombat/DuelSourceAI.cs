using System;
using System.Collections;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

// Source default route: CharacterEnemyAI, APK 5.0.3 ARM64.
// Decision state, parent animation and child nub commands are separate from
// the skeleton adapter in DuelAI.SourceAIWorldHandTarget.
public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
    public enum SourceAIStates {Idling,Attacking,Withdrawing,Defending,Cowering,GoingBerserk,EnjoyingTheVictory}
    enum SourcePersona {Bully,Matador}
    enum SourceStrikeHand {Left,Right,Double}
    public SourceAIStates SourceAIState {get;private set;}=SourceAIStates.Defending;
    public float SourceAIAggression=>aiAggression;
    public bool SourceAIAttacking=>aiAttacking;
    public float SourceAIOpponentActivity=>aiOpponentActivity;
    public float SourceAIReactionTime {get;private set;}=1;
    public Transform SourceAIPlayerWeaponCOM {get;private set;}
    public bool SourceAIProceduralStrikes {get;set;}
    public string SourceAILeftClip=>aiLeft==null?"":aiLeft.CurrentClip;
    public string SourceAIRightClip=>aiRight==null?"":aiRight.CurrentClip;
    public string SourceAIPersonality=>aiPersona.ToString();
    public int SourceAIWalkTaps {get;private set;}
    public int SourceAIKickSchedules {get;private set;}
    SourceHandRelay aiLeft,aiRight,aiDouble;
    Fighter sourceOpponent;
    Transform aiAimOrigin,aiNearestThreat,aiPlayerShieldCOM;
    bool sourceAIInitialized,aiRelentless=true,aiWeaponLeft,aiWeaponRight,aiShieldLeft,aiShieldRight,aiTwoHanded;
    bool aiAttacking,aiCanMove=true,aiKickSequence,aiStarted,aiStopped;
    bool aiStrikeLeft,aiStrikeRight,aiGuardLeft,aiGuardRight,aiGuardActiveLeft,aiGuardActiveRight,sourceLeftActive,sourceRightActive;
    readonly Vector2 aiAggressionRange=new Vector2(1,5),aiPersonaTime=new Vector2(2.5f,6),aiDodgeDuration=new Vector2(.25f,.5f),aiDoubleDelay=new Vector2(.05f,.15f),aiKickDelay=new Vector2(.05f,.15f),aiKickJitter=new Vector2(0,.15f);
    Vector2 aiPrevLeft,aiPrevRight,aiStrikeNubLeft,aiStrikeNubRight,aiGuardNubLeft,aiGuardNubRight,sourceNubLeft,sourceNubRight;
    float aiAttackRange=2,aiKickRange=1,aiWallX=3,aiNubMax=1,aiPreferredSpacing=1.2f,aiMinSpacing=.9f,aiBullyBias=.1f,aiRetreatBoost=3;
    float aiAimStrength=1,aiVariation=1,aiAngleOffset,aiMaxAim=16,aiShieldRadius=.2f,aiShieldStrength=.842f,aiShieldSpeed=10;
    float aiCrossGap=.25f,aiKickCooldown=1.2f,aiAggression,aiAggressionUnit,aiMinDistance,aiPreferredDistance;
    float aiThreatY,aiShieldY,aiOpponentActivity,aiIdleTime,aiSuppressLeft,aiSuppressRight,aiSourceDt,aiLastSourceFrame,aiCanMoveAt,aiInitializeAt;
    float aiLastWalkTime=-10,aiKickDeadline,aiThreatRefresh,aiShieldDeadline,aiDodgeDeadline,aiPersonalityDeadline;
    int aiLastWalkDirection,aiAttackDirection,aiCategoryLeft,aiCategoryRight,aiCategoryDouble;
    SourcePersona aiPersona=SourcePersona.Matador;
    float AIUnit=>Mathf.Clamp01(aiAggressionUnit);
    float AIDistance=>sourceOpponent==null?0:Mathf.Abs(AISignedDistance);
    float AISignedDistance=>mover.position.x-sourceOpponent.mover.position.x;
    float AIWallDistance=>Mathf.Abs(mover.position.x);
    bool AIValid=>sourceOpponent!=null&&mover!=null&&sourceOpponent.mover!=null;
    bool AIInBand=>aiMinDistance<=AIDistance&&AIDistance<=aiAttackRange;
    bool SourceAILeftArmDisabled=>ReferenceArmDisabled("Far");
    bool SourceAIRightArmDisabled=>ReferenceArmDisabled("Near");
    float AIRandom(Vector2 interval)=>AIRandom(interval.x,interval.y);
    float AIValue=>(float)aiRandom.NextDouble();
    float AIRange(float low,float high)=>AIRandom(low,high);
    int AIRange(int low,int high)=>aiRandom.Next(low,high);

    void InitializeSourceAI() {
        if(sourceAIInitialized)return;
        sourceAIInitialized=true;aiInitializeAt=game.Clock;aiLastSourceFrame=game.Clock;
        // Parent positions and child nubs from original enemy prefab. The
        // default enemy has useProceduralStrikes=0; optional route is explicit.
        aiLeft=new SourceHandRelay(this,false,new Vector2(.3230000138f,.5640000105f));
        aiRight=new SourceHandRelay(this,false,new Vector2(.0480000079f,.5640000105f));
        aiDouble=new SourceHandRelay(this,true,Vector2.zero);
        aiSourceParentNear=aiRight.parent;aiSourceParentFar=aiLeft.parent;
    }
    void SetSourceAIWeaponProperties() {
        // The game has one main item plus an optional shield. Retain source
        // slot flags rather than pretending a second weapon exists.
        aiTwoHanded=weapon.TwoHanded;aiWeaponRight=!aiTwoHanded;
        aiWeaponLeft=false;aiShieldLeft=HasShield&&!aiTwoHanded;aiShieldRight=false;
        aiCategoryLeft=8;aiCategoryRight=SourceItemCategory;aiCategoryDouble=SourceItemCategory;
        aiAttackRange=SourceAIWeaponLength+.1f;
        aiMinDistance=aiAttackRange*aiMinSpacing;aiPreferredDistance=aiAttackRange*aiPreferredSpacing;
        AIRefreshThreat();
    }
    int SourceItemCategory=>SourceItemType=="Dagger"?0:SourceItemType=="Sword"?1:SourceItemType=="Axe"?2:SourceItemType=="Mace"?3:SourceItemType=="Hammer"?4:SourceItemType=="Spear"?7:6;
    // Item length is an RPG catalog adapter until native items are mounted.
    float SourceAIWeaponLength=>Mathf.Max(0,weapon.distance);
    Vector2 SourceAIObservedNub(bool left) {
        if(sourceAIInitialized)return left?sourceNubLeft:sourceNubRight;
        bool held;return ReferenceStick(left?"Far":"Near",out held);
    }
    void ReadSourceAI(Fighter target,bool passive) {
        InitializeSourceAI();sourceOpponent=target;aiDriving=true;swordControl=false;brace=false;
        aiSourceDt=Mathf.Max(0,game.Clock-aiLastSourceFrame);aiLastSourceFrame=game.Clock;
        if(health<=0){aiMain=aiOff=Vector2.zero;move=0;return;}
        if(passive){aiMain=aiOff=Vector2.zero;move=0;shieldControl=false;return;}
        if(!aiStarted&&game.Clock>=aiInitializeAt+.5f)SetSourceAIWeaponProperties();
        if(!aiStarted&&game.Clock>=aiInitializeAt+1) {
            aiStarted=true;AIRollAggression();AIRun(AIStateLoop());AIRun(AIActionLoop());AIRun(AIReactiveLoop());
            AIRun(aiLeft.Defend());AIRun(aiRight.Defend());AIRun(aiDouble.Defend());
        }
        AIReadActivity();
        if(game.Clock>=aiThreatRefresh){aiThreatRefresh=game.Clock+1;AIRefreshThreat();}
        if(!aiCanMove&&game.Clock>=aiCanMoveAt)aiCanMove=true;
        StepSourceJobs();aiLeft.Update(aiSourceDt);aiRight.Update(aiSourceDt);aiDouble.Update(aiSourceDt);
        aiGuardLeft=aiGuardRight=aiGuardActiveLeft=aiGuardActiveRight=false;AISetShieldDefending();AITrackShield();
        bool suppressLeft=!aiStrikeLeft&&!aiGuardLeft&&game.Clock<aiSuppressLeft;
        bool suppressRight=!aiStrikeRight&&!aiGuardRight&&game.Clock<aiSuppressRight;
        if(suppressLeft)sourceNubLeft=Vector2.zero;else if(aiStrikeLeft)sourceNubLeft=aiStrikeNubLeft;else if(aiGuardLeft)sourceNubLeft=aiGuardNubLeft;
        if(suppressRight)sourceNubRight=Vector2.zero;else if(aiStrikeRight)sourceNubRight=aiStrikeNubRight;else if(aiGuardRight)sourceNubRight=aiGuardNubRight;
        sourceLeftActive=!suppressLeft&&(aiStrikeLeft||aiGuardActiveLeft||aiAttacking&&(aiWeaponLeft||aiTwoHanded));
        sourceRightActive=!suppressRight&&(aiStrikeRight||aiGuardActiveRight||aiAttacking&&(aiWeaponRight||aiTwoHanded));
        aiMain=sourceRightActive?Vector2.one:Vector2.zero;aiOff=sourceLeftActive?Vector2.one:Vector2.zero;
        shieldControl=sourceLeftActive;move=game.Clock<aiMoveUntil?aiMoveDirection:0;
        aiSourceParentNear=aiRight.parent;aiSourceParentFar=aiLeft.parent;
        aiSourceAggression=(int)aiAggression;
    }
    bool TrySourceAITarget(string side,out Vector2 parent,out Vector2 nub) {
        parent=Vector2.zero;nub=Vector2.zero;
        if(!sourceAIInitialized||!aiStarted||!aiDriving)return false;
        bool left=side=="Far";parent=left?aiLeft.parent:aiRight.parent;nub=left?sourceNubLeft:sourceNubRight;return true;
    }
    IEnumerator AIDelayedLeft(float delay){yield return new SourceWait(delay);SourceAIAttackLeft();}
    // cik 0x342bc14; cil 0x342bdcc; cim/cin 0x342be5c/0x342be98.
    void AIRollAggression(){aiAggression=Mathf.Clamp((int)AIRandom(aiAggressionRange),1,5);aiAggressionUnit=aiRelentless?(aiAggression-1)*.25f:0;}
    float AIReactionDelay()=>aiRelentless?AIRange(Mathf.Lerp(.45f,.05f,AIUnit),Mathf.Lerp(1.1f,.16f,AIUnit)):AIRange(SourceAIReactionTime/2.5f,SourceAIReactionTime*1.5f);
    float AIStaminaThreshold=>35-20*AIUnit;
    float AIAbortChance=>.35f-.34f*Mathf.Min(aiAggressionUnit*aiAggressionUnit,1);
    bool AIVulnerable=>sourceOpponent!=null&&(sourceOpponent.PassedOut||sourceOpponent.stamina<15||sourceOpponent.SourceAILeftArmDisabled||sourceOpponent.SourceAIRightArmDisabled);
    // cig 0x342a9bc, cip 0x342c010.
    void AIReadActivity() {
        Vector2 l=sourceOpponent.SourceAIObservedNub(true),r=sourceOpponent.SourceAIObservedNub(false);float den=Mathf.Max(aiNubMax,.0001f);
        float target=Mathf.Clamp01((l.magnitude+r.magnitude)/(den*2)*.6f+(Vector2.Distance(l,aiPrevLeft)+Vector2.Distance(r,aiPrevRight))/den*1.5f);
        aiPrevLeft=l;aiPrevRight=r;aiOpponentActivity=Mathf.Lerp(aiOpponentActivity,target,target>aiOpponentActivity?.7f:.25f);
    }
    void AIChoosePersonality() {
        if(game.Clock<aiPersonalityDeadline)return;
        float bias=aiBullyBias;if(aiOpponentActivity<.15f)bias+=.25f;if(aiOpponentActivity>.45f)bias-=.25f;bias+=.3f*AIUnit;
        aiPersona=AIValue<Mathf.Clamp01(bias)?SourcePersona.Bully:SourcePersona.Matador;
        aiPersonalityDeadline=game.Clock+AIRandom(aiPersonaTime);
    }
    // cih/cii 0x342acc0/0x342ba74: original mount order, horizontal
    // distance from character root, dropped items and shields excluded.
    void AIRefreshThreat() {
        aiNearestThreat=null;
        if(sourceOpponent==null)return;
        if(sourceOpponent.weapon.id!="unarmed"&&sourceOpponent.primaryGrip!=null&&sourceOpponent.primaryGrip.enabled)
            aiNearestThreat=sourceOpponent.sword.rb.transform;
        SourceAIPlayerWeaponCOM=aiNearestThreat;
        aiPlayerShieldCOM=sourceOpponent.HasShield&&sourceOpponent.shield!=null&&sourceOpponent.offhandGrip!=null&&sourceOpponent.offhandGrip.enabled?sourceOpponent.shield.rb.transform:null;
    }
    // cjc 0x342add0 and cjd 0x342af00.
    bool AIShieldStateBlocked=>SourceAIState==SourceAIStates.Attacking||SourceAIState==SourceAIStates.Cowering||SourceAIState==SourceAIStates.EnjoyingTheVictory;
    void AISetShieldDefending() {
        bool guard=game.Clock>=aiShieldDeadline&&aiNearestThreat!=null&&!AIShieldStateBlocked;
        if(aiShieldLeft)aiLeft.Defending=guard&&game.Clock>=aiSuppressLeft;
        if(aiShieldRight)aiRight.Defending=guard&&game.Clock>=aiSuppressRight;
    }
    void AITrackShield() {
        if(!aiShieldLeft&&!aiShieldRight)return;
        bool active=aiShieldStrength>0&&aiNearestThreat!=null&&game.Clock>=aiShieldDeadline&&!AIShieldStateBlocked&&game.Clock>=aiDodgeDeadline;
        bool left=aiShieldLeft&&!aiStrikeLeft&&game.Clock>=aiSuppressLeft,right=aiShieldRight&&!aiStrikeRight&&game.Clock>=aiSuppressRight;
        if(!left&&!right)return;
        Vector2 target=Vector2.zero;
        if(active) {
            Vector3 origin=aiAimOrigin!=null?aiAimOrigin.position:mover.position;
            float desired=Mathf.Clamp(DuelSourceAIStrikeCurves.AimDegrees(facing,origin,aiNearestThreat.position,aiAngleOffset),-aiMaxAim,aiMaxAim);
            float restAngle=Mathf.Atan2(DuelSourceAIStrikeCurves.Rest.y,DuelSourceAIStrikeCurves.Rest.x)*Mathf.Rad2Deg;
            float angle=Mathf.LerpAngle(restAngle,desired,Mathf.Clamp01(aiShieldStrength));
            target=new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad))*aiShieldRadius;
        }
        if(left){aiGuardLeft=true;aiGuardActiveLeft=active;aiGuardNubLeft=Vector2.Lerp(aiGuardNubLeft,target,aiShieldSpeed*aiSourceDt);}
        if(right){aiGuardRight=true;aiGuardActiveRight=active;aiGuardNubRight=Vector2.Lerp(aiGuardNubRight,target,aiShieldSpeed*aiSourceDt);}
    }
    // cje 0x342c484.
    void AISetWeaponDefending(bool defending) {
        if(aiShieldLeft||aiShieldRight)return;
        if(defending&&AIValue>.5f-.35f*AIUnit)return;
        if(aiWeaponLeft)aiLeft.Defending=defending;if(aiWeaponRight)aiRight.Defending=defending;
    }
    float AIExposure(float y) {
        float distance=float.MaxValue;
        if(aiNearestThreat!=null)distance=Mathf.Min(distance,Mathf.Abs(y-aiThreatY));
        if(aiPlayerShieldCOM!=null)distance=Mathf.Min(distance,Mathf.Abs(y-aiShieldY));
        return distance==float.MaxValue?1:distance;
    }
    // cjm/cjo 0x342ca4c/0x342ccec.
    int AIChooseDirection() {
        Transform target=sourceOpponent.referenceBodies["chestCOM"].transform;
        if(target==null)return AIRange(1,3);
        float high=AIExposure(target.position.y+.5f),low=AIExposure(target.position.y-.4f);
        int preferred=low<=high?1:2,other=high<low?1:2;
        return AIValue<.5f+.45f*AIUnit?preferred:other;
    }
    void AIRefreshAttackDirection() {
        if(aiNearestThreat!=null)aiThreatY=aiNearestThreat.position.y;
        else if(SourceAIPlayerWeaponCOM!=null)aiThreatY=SourceAIPlayerWeaponCOM.position.y;
        if(aiPlayerShieldCOM!=null)aiShieldY=aiPlayerShieldCOM.position.y;
        if(sourceOpponent.PassedOut)aiAttackDirection=1;
        else if(sourceOpponent.SourceAILeftArmDisabled||sourceOpponent.SourceAIRightArmDisabled) {
            SourceAIPlayerWeaponCOM=sourceOpponent.referenceBodies["chestCOM"].transform;aiPlayerShieldCOM=SourceAIPlayerWeaponCOM;
            aiAttackDirection=AIRange(1,3);
        } else aiAttackDirection=AIChooseDirection();
    }
    // cjr 0x342cfb0.
    Vector3 AIProceduralTarget() {
        Vector3 target=sourceOpponent!=null?(sourceOpponent.referenceBodies["chestCOM"].position):mover.position;
        float threatOffset=(aiNearestThreat!=null?aiThreatY:target.y)-target.y;
        float y=target.y-threatOffset+(aiAttackDirection==1?.5f:-.4f);
        if(aiNearestThreat!=null) {
            float delta=y-aiThreatY,weight=Mathf.Clamp01(1-Mathf.Abs(delta)/.7f);
            y+=Mathf.Sign(delta==0?-threatOffset:delta)*weight*.45f;
        }
        target.y=y;return target;
    }
    void AIRewind(){aiLeft.Rewind();aiRight.Rewind();aiDouble.Rewind();}
    void SourceAIAttackLeft(){aiLeft.Attack(aiAttackDirection,aiAggression);aiLeft.Defending=false;}
    void SourceAIEnableMove(){aiCanMove=true;}

    // cjk / cd.MoveNext 0x342d908. Realtime waits are intentional.
    IEnumerator AIWalk(int direction) {
        if(aiKickSequence)yield break;
        if(aiLastWalkDirection!=0&&aiLastWalkDirection!=direction&&game.Clock-aiLastWalkTime<aiCrossGap)yield break;
        aiLastWalkDirection=direction;aiLastWalkTime=game.Clock;float delay=AIRandom(aiDoubleDelay);
        if(direction<0)aiSuppressLeft=game.Clock+delay+.05f;else aiSuppressRight=game.Clock+delay+.05f;
        SourceAIMoveTap(direction<0);yield return new SourceWait(delay,true);SourceAIMoveTap(direction<0);
    }
    // ciw/cix, ca/cc: nested iterator preserves a coroutine yield boundary.
    IEnumerator AIToward(){yield return AIWalk(AISignedDistance>0?-1:1);}
    IEnumerator AIAway(){yield return AIWalk(AISignedDistance>0?1:-1);}
    // cjl / bz.MoveNext 0x342d4b8: exact four alternating UI taps.
    IEnumerator AIKick() {
        if(aiKickSequence||game.Clock<aiKickDeadline)yield break;
        aiKickSequence=true;SourceAIKickSchedules++;aiLastWalkDirection=0;aiKickDeadline=game.Clock+aiKickCooldown;
        SourceAIMoveTap(true);yield return new SourceWait(AIRandom(aiKickJitter),true);
        SourceAIMoveTap(false);yield return new SourceWait(AIRandom(aiKickDelay),true);
        SourceAIMoveTap(true);yield return new SourceWait(AIRandom(aiKickJitter),true);
        SourceAIMoveTap(false);yield return new SourceWait(aiCrossGap,true);
        aiLastWalkDirection=0;aiKickSequence=false;aiLastWalkTime=game.Clock;
    }
    // cjf/cjg 0x342c52c/0x342c7cc.
    void AIDecideMovement() {
        if(!AIValid)return;AIChoosePersonality();
        if(game.Clock>=aiKickDeadline&&AIWallDistance<aiWallX&&AIDistance<aiKickRange&&stamina>20) {
            int[] thresholds={95,90,80,70,40};int index=(int)aiAggression-1,threshold=index>=0&&index<5?thresholds[index]:50;
            if(AIRange(0,100)>threshold){AIRun(AIKick());AIRun(AIAway());}
        }
        if(SourceAIState==SourceAIStates.Attacking||SourceAIState==SourceAIStates.Withdrawing||SourceAIState==SourceAIStates.Cowering||!aiCanMove)return;
        aiCanMove=false;aiCanMoveAt=game.Clock+Mathf.Lerp(.3f,.1f,AIUnit);
        if(SourceAIState==SourceAIStates.Defending)AISetWeaponDefending(true);
        if(AIWallDistance>=aiWallX)SourceAIState=SourceAIStates.GoingBerserk;
        else if(game.Clock<aiDodgeDeadline||AIDistance<aiMinDistance)AIRun(AIAway());
        else {
            float desired=(aiPersona==SourcePersona.Bully?aiAttackRange*Mathf.Lerp(.75f,aiMinSpacing,AIUnit):aiPreferredDistance)*AIRange(.94f,1.08f);
            if(AIDistance>desired)AIRun(AIToward());
            else {
                float value=AIValue;
                if(aiPersona==SourcePersona.Bully) {
                    if(aiOpponentActivity<.2f&&value<.55f)AIRun(AIToward());else if(value>.85f)AIRun(AIAway());
                } else {
                    float probability=.2f+.3f*AIUnit;
                    if(value<probability)AIRun(AIToward());else if(value<probability+.3f)AIRun(AIAway());
                }
            }
        }
        if(stamina<10)AIRewind();
    }
    // ciy / cl.MoveNext 0x342fcb8.
    IEnumerator AIStateLoop() {
        float next=0;
        while(AIValid) {
            if(game.Clock<next){yield return null;continue;}
            next=game.Clock+AIReactionDelay();aiDecisions++;AIRollAggression();AIRefreshAttackDirection();
            if(sourceOpponent.health<=0)SourceAIState=SourceAIStates.EnjoyingTheVictory;
            else if(sourceOpponent.PassedOut)SourceAIState=SourceAIStates.GoingBerserk;
            else if(SourceAILeftArmDisabled&&SourceAIRightArmDisabled){SourceAIState=SourceAIStates.Cowering;yield break;}
            else if(AIDistance<aiAttackRange)SourceAIState=AIVulnerable&&aiAggressionUnit>.5f?SourceAIStates.GoingBerserk:SourceAIStates.Attacking;
            else if(!aiAttacking)SourceAIState=SourceAIStates.Defending;
            AIDecideMovement();yield return null;
        }
    }
    // ciz / ce.MoveNext 0x342dad4.
    IEnumerator AIActionLoop() {
        while(AIValid) {
            switch(SourceAIState) {
                case SourceAIStates.Attacking:AIRun(AIAttack());break;
                case SourceAIStates.Cowering:
                    for(int i=0;i<5&&AIValid;i++){AIRun(AIAway());yield return new SourceWait(AIRange(.25f,.75f));}
                    break;
                case SourceAIStates.GoingBerserk:
                    int count=Mathf.RoundToInt(3+AIUnit*3);
                    for(int i=0;i<count&&AIValid;i++){AIRun(AIToward());yield return new SourceWait(Mathf.Lerp(.45f,.15f,AIUnit));}
                    if(AIValid)AIRun(AIAttack());break;
                case SourceAIStates.EnjoyingTheVictory:
                    SourceStopJobs();AIRun(AIVictory());yield break;
            }
            float delay=SourceAIState==SourceAIStates.GoingBerserk?AIRange(aiAggression*.125f,aiAggression/3):AIReactionDelay();
            float deadline=game.Clock+delay;while(game.Clock<deadline&&AIValid)yield return null;
        }
    }
    // cja / cj.MoveNext 0x342f458.
    IEnumerator AIReactiveLoop() {
        float previous=0,next=0;
        while(AIValid) {
            if(game.Clock<next){yield return null;continue;}
            float step=aiRelentless?Mathf.Lerp(.09f,.03f,AIUnit):.12f;next=game.Clock+step;
            if(SourceAIState!=SourceAIStates.Cowering&&SourceAIState!=SourceAIStates.EnjoyingTheVictory) {
                float activity=aiOpponentActivity,distance=AIDistance,threshold=AIStaminaThreshold,high=Mathf.Lerp(.65f,.32f,AIUnit);
                if(activity>high&&previous<=high&&!aiAttacking) {
                    if(aiPersona==SourcePersona.Matador&&AIValue<Mathf.Lerp(.7f,.3f,AIUnit)) {
                        aiDodgeDeadline=game.Clock+AIRandom(aiDodgeDuration);AISetWeaponDefending(true);AIRun(AIAway());AIRun(AIDodgeCounter());
                    } else if(distance<aiAttackRange&&stamina>threshold&&AIInBand&&AIValue<Mathf.Lerp(.4f,.95f,AIUnit)) {
                        SourceAIState=SourceAIStates.Attacking;AIRun(AIAttack());
                    } else AISetWeaponDefending(true);
                } else if(activity<high*.4f&&previous>=high*.4f&&stamina>threshold&&!aiAttacking) {
                    aiDodgeDeadline=0;
                    if(AIInBand){SourceAIState=SourceAIStates.Attacking;AIRun(AIAttack());}
                    else if(AIDistance>aiAttackRange){AIRun(AIToward());SourceAIState=SourceAIStates.Attacking;}
                } else if(activity<.1f&&distance<aiAttackRange&&!aiAttacking&&stamina>20) {
                    aiIdleTime+=step;
                    if(aiIdleTime>1.4f-AIUnit) {
                        aiIdleTime=0;
                        if(AIWallDistance<aiWallX&&AIValue<.3f+.5f*AIUnit)AIRun(AIKick());
                        else {SourceAIState=SourceAIStates.Attacking;AIRun(AIAttack());}
                    }
                } else aiIdleTime=0;
                distance=AIDistance;
                if(!aiAttacking&&distance>=aiMinDistance&&distance<=aiAttackRange*1.25f&&stamina>threshold&&game.Clock>=aiDodgeDeadline&&SourceAIState!=SourceAIStates.Cowering&&SourceAIState!=SourceAIStates.EnjoyingTheVictory) {
                    float chance=(.03f+.72f*Mathf.Min(aiAggressionUnit*aiAggressionUnit,1))*(aiPersona==SourcePersona.Bully?1.3f:1)*(AIVulnerable?1.5f:1);
                    if(AIValue<chance){if(distance>aiAttackRange)AIRun(AIToward());SourceAIState=SourceAIStates.Attacking;AIRun(AIAttack());}
                }
                previous=activity;
            }
            yield return null;
        }
    }
    // cjb / cg.MoveNext 0x342ea20.
    IEnumerator AIDodgeCounter() {
        yield return new SourceWait(AIRandom(aiDodgeDuration));
        if(!AIValid||aiAttacking||AIValue>.35f+.6f*AIUnit)yield break;
        aiDodgeDeadline=0;
        if(AIDistance>aiAttackRange){AIRun(AIToward());yield return new SourceWait(Mathf.Lerp(.25f,.08f,AIUnit));}
        if(AIValid&&!aiAttacking&&stamina>AIStaminaThreshold){SourceAIState=SourceAIStates.Attacking;AIRun(AIAttack());}
    }
    // cjp / cf.MoveNext 0x342df28. Early-abort and follow-up re-entry retain
    // the original asynchronous scheduling and random draw order.
    IEnumerator AIAttack() {
        if(aiAttacking)yield break;aiAttacking=true;
        if(!AIValid){aiAttacking=false;yield break;}
        bool execute=stamina>=AIStaminaThreshold&&AIValue>=AIAbortChance;
        if(execute) {
            if(!sourceOpponent.PassedOut&&AIWallDistance<aiWallX&&AIDistance<aiMinDistance) {
                aiAttacking=false;AIRun(AIAway());
                if(game.Clock>=aiKickDeadline&&stamina>25&&AIValue<.25f-.2f*AIUnit)AIRun(AIKick());
                yield break;
            }
            aiDodgeDeadline=0;
            if(SourceAILeftArmDisabled||SourceAIRightArmDisabled){aiWeaponLeft=aiWeaponRight=true;aiShieldLeft=aiShieldRight=false;}
            AIRefreshAttackDirection();
            if(AIValue<.5f*AIUnit){yield return new SourceWait(AIRange(.06f,.14f));aiAttackDirection=aiAttackDirection==1?2:1;}
            if(SourceAIProceduralStrikes) {
                if(aiTwoHanded)yield return AIRun(AIProceduralStrike(SourceStrikeHand.Double));
                else if(aiWeaponLeft&&aiWeaponRight) {
                    float duration=.83f*(1-.25f*AIUnit);AIRun(AIProceduralStrike(SourceStrikeHand.Right));
                    yield return new SourceWait(duration*Mathf.Lerp(.85f,.45f,AIUnit));
                    if(AIValid)yield return AIRun(AIProceduralStrike(SourceStrikeHand.Left));
                } else if(aiWeaponLeft)yield return AIRun(AIProceduralStrike(SourceStrikeHand.Left));
                else if(aiWeaponRight)yield return AIRun(AIProceduralStrike(SourceStrikeHand.Right));
                else yield return new SourceWait(.95f);
            } else {
                float duration=aiTwoHanded?1.25f:.95f,leftDelay=0;
                if(aiWeaponLeft) {
                    if(AIValue<.45f+.5f*AIUnit){leftDelay=duration*Mathf.Lerp(.85f,.45f,AIUnit);AIRun(AIDelayedLeft(leftDelay));}
                    else aiLeft.Defending=true;
                }
                if(aiWeaponRight){aiRight.Attack(aiAttackDirection,aiAggression);aiRight.Defending=false;}
                if(aiTwoHanded){aiDouble.Attack(aiAttackDirection,aiAggression);aiDouble.Defending=false;aiLeft.enabled=aiRight.enabled=false;}
                yield return new SourceWait(duration+leftDelay);
            }
        }
        if(!AIValid){aiAttacking=false;yield break;}
        AIRewind();
        float repeatChance=(aiPersona==SourcePersona.Matador?.2f+.55f*AIUnit:.4f+.55f*AIUnit)/Mathf.Max(.01f,aiRetreatBoost);
        if(AIValue<repeatChance&&AIDistance<aiAttackRange+.5f&&stamina>AIStaminaThreshold) {
            yield return new SourceWait(AIRange(.85f,1.15f)*Mathf.Lerp(.3f,.06f,AIUnit));
            aiAttacking=false;if(AIValid){SourceAIState=SourceAIStates.Attacking;AIRun(AIAttack());}yield break;
        }
        aiAttacking=false;SourceAIState=SourceAIStates.Defending;
        if(AIDistance<aiMinDistance)AIRun(AIAway());
        if(AIValue<AIAbortChance)AIRun(AIPullBackward());
    }
    // cjs / ch.MoveNext 0x342eccc, cv 0x35142c8..0x3516050.
    IEnumerator AIProceduralStrike(SourceStrikeHand hand) {
        bool both=hand==SourceStrikeHand.Double;int category=both?aiCategoryDouble:hand==SourceStrikeHand.Left?aiCategoryLeft:aiCategoryRight;
        float stab=category==0?.55f:category==1?.35f:category==7?.9f:0;
        var kind=AIValue<stab?DuelSourceAIStrikeCurves.Kind.Stab:aiAttackDirection==1?DuelSourceAIStrikeCurves.Kind.SwingUp:DuelSourceAIStrikeCurves.Kind.SwingDown;
        var profile=DuelSourceAIStrikeCurves.Profile.Default(kind,both).Varied(aiVariation,aiRandom);profile.duration*=1-.25f*AIUnit;
        bool l=both||hand==SourceStrikeHand.Left,r=both||hand==SourceStrikeHand.Right;
        if(l){aiStrikeLeft=true;sourceLeftActive=true;}if(r){aiStrikeRight=true;sourceRightActive=true;}
        float elapsed=0;
        while(elapsed<profile.duration&&AIValid) {
            float aim=0;
            if(aiAimStrength>0)aim=Mathf.Clamp(DuelSourceAIStrikeCurves.AimDegrees(facing,aiAimOrigin!=null?aiAimOrigin.position:mover.position,AIProceduralTarget(),aiAngleOffset)*aiAimStrength,-aiMaxAim,aiMaxAim)+aiAimBias;
            if(both)DuelSourceAIStrikeCurves.Evaluate2H(profile,elapsed/profile.duration,aim,out aiStrikeNubLeft,out aiStrikeNubRight);
            else if(l)aiStrikeNubLeft=DuelSourceAIStrikeCurves.Evaluate(profile,elapsed/profile.duration,DuelSourceAIStrikeCurves.Rest,aim);
            else aiStrikeNubRight=DuelSourceAIStrikeCurves.Evaluate(profile,elapsed/profile.duration,DuelSourceAIStrikeCurves.Rest,aim);
            elapsed+=aiSourceDt;yield return null;
        }
        if(l){aiStrikeLeft=false;aiStrikeNubLeft=DuelSourceAIStrikeCurves.Rest;}if(r){aiStrikeRight=false;aiStrikeNubRight=DuelSourceAIStrikeCurves.Rest;}
    }
    // cjt / ci.MoveNext 0x342f278.
    IEnumerator AIPullBackward() {
        AIRewind();SourceAIState=AIWallDistance<=aiWallX?SourceAIStates.Defending:SourceAIStates.GoingBerserk;
        yield return new SourceWait(AIRange(.8f,1.2f)*(1-.75f*AIUnit));
        if(AIValid) {
            if(AIWallDistance<=aiWallX)AIRun(AIAway());else AIRun(AIWalk(mover.position.x<=0?1:-1));
            aiAttacking=false;
        }
    }
    // cju / ck.MoveNext 0x342fa5c.
    IEnumerator AIVictory() {
        aiStrikeLeft=aiStrikeRight=aiGuardLeft=aiGuardRight=false;aiSuppressLeft=aiSuppressRight=0;
        aiStrikeNubLeft=aiStrikeNubRight=aiGuardNubLeft=aiGuardNubRight=DuelSourceAIStrikeCurves.Rest;
        SourceAIReactionTime=AIRange(.15f,.35f);yield return new SourceWait(AIRange(.5f,1f));
        AIRun(AIWalk(1));foreach(var relay in new[]{aiLeft,aiRight,aiDouble}){relay.Stop();relay.Rewind();}
        aiLeft.Defending=aiRight.Defending=false;
    }
    }
}
