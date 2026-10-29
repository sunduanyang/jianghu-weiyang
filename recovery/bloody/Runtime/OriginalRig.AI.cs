using System;
using System.Collections;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;
using Random = UnityEngine.Random;

// Source: CharacterEnemyAI and its bz..cl iterator state machines in the
// supplied 5.0.3 APK. Addresses identify native evidence, not an estimated AI.
public sealed partial class OriginalRig {
    public enum OriginalAIStates {Idling,Attacking,Withdrawing,Defending,Cowering,GoingBerserk,EnjoyingTheVictory}
    enum OriginalPersona {Bully,Matador}
    enum OriginalStrikeHand {Left,Right,Double}
    public OriginalAIStates OriginalAIState {get;private set;}
    public float OriginalAIAggression=>aiAggression;
    public bool OriginalAIAttacking=>aiAttacking;
    public float OriginalAIOpponentActivity=>aiOpponentActivity;
    public float OriginalAIReactionTime {get;private set;}
    public Transform OriginalAIPlayerWeaponCOM {get;private set;}
    public bool OriginalAIProceduralStrikes {get;set;}
    public string OriginalAILeftClip=>aiLeft==null?"":aiLeft.CurrentClip;
    public string OriginalAIRightClip=>aiRight==null?"":aiRight.CurrentClip;
    // CharacterExpressions/VoiceSFX is a distinct original subsystem. Its
    // subscriber must preserve its own authority, cooldown and random draws.
    public event Action<bool> OriginalAIChargeRequested;
    OriginalAIExecution aiExecution;
    OriginalHandAnimations aiLeft,aiRight,aiDouble;
    J aiSource;
    Transform aiNubLeft,aiNubRight,aiAimOrigin,aiNearestThreat,aiPlayerShieldCOM;
    bool aiRelentless,aiWeaponLeft,aiWeaponRight,aiShieldLeft,aiShieldRight,aiTwoHanded;
    bool aiAttacking,aiCanMove=true,aiKickSequence,aiStarted,aiStopped,aiUpdatesDisabled;
    bool aiStrikeLeft,aiStrikeRight,aiGuardLeft,aiGuardRight,aiGuardActiveLeft,aiGuardActiveRight;
    Vector2 aiAggressionRange,aiPersonaTime,aiDodgeDuration,aiDoubleDelay,aiKickDelay,aiKickJitter;
    Vector2 aiPrevLeft,aiPrevRight,aiStrikeNubLeft,aiStrikeNubRight,aiGuardNubLeft,aiGuardNubRight;
    float aiAttackRange,aiKickRange,aiWallX,aiNubMax,aiPreferredSpacing,aiMinSpacing,aiBullyBias,aiRetreatBoost;
    float aiAimStrength,aiVariation,aiAngleOffset,aiAimBias,aiMaxAim,aiShieldRadius,aiShieldStrength,aiShieldSpeed;
    float aiCrossGap,aiKickCooldown,aiAggression,aiAggressionUnit,aiMinDistance,aiPreferredDistance;
    float aiThreatY,aiShieldY,aiOpponentActivity,aiIdleTime,aiSuppressLeft,aiSuppressRight;
    float aiLastWalkTime=-10,aiKickDeadline,aiThreatRefresh,aiShieldDeadline=0,aiDodgeDeadline,aiPersonalityDeadline;
    int aiLastWalkDirection,aiAttackDirection,aiCategoryLeft,aiCategoryRight,aiCategoryDouble;
    OriginalPersona aiPersona=OriginalPersona.Matador;
    float AIUnit=>Mathf.Clamp01(aiAggressionUnit);
    float AIDistance=>opponent==null?0:Mathf.Abs(AISignedDistance);
    float AISignedDistance=>transform.position.x-opponent.transform.position.x;
    float AIWallDistance=>Mathf.Abs(transform.position.x);
    bool AIValid=>opponent!=null&&rootBody!=null&&opponent.rootBody!=null;
    bool AIInBand=>aiMinDistance<=AIDistance&&AIDistance<=aiAttackRange;
    float AINumber(string key)=>(float)aiSource[key].N;
    static Vector2 AIVector(J value)=>new Vector2((float)value["x"].N,(float)value["y"].N);
    static float AIRandom(Vector2 interval)=>Random.Range(interval.x,interval.y);
    Coroutine AIRun(IEnumerator routine)=>aiExecution==null||aiStopped?null:aiExecution.StartCoroutine(routine);

