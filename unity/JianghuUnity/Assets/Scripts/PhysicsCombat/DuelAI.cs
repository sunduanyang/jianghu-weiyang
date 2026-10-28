using System;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        enum AIPhase { Guard, Windup, Strike, Recover, Retreat }
        bool aiDriving,aiThrust,aiOffAttack;
        Vector2 aiMain,aiOff,aiAim;
        System.Random aiRandom;
        AIPhase aiPhase;
        float aiDecisionAt,aiPhaseAt,aiUntil,aiArc,aiSign,aiAimBias;
        DuelSourceMotion.Motion aiSourceAttack;
        float aiSourceAt,aiSourceStart;
        int aiSourceAggression;
        Vector2 aiSourceParentNear,aiSourceParentFar;
        public int aiDecisions,aiAttacks,aiRetargets;
        public string aiTargetZone="body";
        public string AIState=>Reference&&sourceAIInitialized?SourceAIState.ToString():aiPhase.ToString();
        float AIRandom(float low,float high)=>Mathf.Lerp(low,high,(float)aiRandom.NextDouble());
        Vector2 AIDirection(float localDegrees){float a=localDegrees*Mathf.Deg2Rad;return new Vector2(facing*Mathf.Cos(a),Mathf.Sin(a));}
        void AITransition(AIPhase phase,float duration){aiPhase=phase;aiPhaseAt=game.Clock;aiUntil=game.Clock+duration;}
        void InitializeAI(){
            if(aiRandom!=null)return;
            int seed=game.Campaign?game.campaign.battle["seed"].I:42;
            foreach(char c in weapon.id)seed=unchecked(seed*31+c);
            aiRandom=new System.Random(seed^(human?7919:104729));
            if(Reference){aiSourceAggression=1;InitializeSourceAI();return;}
            // CharacterEnemyAI.cik truncates a float Range(1,5).
            aiSourceAggression=Mathf.Clamp((int)AIRandom(1,5),1,5);
            aiDecisionAt=game.Clock+AIRandom(.08f,.22f);AITransition(AIPhase.Guard,.15f);
        }
        Vector2 ObserveOpening(Fighter target){
            Vector2[] points={target.head.rb.position,target.torso.rb.position,target.Joint("kneeNear")};
            string[] zones={"head","body","leg"};
            float best=float.NegativeInfinity;Vector2 chosen=points[1];string zone="body";
            for(int i=0;i<points.Length;i++){
                float score=i==0?1.35f:i==1?1:.8f;
                Vector2 ray=points[i]-limbs["upperNear"].A;
                int count=game.combatPhysics.Raycast(limbs["upperNear"].A,ray.normalized,ray.magnitude,game.guardFilter,game.guardRays);
                for(int n=0;n<count;n++){
                    var contact=game.guardRays[n].collider.GetComponent<DuelContact>();
                    if(contact!=null&&contact.owner==target){score-=1.8f;break;}
                }
                // Prefer a revealed head / low line, with imperfect perception.
                score+=AIRandom(-.24f,.24f)+(target.stamina<20&&i==0?.25f:0);
                if(score<=best)continue;best=score;chosen=points[i];zone=zones[i];
            }
            if(zone!=aiTargetZone||(chosen-aiAim).sqrMagnitude>.16f)aiRetargets++;
            aiTargetZone=zone;
            return chosen+target.pelvis.rb.linearVelocity*AIRandom(.04f,.12f)+new Vector2(AIRandom(-.07f,.07f),AIRandom(-.06f,.06f));
        }
        void GuardHands(Fighter target,bool danger){
            aiMain=AIDirection(weapon.TwoHanded?(weapon.id=="spear"?12:55):65)*.52f;
            Vector2 from=limbs["upperFar"].A;
            Vector2 cover=(head.rb.position+torso.rb.position)*.5f+new Vector2(facing*.45f,.05f);
            if(danger)cover=Vector2.Lerp(cover,target.sword.B,.28f);
            aiOff=(cover-from).normalized*(danger?.88f:.55f);
            if(weapon.TwoHanded)aiOff=Vector2.zero;
        }
        public void ReadAI(Fighter target,bool passive){
            if(testOverride)return;
            InitializeAI();aiDriving=true;swordControl=false;
            if(Reference){ReadSourceAI(target,passive);return;}
            float dx=target.Hip.x-Hip.x,dist=Mathf.Abs(dx);
            float preferred=weapon.id=="spear"?2.35f:weapon.distance*.88f;
            bool danger=target.IsAttacking&&dist<target.weapon.distance+.55f;
            shieldControl=passive||danger;brace=passive||danger&&dist<preferred*.7f;
            GuardHands(target,danger);
            move=passive?0:dist>preferred+.12f?Mathf.Sign(dx):dist<preferred-.32f?-Mathf.Sign(dx)*.65f:0;
            if(passive)return;
            if(game.Clock<stunUntil){move=0;aiMain=Vector2.zero;AITransition(AIPhase.Recover,AIRandom(.15f,.3f));aiDecisionAt=game.Clock+.12f;return;}
            if(StaminaRatio<.18f)recoveringBreath=true;else if(StaminaRatio>.44f)recoveringBreath=false;
            if(recoveringBreath){
                if(aiPhase!=AIPhase.Retreat)AITransition(AIPhase.Retreat,.3f);
                move=-Mathf.Sign(dx)*.65f;brace=false;
                if(!danger){aiOff=Vector2.zero;shieldControl=false;}
                return;
            }
            if(aiPhase==AIPhase.Retreat)AITransition(AIPhase.Recover,AIRandom(.16f,.35f));
            if(game.Clock>=aiDecisionAt){
                aiDecisions++;aiAim=ObserveOpening(target);aiDecisionAt=game.Clock+AIRandom(.11f,.25f);
                if((aiPhase==AIPhase.Guard||aiPhase==AIPhase.Recover&&game.Clock>=aiUntil)&&dist<preferred+.75f&&(!danger||stamina>45)){
                    aiThrust=weapon.id=="spear"||aiRandom.NextDouble()<.28;
                    aiOffAttack=!HasShield&&!weapon.TwoHanded&&aiRandom.NextDouble()<.3;
                    aiArc=AIRandom(65,100);aiSign=aiRandom.NextDouble()<.5?1:-1;
                    if(Reference)BeginSourceAttack(target);
                    else AITransition(AIPhase.Windup,AIRandom(.13f,.29f)/weapon.handling);
                }
            }
            if(aiPhase==AIPhase.Windup&&game.Clock>=aiUntil){AITransition(AIPhase.Strike,AIRandom(.17f,.29f)/weapon.handling);aiAttacks++;}
            else if(aiPhase==AIPhase.Strike&&game.Clock>=aiUntil)AITransition(AIPhase.Recover,AIRandom(.22f,.5f)/weapon.handling);
            else if(aiPhase==AIPhase.Recover&&game.Clock>=aiUntil){AITransition(AIPhase.Guard,AIRandom(.08f,.22f));aiDecisionAt=Mathf.Min(aiDecisionAt,game.Clock);}
            if(aiPhase==AIPhase.Guard)return;
            if(Reference&&aiPhase==AIPhase.Strike&&aiSourceAttack!=null){
                // The source animates target containers. Do not feed these
                // metre-valued curves into the normalized joystick adapter.
                if(aiOffAttack)aiOff=Vector2.one;
                else aiMain=Vector2.one;
                if(weapon.TwoHanded)aiOff=Vector2.one;
                return;
            }
            float t=Mathf.Clamp01((game.Clock-aiPhaseAt)/Mathf.Max(.01f,aiUntil-aiPhaseAt));
            float eased=t*t*(3-2*t);
            Vector2 shoulder=limbs[aiOffAttack?"upperFar":"upperNear"].A;
            Vector2 to=aiAim-shoulder;
            float aimed=Mathf.Atan2(to.y,to.x*facing)*Mathf.Rad2Deg;
            float angle=aiThrust?aimed:aiPhase==AIPhase.Windup?aimed+aiSign*aiArc:aiPhase==AIPhase.Strike?aimed+aiSign*Mathf.Lerp(aiArc,-aiArc*.7f,eased):Mathf.LerpAngle(aimed-aiSign*aiArc*.7f,weapon.readyAngle,eased);
            float reach=aiPhase==AIPhase.Windup?.3f:aiPhase==AIPhase.Strike?Mathf.Lerp(.35f,1,eased):Mathf.Lerp(.9f,.48f,eased);
            Vector2 command=AIDirection(angle)*reach;
            if(weapon.id=="spear"){
                Vector2 main=AIDirection(7);
                Vector2 line=aiAim-sword.WorldPoint(weapon.Local(Vector2.zero,facing));
                float wanted=Mathf.Atan2(line.y,line.x)*Mathf.Rad2Deg;
                float aimDt=game.fixtureMode?Time.fixedDeltaTime:Time.deltaTime;
                aiAimBias=Mathf.Clamp(aiAimBias-Mathf.DeltaAngle(wanted,sword.rb.rotation+90)*aimDt,-50,50);
                float axis=(wanted+aiAimBias)*Mathf.Deg2Rad;
                aiMain=main*reach;
                aiOff=Vector2.ClampMagnitude((new Vector2(Mathf.Cos(axis),Mathf.Sin(axis))-main)*(gripSpacing/.42f),1);
            }else if(aiOffAttack)aiOff=command;
            else aiMain=command;
            if(aiPhase==AIPhase.Strike&&dist>preferred-.2f)move=Mathf.Sign(dx)*.65f;
        }
        string SourceItemType=>weapon.id=="dagger"?"Dagger":weapon.id=="axe"||weapon.id=="greataxe"?"Axe":weapon.id=="mace"?"Mace":weapon.id=="warhammer"?"Hammer":weapon.id=="spear"?"Spear":weapon.id=="unarmed"?"Stick":"Sword";
        void BeginSourceAttack(Fighter target){
            aiSourceParentNear=SourceParentFromHand("Near");aiSourceParentFar=SourceParentFromHand("Far");
            // cjm/cjn compare two heights against the nearest held threats.
            float origin=target.referenceBodies["chestCOM"].position.y;
            bool hasMain=target.weapon.id!="unarmed"&&target.primaryGrip!=null&&target.primaryGrip.enabled;
            bool hasShield=target.HasShield&&target.offhandGrip!=null&&target.offhandGrip.enabled&&target.shield!=null;
            Vector2 nearest=target.sword.rb.position;
            if(hasShield&&(!hasMain||Mathf.Abs(target.shield.rb.position.x-mover.position.x)<Mathf.Abs(nearest.x-mover.position.x)))nearest=target.shield.rb.position;
            float aggression=(aiSourceAggression-1)*.25f;
            bool up=target.PassedOut?true:target.ReferenceDowned?aiRandom.Next(1,3)==1:DuelReferenceRules.AttackUp(origin,hasMain||hasShield,nearest.y,hasShield,hasShield?target.shield.rb.position.y:0,aggression,(float)aiRandom.NextDouble());
            aiSourceAttack=DuelSourceMotion.Choose(SourceItemType,weapon.TwoHanded,up,aiRandom);
            // AIHandsInputRelayer.Attack sets animation TIME, not speed.
            float low=aiSourceAggression<=2?.05f:aiSourceAggression==3?.15f:aiSourceAggression==4?.25f:.45f;
            float high=aiSourceAggression==1?.2f:aiSourceAggression==2?.25f:aiSourceAggression==3?.35f:aiSourceAggression==4?.45f:.55f;
            aiSourceStart=AIRandom(low,high)*.5f;aiSourceAt=game.Clock;
            AITransition(AIPhase.Strike,aiSourceAttack.duration-aiSourceStart);aiAttacks++;
        }
        bool SourceHandTarget(string side,out Vector2 target){
            target=Vector2.zero;
            if(sourceAIInitialized&&TrySourceAITarget(side,out var parent,out var nub)){target=SourceAIWorldHandTarget(side,parent,nub);return true;}
            if(!aiDriving||aiPhase!=AIPhase.Strike||aiSourceAttack==null||(!weapon.TwoHanded&&(side=="Far")!=aiOffAttack))return false;
            string path=weapon.TwoHanded?(side=="Near"?"HandLeftPointContainer":"HandRightPointContainer"):"";
            // Some original one-hand bundles include a two-hand clip. Legacy
            // Animation leaves absent transform bindings untouched.
            Vector2 local=side=="Near"?aiSourceParentNear:aiSourceParentFar;
            if(aiSourceAttack.positions.ContainsKey(path))local=aiSourceAttack.Position(path,aiSourceStart+game.Clock-aiSourceAt);
            // Original target hierarchy: ControlPoints(-.025,0) -> Top ->
            // animated container -> active nub(.15,.25). The current rig's
            // pelvis target is .435 m higher; keep that explicit adaptation.
            target=mover.position+new Vector2(facing*(local.x+.125f),local.y+.25f+.435f);
            if(ReferenceDowned)target+=pelvis.B-(mover.position+new Vector2(0,.935f));
            return true;
        }
        // Physical hierarchy adapter only. The source AI supplies metre-valued
        // animation parents and independent child nub offsets.
        Vector2 SourceAIWorldHandTarget(string side,Vector2 parent,Vector2 nub){
            Vector2 result=mover.position+new Vector2(facing*(parent.x+nub.x-.025f),parent.y+nub.y+.435f);
            if(ReferenceDowned)result+=pelvis.B-(mover.position+new Vector2(0,.935f));
            return result;
        }
        Vector2 SourceParentFromHand(string side){var p=referenceHands[side].target-mover.position;if(ReferenceDowned)p-=pelvis.B-(mover.position+new Vector2(0,.935f));return new Vector2(p.x/facing-.125f,p.y-.685f);}
        J CaptureSourceMotion(){
            // Pausing releases input captures; it must retain an NPC's
            // in-flight animation so a cold restart resumes the same sample.
            bool active=aiPhase==AIPhase.Strike&&aiSourceAttack!=null&&(!human||game.CampaignAutoplay);
            return J.O("version",1,"clip",active?aiSourceAttack.name:"","time",active?Mathf.Clamp(aiSourceStart+game.Clock-aiSourceAt,0,aiSourceAttack.duration):0,"off",active&&aiOffAttack,"aggression",aiSourceAggression);
        }
        void RestoreSourceMotion(J state){
            if(!state.Has)return;
            if(state["clip"].S.Length==0){aiSourceAggression=state["aggression"].I;return;}
            InitializeAI();aiSourceAggression=state["aggression"].I;
            aiSourceParentNear=SourceParentFromHand("Near");aiSourceParentFar=SourceParentFromHand("Far");
            aiSourceAttack=DuelSourceMotion.Named(state["clip"].S);aiSourceStart=(float)state["time"].N;aiSourceAt=game.Clock;aiOffAttack=state["off"].B;
            aiDriving=true;AITransition(AIPhase.Strike,Mathf.Max(0,aiSourceAttack.duration-aiSourceStart));
            if(aiOffAttack)aiOff=Vector2.one;else aiMain=Vector2.one;if(weapon.TwoHanded)aiOff=Vector2.one;
        }
    }
}
