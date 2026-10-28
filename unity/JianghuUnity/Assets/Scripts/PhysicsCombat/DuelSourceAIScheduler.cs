using System;
using System.Collections;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        sealed class SourceWait {
            public readonly float seconds;
            public readonly bool realtime;
            public SourceWait(float seconds,bool realtime=false){this.seconds=seconds;this.realtime=realtime;}
        }
        sealed class SourceJob {
            public readonly Stack<IEnumerator> stack=new Stack<IEnumerator>();
            public SourceJob child;
            public float due;
            public bool realtime,done;
        }
        readonly List<SourceJob> sourceJobs=new List<SourceJob>();
        float SourceRealtime=>game.fixtureMode?game.Clock:Time.unscaledTime;
        SourceJob AIRun(IEnumerator routine){
            if(aiStopped||routine==null)return null;
            var job=new SourceJob();job.stack.Push(routine);sourceJobs.Add(job);ResumeSourceJob(job);return job;
        }
        void ResumeSourceJob(SourceJob job){
            if(job.done)return;
            if(job.child!=null){if(!job.child.done)return;job.child=null;}
            if((job.realtime?SourceRealtime:game.Clock)<job.due)return;
            while(job.stack.Count>0){
                var current=job.stack.Peek();
                if(!current.MoveNext()){job.stack.Pop();continue;}
                object value=current.Current;
                if(value is IEnumerator nested){job.stack.Push(nested);continue;}
                if(value is SourceJob child){job.child=child;job.due=game.Clock;job.realtime=false;return;}
                if(value is SourceWait wait){job.realtime=wait.realtime;job.due=(wait.realtime?SourceRealtime:game.Clock)+wait.seconds;return;}
                job.realtime=false;job.due=game.Clock;return;
            }
            job.done=true;
        }
        void StepSourceJobs(){
            // StartCoroutine runs up to the first yield immediately. Jobs
            // created while resuming already ran their first segment; do not
            // advance them a second time in this same frame.
            var frame=sourceJobs.ToArray();
            foreach(var job in frame)if(!job.done)ResumeSourceJob(job);
            sourceJobs.RemoveAll(j=>j.done);
        }
        void SourceStopJobs(){foreach(var job in sourceJobs)job.done=true;sourceJobs.Clear();}

        // CharacterMover.clu 0x3435e18: source tap recognizer at 60 Hz.
        // Fusion input ticks are replaced by the shared duel clock; this is
        // deliberately one adapter so the recovered mover can own it later.
        int sourceTapLeft=-1000,sourceTapRight=-1000,sourceDoubleLeft=-1000,sourceDoubleRight=-1000;
        float aiMoveUntil;
        int aiMoveDirection;
        void SourceAIMoveTap(bool left){
            SourceAIWalkTaps++;
            int tick=Mathf.RoundToInt(game.Clock*60);
            if(left){if(tick-sourceTapLeft<=21)sourceDoubleLeft=tick;sourceTapLeft=tick;}
            else{if(tick-sourceTapRight<=21)sourceDoubleRight=tick;sourceTapRight=tick;}
            // kickWindow .175s rounds to 10 ticks (ties-to-even).
            if(tick-sourceDoubleLeft<=10&&tick-sourceDoubleRight<=10&&Mathf.Abs(sourceDoubleLeft-sourceDoubleRight)<=10){
                sourceTapLeft=sourceTapRight=sourceDoubleLeft=sourceDoubleRight=-1000;
                KickReference();return;
            }
            // In source a double tap starts .25s of sustained movement.
            if((left?sourceDoubleLeft:sourceDoubleRight)==tick&&!PassedOut&&!ReferenceKicking&&health>0){
                aiMoveDirection=left?-1:1;aiMoveUntil=game.Clock+.25f;
            }
        }

        sealed class SourceHandRelay {
            readonly Fighter owner;
            readonly bool both;
            readonly Vector2 rest;
            public Vector2 parent;
            public bool Defending,enabled=true;
            DuelSourceMotion.Motion clip;
            float clipAt,clipOffset;
            bool playing;
            public string CurrentClip=>clip==null?"":clip.name;
            sealed class Tween {public Vector2 start,target;public float at,duration;public int axes;public bool circ,worldY;}
            readonly List<Tween> tweens=new List<Tween>();
            public SourceHandRelay(Fighter owner,bool both,Vector2 parent){this.owner=owner;this.both=both;this.parent=parent;rest=parent+Vector2.right*.15f;}
            public void Attack(int direction,float aggression){
                // Native Attack draws the time offset before the bundle index.
                Stop();Defending=false;
                float low=aggression<=2?.05f:aggression==3?.15f:aggression==4?.25f:.45f;
                float high=aggression==1?.2f:aggression==2?.25f:aggression==3?.35f:aggression==4?.45f:.55f;
                clipOffset=owner.AIRandom(low,high)*.5f;
                clip=DuelSourceMotion.Choose(owner.SourceItemType,both,direction==1,owner.aiRandom);
                clipAt=owner.game.Clock;playing=true;owner.aiAttacks++;
            }
            public void Stop(){playing=false;}
            public void Rewind(){if(clip!=null){clipOffset=0;clipAt=owner.game.Clock;Sample(0);}}
            public void Update(float dt){
                if(enabled&&playing&&clip!=null){
                    float time=clipOffset+owner.game.Clock-clipAt;
                    Sample(time);if(time>=clip.duration)playing=false;
                }
                for(int i=0;i<tweens.Count;){
                    var t=tweens[i];float q=Mathf.Clamp01((owner.game.Clock-t.at)/Mathf.Max(t.duration,.00001f));
                    float ease=t.circ?Mathf.Sqrt(1-(q-1)*(q-1)):1-(1-q)*(1-q);
                    var value=Vector2.LerpUnclamped(t.start,t.target,ease);
                    if((t.axes&1)!=0)parent.x=value.x;
                    if((t.axes&2)!=0)parent.y=t.worldY?value.y-owner.SourceAIParentOriginY: value.y;
                    if(q>=1)tweens.RemoveAt(i);else i++;
                }
            }
            void Sample(float time){
                if(both){
                    if(clip.positions.ContainsKey("HandLeftPointContainer"))owner.aiLeft.parent=clip.Position("HandLeftPointContainer",time);
                    if(clip.positions.ContainsKey("HandRightPointContainer"))owner.aiRight.parent=clip.Position("HandRightPointContainer",time);
                }else if(clip.positions.ContainsKey(""))parent=clip.Position("",time);
                // A one-hand bundle can select a two-hand clip. The absent
                // paths remain unbound, matching legacy Animation behavior.
            }
            void AddTween(Vector2 target,int axes,float duration,bool circ,bool worldY=false){
                Vector2 start=parent;if(worldY)start.y+=owner.SourceAIParentOriginY;
                tweens.Add(new Tween{start=start,target=target,axes=axes,duration=duration,circ=circ,worldY=worldY,at=owner.game.Clock});
            }
            public IEnumerator Defend(){
                while(owner.AIValid){
                    // StartDefendingCoroutine retries while no weapon COM.
                    if(owner.SourceAIPlayerWeaponCOM==null){yield return new SourceWait(.5f);continue;}
                    float reaction=owner.SourceAIReactionTime;
                    yield return new SourceWait(owner.AIRandom(reaction*.5f,reaction));
                    if(!Defending)AddTween(rest,3,owner.AIRandom(reaction*.5f,reaction*2),false);
                    else {
                        Stop();AddTween(new Vector2(rest.x,0),1,owner.AIRandom(reaction/2.5f,reaction),true);
                        var target=owner.SourceAIPlayerWeaponCOM;
                        if(target!=null){float y=target.position.y+owner.AIRandom(-.1f,.1f);AddTween(new Vector2(0,y),2,owner.AIRandom(reaction/2.5f,reaction),true,true);}
                    }
                    yield return new SourceWait(owner.AIRandom(reaction*.5f,reaction*2));
                }
            }
        }
        // World-Y defensive tween conversion is kept next to other physical
        // bridge values, separate from the native target/decision calculations.
        float SourceAIParentOriginY=>SourceAIWorldHandTarget("Near",Vector2.zero,Vector2.zero).y;
    }
}