    partial void InitializeAI() {
        InitializeStartup();
        InitializeExpressions();
        if(human)return;
        aiSource=source["controllers"]["CharacterEnemyAI"][0]["data"];
        if(!aiSource.Has)return;
        aiAggressionRange=new Vector2((float)aiSource["Aggression"]["min"].N,(float)aiSource["Aggression"]["max"].N);
        OriginalAIReactionTime=AINumber("ReactionTime");aiAttackRange=AINumber("attackRange");aiKickRange=AINumber("kickRange");aiWallX=AINumber("wallX");
        aiRelentless=aiSource["relentless"].B;aiNubMax=AINumber("nubMaxDeflection");aiPreferredSpacing=AINumber("preferredSpacing");aiMinSpacing=AINumber("minSpacing");
        aiPersonaTime=AIVector(aiSource["personalitySwitchTime"]);aiBullyBias=AINumber("bullyBias");aiDodgeDuration=AIVector(aiSource["dodgeBackDuration"]);aiRetreatBoost=AINumber("postAttackRetreatBoost");
        OriginalAIProceduralStrikes=aiSource["useProceduralStrikes"].B;aiAimStrength=AINumber("aimStrength");aiVariation=AINumber("strikeVariation");aiAngleOffset=AINumber("nubAngleOffsetDeg");
        aiAimBias=AINumber("aimBiasDeg");aiMaxAim=AINumber("maxAimDeg");aiShieldRadius=AINumber("shieldGuardRadius");aiShieldStrength=AINumber("shieldTrackStrength");aiShieldSpeed=AINumber("shieldTrackSpeed");
        aiDoubleDelay=AIVector(aiSource["aiDoubleTapDelay"]);aiKickDelay=AIVector(aiSource["aiKickPairDelay"]);aiKickJitter=AIVector(aiSource["aiKickSideJitter"]);aiCrossGap=AINumber("minCrossSideWalkGap");aiKickCooldown=AINumber("kickCooldown");
        OriginalAIState=(OriginalAIStates)aiSource["AIState"].I;
        aiNubLeft=ResolveTransform(aiSource["nubLeft"]);aiNubRight=ResolveTransform(aiSource["nubRight"]);aiAimOrigin=ResolveTransform(aiSource["aimOrigin"]);
        var clips=OriginalHandAnimations.LoadOriginalClips();
        foreach(var animation in GetComponentsInChildren<Animation>(true))foreach(AnimationState state in animation)if(state.clip!=null)clips[state.clip.name]=state.clip;
        foreach(var entry in source["controllers"]["AIHandsInputRelayer"].A) {
            var data=entry["data"];var target=ResolveTransform(data["m_GameObject"]);
            var relay=target.gameObject.AddComponent<OriginalHandAnimations>();relay.Initialize(this,data,clips);
            if(entry["id"].S==aiSource["InputRelayer_SingleLeft"]["id"].S)aiLeft=relay;
            if(entry["id"].S==aiSource["InputRelayer_SingleRight"]["id"].S)aiRight=relay;
            if(entry["id"].S==aiSource["InputRelayer_Double"]["id"].S)aiDouble=relay;
        }
        aiExecution=gameObject.AddComponent<OriginalAIExecution>();
        aiExecution.relays=new[]{aiLeft,aiRight,aiDouble};
        // Start 0x342a154 schedules equipment properties at .5 and loops at 1.
        Invoke(nameof(SetOriginalAIWeaponProperties),.5f);Invoke(nameof(StartOriginalAICoroutines),1f);
    }

