using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public J ProbeSourceAI(){
        if(!IsSmoke)throw new InvalidOperationException("Source AI probe requires an isolated acceptance save");
        Clock=elapsed=2;ResetRound();RunCampaign(true);fixtureMode=true;
        player.testOverride=enemy.testOverride=true;
        var result=enemy.ProbeSourceAIState(player);
        fixtureMode=false;PauseCampaign();return result;
    }
    public sealed partial class Fighter {
        internal J ProbeSourceAIState(Fighter target){
            InitializeAI();sourceOpponent=target;SetSourceAIWeaponProperties();aiStarted=true;aiDriving=true;
            aiRandom=new SourceAIRandom(90413);AIRollAggression();
            var states=new List<J>();
            for(int i=0;i<7;i++){
                SourceStopJobs();aiAttacking=false;aiKickSequence=false;aiLastWalkDirection=0;aiLastWalkTime=-10;
                SourceAIState=(SourceAIStates)i;int before=SourceAIWalkTaps;
                AIRun(AIActionLoop());
                states.Add(J.O("state",i,"name",((SourceAIStates)i).ToString(),"jobs",sourceJobs.Count,"taps",SourceAIWalkTaps-before));
            }
            SourceStopJobs();aiAttacking=false;aiKickSequence=false;SourceAIReactionTime=1;
            aiLeft.Attack(1,3);aiRight.Attack(2,3);
            var leftClip=SourceAILeftClip;var rightClip=SourceAIRightClip;
            game.Clock+=.2f;aiLeft.Update(.2f);aiRight.Update(.2f);
            bool parallel=leftClip.Length>0&&rightClip.Length>0;
            // A 1H relay has no binding for the 2H child paths.
            Vector2 held=aiLeft.parent;
            aiLeft.Stop();aiLeft.clipForProbe(DuelSourceMotion.Named("2Hands_Stab"));
            aiLeft.Update(.1f);bool missingBinding=Vector2.Distance(held,aiLeft.parent)<.000001f;

            sourceTapLeft=sourceTapRight=sourceDoubleLeft=sourceDoubleRight=-1000;kickUntil=0;
            SourceAIMoveTap(true);game.Clock+=.05f;SourceAIMoveTap(false);
            game.Clock+=.05f;SourceAIMoveTap(true);game.Clock+=.05f;SourceAIMoveTap(false);
            bool tapKick=ReferenceKicking;kickUntil=0;
            SourceStopJobs();aiAttacking=false;aiKickSequence=false;
            aiLastWalkDirection=0;aiLastWalkTime=-10;aiDodgeDeadline=0;
            SourceAIState=SourceAIStates.Defending;SourceAIReactionTime=1;
            aiRandom=new SourceAIRandom(19790529);AIRollAggression();
            AIRun(AIStateLoop());AIRun(AIActionLoop());AIRun(AIReactiveLoop());
            AIRun(aiLeft.Defend());AIRun(aiRight.Defend());AIRun(aiDouble.Defend());
            AIRun(AIWalk(-1));AIRun(AIKick());
            for(int i=0;i<5;i++){game.Clock+=1f/60;ReadSourceAI(target,false);}
            SourceAIClockPaused(true);
            string frozen=CaptureSourceAI().ToString();
            // Simulate elapsed process time without sleeping or changing the
            // duel's scaled clock. A paused scheduler must preserve its queue.
            sourceRealtimeRaw-=2;
            bool pausedRealtimeStable=frozen==CaptureSourceAI().ToString();
            SourceAIClockPaused(false);
            float logicalBefore=SourceRealtime;
            bool previousMode=game.fixtureMode;game.fixtureMode=!previousMode;RebaseSourceAIClock();
            game.fixtureMode=previousMode;RebaseSourceAIClock();
            bool fixtureRealtimeStable=logicalBefore==SourceRealtime;
            var checkpoint=J.Parse(CaptureSourceAI().ToString());float savedClock=game.Clock;
            // Tap acceptance also reads the mover's kick timer. Production
            // CaptureReference saves it alongside source_ai; the isolated AI
            // probe must restore that dependency before replaying its queue.
            var kickCheckpoint=J.Parse(J.O("kick_left",Mathf.Max(0,kickUntil-game.Clock),"kick_state",CaptureReferenceKick()).ToString());
            var first=ProbeSourceAIContinuation(target,120);
            game.Clock=savedClock;
            // RestoreReference normally restores kickUntil before invoking
            // RestoreReferenceKick, whose scope is the paid/receiver state.
            kickUntil=game.Clock+(float)kickCheckpoint["kick_left"].N;
            RestoreReferenceKick(kickCheckpoint);RestoreSourceAI(checkpoint);
            var second=ProbeSourceAIContinuation(target,120);
            bool continuation=first.ToString()==second.ToString();
            return J.O("states",new J(states),"parallel_relays",parallel,"left_clip",leftClip,"right_clip",rightClip,
                "missing_binding_held",missingBinding,"tap_kick",tapKick,"queue_checkpoint",checkpoint,
                "pause_realtime_stable",pausedRealtimeStable,"fixture_realtime_stable",fixtureRealtimeStable,
                "continuation_equal",continuation,"first",first,"restored",second);
        }
        J ProbeSourceAIContinuation(Fighter target,int frames){
            var samples=new List<J>();
            for(int i=0;i<frames;i++){
                game.Clock+=1f/60;ReadSourceAI(target,false);
                if(i%15==0)samples.Add(J.O("state",(int)SourceAIState,"persona",(int)aiPersona,"aggression",aiAggression,
                    "attacking",aiAttacking,"move",move,"kicking",ReferenceKicking,"left",SourceValue(aiLeft.parent,typeof(Vector2)),"right",SourceValue(aiRight.parent,typeof(Vector2)),
                    "left_nub",SourceValue(sourceNubLeft,typeof(Vector2)),"right_nub",SourceValue(sourceNubRight,typeof(Vector2)),"draws",((SourceAIRandom)aiRandom).draws));
            }
            return J.O("samples",new J(samples),"final",CaptureSourceAI());
        }
    }
}
