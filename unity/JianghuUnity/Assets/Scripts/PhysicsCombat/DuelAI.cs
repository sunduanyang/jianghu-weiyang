using System;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        enum AIPhase { Guard, Windup, Strike, Recover, Retreat }
        bool aiDriving,aiThrust,aiOffAttack;
        Vector2 aiMain,aiOff,aiAim;
        System.Random aiRandom;
        AIPhase aiPhase;
        float aiDecisionAt,aiPhaseAt,aiUntil,aiArc,aiSign,aiAimBias;
        public int aiDecisions,aiAttacks,aiRetargets;
        public string aiTargetZone="body";
        public string AIState=>aiPhase.ToString();
        float AIRandom(float low,float high)=>Mathf.Lerp(low,high,(float)aiRandom.NextDouble());
        Vector2 AIDirection(float localDegrees){float a=localDegrees*Mathf.Deg2Rad;return new Vector2(facing*Mathf.Cos(a),Mathf.Sin(a));}
        void AITransition(AIPhase phase,float duration){aiPhase=phase;aiPhaseAt=game.Clock;aiUntil=game.Clock+duration;}
        void InitializeAI(){
            if(aiRandom!=null)return;
            int seed=game.Campaign?game.campaign.battle["seed"].I:42;
            foreach(char c in weapon.id)seed=unchecked(seed*31+c);
            aiRandom=new System.Random(seed^(human?7919:104729));
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
                    AITransition(AIPhase.Windup,AIRandom(.13f,.29f)/weapon.handling);
                }
            }
            if(aiPhase==AIPhase.Windup&&game.Clock>=aiUntil){AITransition(AIPhase.Strike,AIRandom(.17f,.29f)/weapon.handling);aiAttacks++;}
            else if(aiPhase==AIPhase.Strike&&game.Clock>=aiUntil)AITransition(AIPhase.Recover,AIRandom(.22f,.5f)/weapon.handling);
            else if(aiPhase==AIPhase.Recover&&game.Clock>=aiUntil){AITransition(AIPhase.Guard,AIRandom(.08f,.22f));aiDecisionAt=Mathf.Min(aiDecisionAt,game.Clock);}
            if(aiPhase==AIPhase.Guard)return;
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
                aiAimBias=Mathf.Clamp(aiAimBias-Mathf.DeltaAngle(wanted,sword.rb.rotation+90)*Time.deltaTime,-50,50);
                float axis=(wanted+aiAimBias)*Mathf.Deg2Rad;
                aiMain=main*reach;
                aiOff=Vector2.ClampMagnitude((new Vector2(Mathf.Cos(axis),Mathf.Sin(axis))-main)*(gripSpacing/.42f),1);
            }else if(aiOffAttack)aiOff=command;
            else aiMain=command;
            if(aiPhase==AIPhase.Strike&&dist>preferred-.2f)move=Mathf.Sign(dx)*.65f;
        }
    }
}