    // SetAIWeaponAndShieldProperties 0x342b488. Flags reflect which original
    // mounts contain items. Equipment and original bind points are supplied by
    // the independently recovered config applier, never synthesized here.
    public void SetOriginalAIWeaponProperties() {
        if(human||aiSource==null||opponent==null)return;
        bool singleLeft=leftItem!=null&&!leftItem.twoHanded,singleRight=rightItem!=null&&!rightItem.twoHanded;
        aiWeaponLeft=singleLeft&&leftItem.OriginalEquipmentType==6;
        aiWeaponRight=singleRight&&rightItem.OriginalEquipmentType==6;
        // Source flags inspect the populated weapon/shield mount. A Type7
        // None still sets its category0 bundle, but Name="Nothing" suppresses
        // the shield flag (0x342b488, native getter eb 0x35930bc).
        aiShieldLeft=singleLeft&&leftItem.OriginalUsesShieldMount&&leftItem.config["Name"].S!="Nothing"&&!leftItem.dropped;
        aiShieldRight=singleRight&&rightItem.OriginalUsesShieldMount&&rightItem.config["Name"].S!="Nothing"&&!rightItem.dropped;
        aiTwoHanded=doubleHanded;
        float length=0;
        if(singleLeft) {
            if(aiWeaponLeft)aiCategoryLeft=leftItem.category;
            aiLeft.SetBundle(false,leftItem.category);
            if(aiWeaponLeft)length=Mathf.Max(length,leftItem.length);
        }
        if(singleRight) {
            if(aiWeaponRight)aiCategoryRight=rightItem.category;
            aiRight.SetBundle(false,rightItem.category);
            if(aiWeaponRight)length=Mathf.Max(length,rightItem.length);
        }
        if(aiTwoHanded) {
            var item=leftItem!=null&&leftItem.twoHanded?leftItem:rightItem;
            if(item!=null){aiCategoryDouble=item.category;aiDouble.SetBundle(true,item.category);length=Mathf.Max(length,item.length);}
        }
        if(length>0)aiAttackRange=length+.1f;
        aiMinDistance=aiAttackRange*aiMinSpacing;aiPreferredDistance=aiAttackRange*aiPreferredSpacing;
        // Native0x342ba08..0x342ba30 copies applier caches as stored after
        // its ordered ApplyLeft/Right or Apply2H, including Chest fallback.
        OriginalAIPlayerWeaponCOM=opponent.OriginalEquipmentWeaponCOM;
        aiPlayerShieldCOM=opponent.OriginalEquipmentShieldCOM;
        AIRefreshThreat();
    }
    void StartOriginalAICoroutines() {
        if(aiStopped||aiStarted)return;
        if(!AIValid){Invoke(nameof(StartOriginalAICoroutines),.1f);return;}
        aiStarted=true;AIRollAggression();AIRun(AIStateLoop());AIRun(AIActionLoop());AIRun(AIReactiveLoop());
    }
    public void StartOriginalPullBackward(){if(!human)AIRun(AIPullBackward());}
    // CharacterVitality.cmu disables the AI Behaviour. Unity keeps its
    // coroutines and Invoke calls alive when enabled=false; retain their
    // original scheduling/random side effects until object disposal.
    public void DisableOriginalAIUpdates(){if(!human)aiUpdatesDisabled=true;}
    public void StopOriginalAI() {
        if(human)return;aiStopped=true;
        CancelInvoke(nameof(SetOriginalAIWeaponProperties));CancelInvoke(nameof(StartOriginalAICoroutines));CancelInvoke(nameof(OriginalAIEnableMove));CancelInvoke(nameof(OriginalAIAttackLeft));
        if(aiExecution!=null)aiExecution.StopAllCoroutines();
        foreach(var relay in new[]{aiLeft,aiRight,aiDouble})if(relay!=null){relay.StopAllCoroutines();relay.CancelInvoke();relay.Stop();relay.enabled=false;}
        aiStrikeLeft=aiStrikeRight=false;aiGuardLeft=aiGuardRight=aiGuardActiveLeft=aiGuardActiveRight=false;aiAttacking=false;leftActive=rightActive=false;
    }
    partial void FixedAI() { }
    // Update 0x342a4ac: parent legacy animation + child nub values remain
    // separate. Do not clamp these AI vectors as a virtual joystick input.
    partial void UpdateAI() {
        if(human||aiSource==null||aiStopped||aiUpdatesDisabled||!AIValid)return;
        AIReadActivity();
        if(Time.time>=aiThreatRefresh){aiThreatRefresh=Time.time+1;AIRefreshThreat();}
        aiGuardLeft=aiGuardRight=aiGuardActiveLeft=aiGuardActiveRight=false;
        AISetShieldDefending();AITrackShield();
        bool suppressLeft=!aiStrikeLeft&&!aiGuardLeft&&Time.time<aiSuppressLeft;
        bool suppressRight=!aiStrikeRight&&!aiGuardRight&&Time.time<aiSuppressRight;
        if(suppressLeft)aiNubLeft.localPosition=Vector3.zero;
        else if(aiStrikeLeft)aiNubLeft.localPosition=aiStrikeNubLeft;
        else if(aiGuardLeft)aiNubLeft.localPosition=aiGuardNubLeft;
        if(suppressRight)aiNubRight.localPosition=Vector3.zero;
        else if(aiStrikeRight)aiNubRight.localPosition=aiStrikeNubRight;
        else if(aiGuardRight)aiNubRight.localPosition=aiGuardNubRight;
        leftInput=aiNubLeft.localPosition;rightInput=aiNubRight.localPosition;
        leftActive=!suppressLeft&&(aiStrikeLeft||aiGuardActiveLeft||aiAttacking&&(aiWeaponLeft||aiTwoHanded));
        rightActive=!suppressRight&&(aiStrikeRight||aiGuardActiveRight||aiAttacking&&(aiWeaponRight||aiTwoHanded));
    }

    // cik 0x342bc14; cil 0x342bdcc; cim/cin 0x342be5c/0x342be98.
    void AIRollAggression(){aiAggression=Mathf.Clamp((int)AIRandom(aiAggressionRange),1,5);aiAggressionUnit=aiRelentless?(aiAggression-1)*.25f:0;}
    float AIReactionDelay()=>aiRelentless?Random.Range(Mathf.Lerp(.45f,.05f,AIUnit),Mathf.Lerp(1.1f,.16f,AIUnit)):Random.Range(OriginalAIReactionTime/2.5f,OriginalAIReactionTime*1.5f);
    float AIStaminaThreshold=>35-20*AIUnit;
    float AIAbortChance=>.35f-.34f*Mathf.Min(aiAggressionUnit*aiAggressionUnit,1);
    bool AIVulnerable=>opponent!=null&&(opponent.passedOut||opponent.stamina<15||opponent.leftArmDisabled||opponent.rightArmDisabled);
    // cig 0x342a9bc, cip 0x342c010.
    void AIReadActivity() {
        Vector2 l=opponent.leftInput,r=opponent.rightInput;float den=Mathf.Max(aiNubMax,.0001f);
        float target=Mathf.Clamp01((l.magnitude+r.magnitude)/(den*2)*.6f+(Vector2.Distance(l,aiPrevLeft)+Vector2.Distance(r,aiPrevRight))/den*1.5f);
        aiPrevLeft=l;aiPrevRight=r;aiOpponentActivity=Mathf.Lerp(aiOpponentActivity,target,target>aiOpponentActivity?.7f:.25f);
    }
    void AIChoosePersonality() {
        if(Time.time<aiPersonalityDeadline)return;
        float bias=aiBullyBias;if(aiOpponentActivity<.15f)bias+=.25f;if(aiOpponentActivity>.45f)bias-=.25f;bias+=.3f*AIUnit;
        aiPersona=Random.value<Mathf.Clamp01(bias)?OriginalPersona.Bully:OriginalPersona.Matador;
        aiPersonalityDeadline=Time.time+AIRandom(aiPersonaTime);
    }
    // cih/cii 0x342acc0/0x342ba74: original mount order, horizontal
    // distance from character root, dropped items and shields excluded.
    void AIRefreshThreat() {
        aiNearestThreat=null;float nearest=float.MaxValue;
        if(opponent==null)return;
        foreach(var item in new[]{opponent.leftItem,opponent.rightItem}) {
            if(item==null||item.dropped||item.isShield)continue;
            var mount=item.transform.parent;
            if(mount==null)continue;
            float d=Mathf.Abs(mount.position.x-transform.position.x);
            if(d<nearest){nearest=d;aiNearestThreat=mount;}
        }
    }
    // cjc 0x342add0 and cjd 0x342af00.
    bool AIShieldStateBlocked=>OriginalAIState==OriginalAIStates.Attacking||OriginalAIState==OriginalAIStates.Cowering||OriginalAIState==OriginalAIStates.EnjoyingTheVictory;
    void AISetShieldDefending() {
        bool guard=Time.time>=aiShieldDeadline&&aiNearestThreat!=null&&!AIShieldStateBlocked;
        if(aiShieldLeft)aiLeft.Defending=guard&&Time.time>=aiSuppressLeft;
        if(aiShieldRight)aiRight.Defending=guard&&Time.time>=aiSuppressRight;
    }
    void AITrackShield() {
        if(!aiShieldLeft&&!aiShieldRight)return;
        bool active=aiShieldStrength>0&&aiNearestThreat!=null&&Time.time>=aiShieldDeadline&&!AIShieldStateBlocked&&Time.time>=aiDodgeDeadline;
        bool left=aiShieldLeft&&!aiStrikeLeft&&Time.time>=aiSuppressLeft,right=aiShieldRight&&!aiStrikeRight&&Time.time>=aiSuppressRight;
        if(!left&&!right)return;
        Vector2 target=Vector2.zero;
        if(active) {
            Vector3 origin=aiAimOrigin!=null?aiAimOrigin.position:transform.position;
            float desired=Mathf.Clamp(OriginalStrikeCurves.AimDegrees(transform,origin,aiNearestThreat.position,aiAngleOffset),-aiMaxAim,aiMaxAim);
            float restAngle=Mathf.Atan2(OriginalStrikeCurves.Rest.y,OriginalStrikeCurves.Rest.x)*Mathf.Rad2Deg;
            float angle=Mathf.LerpAngle(restAngle,desired,Mathf.Clamp01(aiShieldStrength));
            target=new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad))*aiShieldRadius;
        }
        if(left){aiGuardLeft=true;aiGuardActiveLeft=active;aiGuardNubLeft=Vector2.Lerp(aiGuardNubLeft,target,aiShieldSpeed*Time.deltaTime);}
        if(right){aiGuardRight=true;aiGuardActiveRight=active;aiGuardNubRight=Vector2.Lerp(aiGuardNubRight,target,aiShieldSpeed*Time.deltaTime);}
    }
    // cje 0x342c484.
    void AISetWeaponDefending(bool defending) {
        if(aiShieldLeft||aiShieldRight)return;
        if(defending&&Random.value>.5f-.35f*AIUnit)return;
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
        Transform target=opponent.chest!=null?opponent.chest.transform:opponent.transform;
        if(target==null)return Random.Range(1,3);
        float high=AIExposure(target.position.y+.5f),low=AIExposure(target.position.y-.4f);
        int preferred=low<=high?1:2,other=high<low?1:2;
        return Random.value<.5f+.45f*AIUnit?preferred:other;
    }
    void AIRefreshAttackDirection() {
        if(aiNearestThreat!=null)aiThreatY=aiNearestThreat.position.y;
        else if(OriginalAIPlayerWeaponCOM!=null)aiThreatY=OriginalAIPlayerWeaponCOM.position.y;
        if(aiPlayerShieldCOM!=null)aiShieldY=aiPlayerShieldCOM.position.y;
        if(opponent.passedOut)aiAttackDirection=1;
        else if(opponent.leftArmDisabled||opponent.rightArmDisabled) {
            OriginalAIPlayerWeaponCOM=opponent.chest!=null?opponent.chest.transform:opponent.transform;aiPlayerShieldCOM=OriginalAIPlayerWeaponCOM;
            aiAttackDirection=Random.Range(1,3);
        } else aiAttackDirection=AIChooseDirection();
    }
    // cjr 0x342cfb0.
    Vector3 AIProceduralTarget() {
        Vector3 target=opponent!=null?(opponent.chest!=null?opponent.chest.transform.position:opponent.transform.position):transform.position;
        float threatOffset=(aiNearestThreat!=null?aiThreatY:target.y)-target.y;
        float y=target.y-threatOffset+(aiAttackDirection==1?.5f:-.4f);
        if(aiNearestThreat!=null) {
            float delta=y-aiThreatY,weight=Mathf.Clamp01(1-Mathf.Abs(delta)/.7f);
            y+=Mathf.Sign(delta==0?-threatOffset:delta)*weight*.45f;
        }
        target.y=y;return target;
    }
    void AIRewind(){aiLeft.Rewind();aiRight.Rewind();aiDouble.Rewind();}
    void OriginalAIAttackLeft(){aiLeft.Attack(aiAttackDirection,aiAggression);aiLeft.Defending=false;}
    void OriginalAIEnableMove(){aiCanMove=true;}

    // cjk / cd.MoveNext 0x342d908. Realtime waits are intentional.
    IEnumerator AIWalk(int direction) {
        if(aiKickSequence)yield break;
        if(aiLastWalkDirection!=0&&aiLastWalkDirection!=direction&&Time.time-aiLastWalkTime<aiCrossGap)yield break;
        aiLastWalkDirection=direction;aiLastWalkTime=Time.time;float delay=AIRandom(aiDoubleDelay);
        if(direction<0)aiSuppressLeft=Time.time+delay+.05f;else aiSuppressRight=Time.time+delay+.05f;
        OnOriginalMoveTap(direction<0);yield return new WaitForSecondsRealtime(delay);OnOriginalMoveTap(direction<0);
    }
    // ciw/cix, ca/cc: nested iterator preserves a coroutine yield boundary.
    IEnumerator AIToward(){yield return AIWalk(AISignedDistance>0?-1:1);}
    IEnumerator AIAway(){yield return AIWalk(AISignedDistance>0?1:-1);}
    // cjl / bz.MoveNext 0x342d4b8: exact four alternating UI taps.
    IEnumerator AIKick() {
        if(aiKickSequence||Time.time<aiKickDeadline)yield break;
        aiKickSequence=true;aiLastWalkDirection=0;aiKickDeadline=Time.time+aiKickCooldown;
        OnOriginalMoveTap(true);yield return new WaitForSecondsRealtime(AIRandom(aiKickJitter));
        OnOriginalMoveTap(false);yield return new WaitForSecondsRealtime(AIRandom(aiKickDelay));
        OnOriginalMoveTap(true);yield return new WaitForSecondsRealtime(AIRandom(aiKickJitter));
        OnOriginalMoveTap(false);yield return new WaitForSecondsRealtime(aiCrossGap);
        aiLastWalkDirection=0;aiKickSequence=false;aiLastWalkTime=Time.time;
    }
    // cjf/cjg 0x342c52c/0x342c7cc.
    void AIDecideMovement() {
        if(!AIValid)return;AIChoosePersonality();
        if(Time.time>=aiKickDeadline&&AIWallDistance<aiWallX&&AIDistance<aiKickRange&&stamina>20) {
            int[] thresholds={95,90,80,70,40};int index=(int)aiAggression-1,threshold=index>=0&&index<5?thresholds[index]:50;
            if(Random.Range(0,100)>threshold){AIRun(AIKick());AIRun(AIAway());}
        }
        if(OriginalAIState==OriginalAIStates.Attacking||OriginalAIState==OriginalAIStates.Withdrawing||OriginalAIState==OriginalAIStates.Cowering||!aiCanMove)return;
        aiCanMove=false;Invoke(nameof(OriginalAIEnableMove),Mathf.Lerp(.3f,.1f,AIUnit));
        if(OriginalAIState==OriginalAIStates.Defending)AISetWeaponDefending(true);
        if(AIWallDistance>=aiWallX)OriginalAIState=OriginalAIStates.GoingBerserk;
        else if(Time.time<aiDodgeDeadline||AIDistance<aiMinDistance)AIRun(AIAway());
        else {
            float desired=(aiPersona==OriginalPersona.Bully?aiAttackRange*Mathf.Lerp(.75f,aiMinSpacing,AIUnit):aiPreferredDistance)*Random.Range(.94f,1.08f);
            if(AIDistance>desired)AIRun(AIToward());
            else {
                float value=Random.value;
                if(aiPersona==OriginalPersona.Bully) {
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
            if(Time.time<next){yield return null;continue;}
            next=Time.time+AIReactionDelay();AIRollAggression();AIRefreshAttackDirection();
            if(opponent.dead)OriginalAIState=OriginalAIStates.EnjoyingTheVictory;
            else if(opponent.passedOut)OriginalAIState=OriginalAIStates.GoingBerserk;
            else if(leftArmDisabled&&rightArmDisabled){OriginalAIState=OriginalAIStates.Cowering;yield break;}
            else if(AIDistance<aiAttackRange)OriginalAIState=AIVulnerable&&aiAggressionUnit>.5f?OriginalAIStates.GoingBerserk:OriginalAIStates.Attacking;
            else if(!aiAttacking)OriginalAIState=OriginalAIStates.Defending;
            AIDecideMovement();yield return null;
        }
    }
    // ciz / ce.MoveNext 0x342dad4.
    IEnumerator AIActionLoop() {
        while(AIValid) {
            switch(OriginalAIState) {
                case OriginalAIStates.Attacking:AIRun(AIAttack());break;
                case OriginalAIStates.Cowering:
                    for(int i=0;i<5&&AIValid;i++){AIRun(AIAway());yield return new WaitForSeconds(Random.Range(.25f,.75f));}
                    break;
                case OriginalAIStates.GoingBerserk:
                    int count=Mathf.RoundToInt(3+AIUnit*3);
                    for(int i=0;i<count&&AIValid;i++){AIRun(AIToward());yield return new WaitForSeconds(Mathf.Lerp(.45f,.15f,AIUnit));}
                    if(AIValid)AIRun(AIAttack());break;
                case OriginalAIStates.EnjoyingTheVictory:
                    aiExecution.StopAllCoroutines();aiExecution.StartCoroutine(AIVictory());yield break;
            }
            float delay=OriginalAIState==OriginalAIStates.GoingBerserk?Random.Range(aiAggression*.125f,aiAggression/3):AIReactionDelay();
            float deadline=Time.time+delay;while(Time.time<deadline&&AIValid)yield return null;
        }
    }
    // cja / cj.MoveNext 0x342f458.
    IEnumerator AIReactiveLoop() {
        float previous=0,next=0;
        while(AIValid) {
            if(Time.time<next){yield return null;continue;}
            float step=aiRelentless?Mathf.Lerp(.09f,.03f,AIUnit):.12f;next=Time.time+step;
            if(OriginalAIState!=OriginalAIStates.Cowering&&OriginalAIState!=OriginalAIStates.EnjoyingTheVictory) {
                float activity=aiOpponentActivity,distance=AIDistance,threshold=AIStaminaThreshold,high=Mathf.Lerp(.65f,.32f,AIUnit);
                if(activity>high&&previous<=high&&!aiAttacking) {
                    if(aiPersona==OriginalPersona.Matador&&Random.value<Mathf.Lerp(.7f,.3f,AIUnit)) {
                        aiDodgeDeadline=Time.time+AIRandom(aiDodgeDuration);AISetWeaponDefending(true);AIRun(AIAway());AIRun(AIDodgeCounter());
                    } else if(distance<aiAttackRange&&stamina>threshold&&AIInBand&&Random.value<Mathf.Lerp(.4f,.95f,AIUnit)) {
                        OriginalAIState=OriginalAIStates.Attacking;AIRun(AIAttack());
                    } else AISetWeaponDefending(true);
                } else if(activity<high*.4f&&previous>=high*.4f&&stamina>threshold&&!aiAttacking) {
                    aiDodgeDeadline=0;
                    if(AIInBand){OriginalAIState=OriginalAIStates.Attacking;AIRun(AIAttack());}
                    else if(AIDistance>aiAttackRange){AIRun(AIToward());OriginalAIState=OriginalAIStates.Attacking;}
                } else if(activity<.1f&&distance<aiAttackRange&&!aiAttacking&&stamina>20) {
                    aiIdleTime+=step;
                    if(aiIdleTime>1.4f-AIUnit) {
                        aiIdleTime=0;
                        if(AIWallDistance<aiWallX&&Random.value<.3f+.5f*AIUnit)AIRun(AIKick());
                        else {OriginalAIState=OriginalAIStates.Attacking;AIRun(AIAttack());}
                    }
                } else aiIdleTime=0;
                distance=AIDistance;
                if(!aiAttacking&&distance>=aiMinDistance&&distance<=aiAttackRange*1.25f&&stamina>threshold&&Time.time>=aiDodgeDeadline&&OriginalAIState!=OriginalAIStates.Cowering&&OriginalAIState!=OriginalAIStates.EnjoyingTheVictory) {
                    float chance=(.03f+.72f*Mathf.Min(aiAggressionUnit*aiAggressionUnit,1))*(aiPersona==OriginalPersona.Bully?1.3f:1)*(AIVulnerable?1.5f:1);
                    if(Random.value<chance){if(distance>aiAttackRange)AIRun(AIToward());OriginalAIState=OriginalAIStates.Attacking;AIRun(AIAttack());}
                }
                previous=activity;
            }
            yield return null;
        }
    }
    // cjb / cg.MoveNext 0x342ea20.
    IEnumerator AIDodgeCounter() {
        yield return new WaitForSeconds(AIRandom(aiDodgeDuration));
        if(!AIValid||aiAttacking||Random.value>.35f+.6f*AIUnit)yield break;
        aiDodgeDeadline=0;
        if(AIDistance>aiAttackRange){AIRun(AIToward());yield return new WaitForSeconds(Mathf.Lerp(.25f,.08f,AIUnit));}
        if(AIValid&&!aiAttacking&&stamina>AIStaminaThreshold){OriginalAIState=OriginalAIStates.Attacking;AIRun(AIAttack());}
    }
    // cjp / cf.MoveNext 0x342df28. Early-abort and follow-up re-entry retain
    // the original asynchronous scheduling and random draw order.
    IEnumerator AIAttack() {
        if(aiAttacking)yield break;aiAttacking=true;
        if(!AIValid){aiAttacking=false;yield break;}
        bool execute=stamina>=AIStaminaThreshold&&Random.value>=AIAbortChance;
        if(execute) {
            if(!opponent.passedOut&&AIWallDistance<aiWallX&&AIDistance<aiMinDistance) {
                aiAttacking=false;AIRun(AIAway());
                if(Time.time>=aiKickDeadline&&stamina>25&&Random.value<.25f-.2f*AIUnit)AIRun(AIKick());
                yield break;
            }
            aiDodgeDeadline=0;
            if(leftArmDisabled||rightArmDisabled){aiWeaponLeft=aiWeaponRight=true;aiShieldLeft=aiShieldRight=false;}
            AIRefreshAttackDirection();
            OriginalAIChargeRequested?.Invoke(true); // CharacterExpressions.Charge, 0x342e8ac.
            if(Random.value<.5f*AIUnit){yield return new WaitForSeconds(Random.Range(.06f,.14f));aiAttackDirection=aiAttackDirection==1?2:1;}
            if(OriginalAIProceduralStrikes) {
                if(aiTwoHanded)yield return AIRun(AIProceduralStrike(OriginalStrikeHand.Double));
                else if(aiWeaponLeft&&aiWeaponRight) {
                    float duration=.83f*(1-.25f*AIUnit);AIRun(AIProceduralStrike(OriginalStrikeHand.Right));
                    yield return new WaitForSeconds(duration*Mathf.Lerp(.85f,.45f,AIUnit));
                    if(AIValid)yield return AIRun(AIProceduralStrike(OriginalStrikeHand.Left));
                } else if(aiWeaponLeft)yield return AIRun(AIProceduralStrike(OriginalStrikeHand.Left));
                else if(aiWeaponRight)yield return AIRun(AIProceduralStrike(OriginalStrikeHand.Right));
                else yield return new WaitForSeconds(.95f);
            } else {
                float duration=aiTwoHanded?1.25f:.95f,leftDelay=0;
                if(aiWeaponLeft) {
                    if(Random.value<.45f+.5f*AIUnit){leftDelay=duration*Mathf.Lerp(.85f,.45f,AIUnit);Invoke(nameof(OriginalAIAttackLeft),leftDelay);}
                    else aiLeft.Defending=true;
                }
                if(aiWeaponRight){aiRight.Attack(aiAttackDirection,aiAggression);aiRight.Defending=false;}
                if(aiTwoHanded){aiDouble.Attack(aiAttackDirection,aiAggression);aiDouble.Defending=false;aiLeft.enabled=aiRight.enabled=false;}
                yield return new WaitForSeconds(duration+leftDelay);
            }
        }
        if(!AIValid){aiAttacking=false;yield break;}
        AIRewind();
        float repeatChance=(aiPersona==OriginalPersona.Matador?.2f+.55f*AIUnit:.4f+.55f*AIUnit)/Mathf.Max(.01f,aiRetreatBoost);
        if(Random.value<repeatChance&&AIDistance<aiAttackRange+.5f&&stamina>AIStaminaThreshold) {
            yield return new WaitForSeconds(Random.Range(.85f,1.15f)*Mathf.Lerp(.3f,.06f,AIUnit));
            aiAttacking=false;if(AIValid){OriginalAIState=OriginalAIStates.Attacking;AIRun(AIAttack());}yield break;
        }
        aiAttacking=false;OriginalAIState=OriginalAIStates.Defending;
        if(AIDistance<aiMinDistance)AIRun(AIAway());
        if(Random.value<AIAbortChance)AIRun(AIPullBackward());
    }
    // cjs / ch.MoveNext 0x342eccc, cv 0x35142c8..0x3516050.
    IEnumerator AIProceduralStrike(OriginalStrikeHand hand) {
        bool both=hand==OriginalStrikeHand.Double;int category=both?aiCategoryDouble:hand==OriginalStrikeHand.Left?aiCategoryLeft:aiCategoryRight;
        float stab=category==0?.55f:category==1?.35f:category==7?.9f:0;
        var kind=Random.value<stab?OriginalStrikeCurves.Kind.Stab:aiAttackDirection==1?OriginalStrikeCurves.Kind.SwingUp:OriginalStrikeCurves.Kind.SwingDown;
        var profile=OriginalStrikeCurves.Profile.Default(kind,both).Varied(aiVariation);profile.duration*=1-.25f*AIUnit;
        bool l=both||hand==OriginalStrikeHand.Left,r=both||hand==OriginalStrikeHand.Right;
        if(l){aiStrikeLeft=true;leftActive=true;}if(r){aiStrikeRight=true;rightActive=true;}
        float elapsed=0;
        while(elapsed<profile.duration&&AIValid) {
            float aim=0;
            if(aiAimStrength>0)aim=Mathf.Clamp(OriginalStrikeCurves.AimDegrees(transform,aiAimOrigin!=null?aiAimOrigin.position:transform.position,AIProceduralTarget(),aiAngleOffset)*aiAimStrength,-aiMaxAim,aiMaxAim)+aiAimBias;
            if(both)OriginalStrikeCurves.Evaluate2H(profile,elapsed/profile.duration,aim,out aiStrikeNubLeft,out aiStrikeNubRight);
            else if(l)aiStrikeNubLeft=OriginalStrikeCurves.Evaluate(profile,elapsed/profile.duration,OriginalStrikeCurves.Rest,aim);
            else aiStrikeNubRight=OriginalStrikeCurves.Evaluate(profile,elapsed/profile.duration,OriginalStrikeCurves.Rest,aim);
            elapsed+=Time.deltaTime;yield return null;
        }
        if(l){aiStrikeLeft=false;aiStrikeNubLeft=OriginalStrikeCurves.Rest;}if(r){aiStrikeRight=false;aiStrikeNubRight=OriginalStrikeCurves.Rest;}
    }
    // cjt / ci.MoveNext 0x342f278.
    IEnumerator AIPullBackward() {
        AIRewind();OriginalAIState=AIWallDistance<=aiWallX?OriginalAIStates.Defending:OriginalAIStates.GoingBerserk;
        yield return new WaitForSeconds(Random.Range(.8f,1.2f)*(1-.75f*AIUnit));
        if(AIValid) {
            if(AIWallDistance<=aiWallX)AIRun(AIAway());else AIRun(AIWalk(transform.position.x<=0?1:-1));
            aiAttacking=false;
        }
    }
    // cju / ck.MoveNext 0x342fa5c.
    IEnumerator AIVictory() {
        aiStrikeLeft=aiStrikeRight=aiGuardLeft=aiGuardRight=false;aiSuppressLeft=aiSuppressRight=0;
        aiStrikeNubLeft=aiStrikeNubRight=aiGuardNubLeft=aiGuardNubRight=OriginalStrikeCurves.Rest;
        OriginalAIReactionTime=Random.Range(.15f,.35f);yield return new WaitForSeconds(Random.Range(.5f,1f));
        AIRun(AIWalk(1));foreach(var relay in new[]{aiLeft,aiRight,aiDouble}){relay.Stop();relay.Rewind();}
        aiLeft.Defending=aiRight.Defending=false;
    }
}
