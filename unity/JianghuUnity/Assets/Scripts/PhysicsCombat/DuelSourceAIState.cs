using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public sealed partial class Fighter {
        // Snapshot only the closed set of routines below. No type names from
        // a save file are loaded, instantiated, or invoked through reflection.
        // Compiler-generated field names are versioned implementation data;
        // a changed routine shape is rejected instead of silently losing a wait.
        const BindingFlags SourceStateFields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        readonly string[] sourceRoutineNames={"state","action","reactive","counter","attack","walk","ciw","cix","kick","pull","victory","left-delay","procedural","guard-left","guard-right","guard-double","legacy-resume"};
        IEnumerator NewSourceRoutine(string name){
            switch(name){
                case "state":return AIStateLoop();case "action":return AIActionLoop();case "reactive":return AIReactiveLoop();
                case "counter":return AIDodgeCounter();case "attack":return AIAttack();case "walk":return AIWalk(0);
                case "ciw":return AICiw();case "cix":return AICix();case "kick":return AIKick();
                case "pull":return AIPullBackward();case "victory":return AIVictory();case "left-delay":return AIDelayedLeft(0);
                case "procedural":return AIProceduralStrike(SourceStrikeHand.Left);
                case "legacy-resume":return AIResumeLegacyMotion(0);
                case "guard-left":return aiLeft.Defend();case "guard-right":return aiRight.Defend();case "guard-double":return aiDouble.Defend();
                default:throw new InvalidOperationException("Unknown source AI routine: "+name);
            }
        }
        J CaptureSourceAICompatibilityMotion(){
            var relay=aiDouble.Playing?aiDouble:aiLeft.Playing?aiLeft:aiRight.Playing?aiRight:null;
            bool active=relay!=null&&(!human||game.CampaignAutoplay);
            return J.O("version",1,"clip",active?relay.CurrentClip:"","time",active?relay.ClipTime:0,"off",active&&ReferenceEquals(relay,aiRight),"aggression",(int)aiAggression);
        }
        void RestoreSourceAILegacyMotion(J state){
            if(!Reference||state["clip"].S.Length==0)return;
            InitializeSourceAI();SetSourceAIWeaponProperties();aiStarted=true;sourceLegacyResume=true;
            aiAggression=state["aggression"].I;aiAggressionUnit=aiRelentless?(aiAggression-1)*.25f:0;
            var clip=DuelSourceMotion.Named(state["clip"].S);float time=(float)state["time"].N;
            (weapon.TwoHanded?aiDouble:state["off"].B?aiRight:aiLeft).ResumeClip(clip,time);
            sourceLegacyWait=Mathf.Max(0,clip.duration-time);aiAttacking=true;SourceAIState=SourceAIStates.Attacking;
        }
        IEnumerator AIResumeLegacyMotion(float delay){yield return new SourceWait(delay);aiAttacking=false;SourceAIState=SourceAIStates.Defending;}
        static bool IsSourceValue(Type type)=>type==typeof(float)||type==typeof(int)||type==typeof(bool)||type.IsEnum||type==typeof(Vector2)||type==typeof(Vector3)||type==typeof(DuelSourceAIStrikeCurves.Profile);
        static J SourceValue(object value,Type type){
            // J uses invariant default number formatting. Widen a float before
            // formatting so Mono's G7 float display cannot lose its last bit.
            if(type==typeof(float))return new J((double)(float)value);if(type==typeof(int))return new J((int)value);if(type==typeof(bool))return new J((bool)value);
            if(type.IsEnum)return new J(Convert.ToInt32(value));
            var data=J.O();foreach(var field in type.GetFields(SourceStateFields))if(IsSourceValue(field.FieldType))data[field.Name]=SourceValue(field.GetValue(value),field.FieldType);return data;
        }
        static object RestoreSourceValue(J value,Type type){
            if(type==typeof(float))return (float)value.N;if(type==typeof(int))return value.I;if(type==typeof(bool))return value.B;if(type.IsEnum)return Enum.ToObject(type,value.I);
            object boxed=Activator.CreateInstance(type);foreach(var field in type.GetFields(SourceStateFields))if(IsSourceValue(field.FieldType)&&value[field.Name].Has)field.SetValue(boxed,RestoreSourceValue(value[field.Name],field.FieldType));return boxed;
        }
        string SourceRoutineName(IEnumerator iterator){
            foreach(string name in sourceRoutineNames){
                var candidate=NewSourceRoutine(name);
                if(candidate.GetType()!=iterator.GetType())continue;
                if(name.StartsWith("guard-")){
                    var ownerField=iterator.GetType().GetField("<>4__this",SourceStateFields);
                    var relay=ownerField==null?null:ownerField.GetValue(iterator);
                    return ReferenceEquals(relay,aiLeft)?"guard-left":ReferenceEquals(relay,aiRight)?"guard-right":"guard-double";
                }
                return name;
            }
            throw new InvalidOperationException("Unregistered source AI iterator");
        }
        J CaptureSourceIterator(IEnumerator iterator){
            var fields=J.O();foreach(var field in iterator.GetType().GetFields(SourceStateFields))if(IsSourceValue(field.FieldType))fields[field.Name]=SourceValue(field.GetValue(iterator),field.FieldType);
            return J.O("routine",SourceRoutineName(iterator),"fields",fields);
        }
        IEnumerator RestoreSourceIterator(J data,float clockShift){
            var iterator=NewSourceRoutine(data["routine"].S);var saved=data["fields"];
            foreach(var field in iterator.GetType().GetFields(SourceStateFields))if(IsSourceValue(field.FieldType)){
                if(!saved[field.Name].Has)throw new InvalidOperationException("Source AI iterator schema changed: "+data["routine"].S);
                object value=RestoreSourceValue(saved[field.Name],field.FieldType);
                if(field.FieldType==typeof(float)&&(field.Name.StartsWith("<next>")||field.Name.StartsWith("<deadline>")))value=(float)value+clockShift;
                field.SetValue(iterator,value);
            }
            return iterator;
        }
        static readonly HashSet<string> SourceOwnedScalarFields=new HashSet<string>{
            "sourceAIInitialized","sourceLeftActive","sourceRightActive","sourceNubLeft","sourceNubRight","sourceAIFrame",
            "sourceTapLeft","sourceTapRight","sourceDoubleLeft","sourceDoubleRight","sourceLegacyResume","sourceLegacyWait","sourceRealtimeValue"};
        static bool SourceStateField(FieldInfo field)=>!field.IsInitOnly&&IsSourceValue(field.FieldType)&&(field.Name.StartsWith("ai")||SourceOwnedScalarFields.Contains(field.Name));
        static bool SourceClockField(string name)=>name=="aiInitializeAt"||name=="aiLastSourceFrame"||name=="aiLastWalkTime"||name=="aiKickDeadline"||name=="aiThreatRefresh"||name=="aiShieldDeadline"||name=="aiDodgeDeadline"||name=="aiPersonalityDeadline"||name=="aiSuppressLeft"||name=="aiSuppressRight"||name=="aiCanMoveAt"||name=="aiMoveUntil"||name=="aiSourceAt"||name=="aiPhaseAt"||name=="aiUntil"||name=="aiDecisionAt";
        public J CaptureSourceAI(){
            if(!sourceAIInitialized)return J.O("version",1,"initialized",false);
            float realtime=SourceRealtime;
            var fields=J.O();foreach(var field in typeof(Fighter).GetFields(SourceStateFields))if(SourceStateField(field))fields[field.Name]=SourceValue(field.GetValue(this),field.FieldType);
            var jobs=new List<J>();foreach(var job in sourceJobs){
                var stack=new List<J>();foreach(var iterator in job.stack)stack.Add(CaptureSourceIterator(iterator));
                jobs.Add(J.O("done",job.done,"due",(double)(job.due-(job.realtime?realtime:game.Clock)),"realtime",job.realtime,"child",job.child==null?-1:sourceJobs.IndexOf(job.child),"stack",new J(stack)));
            }
            var random=aiRandom as SourceAIRandom;
            return J.O("version",1,"initialized",true,"clock",(double)game.Clock,"fields",fields,"state",(int)SourceAIState,"persona",(int)aiPersona,"reaction",(double)SourceAIReactionTime,"procedural",SourceAIProceduralStrikes,
                "random_seed",random==null?0:random.seed,"random_draws",random==null?0:random.draws,"walk_taps",SourceAIWalkTaps,"kick_schedules",SourceAIKickSchedules,"left",aiLeft.Capture(),"right",aiRight.Capture(),"double",aiDouble.Capture(),"jobs",new J(jobs));
        }
        public void RestoreSourceAI(J state){
            if(!state.Has||state["version"].I!=1||!state["initialized"].B)return;
            InitializeAI();InitializeSourceAI();SourceStopJobs();
            float shift=game.Clock-(float)state["clock"].N;
            foreach(var field in typeof(Fighter).GetFields(SourceStateFields))if(SourceStateField(field)&&state["fields"][field.Name].Has){
                object value=RestoreSourceValue(state["fields"][field.Name],field.FieldType);
                if(field.FieldType==typeof(float)&&SourceClockField(field.Name))value=(float)value+shift;
                if(field.FieldType==typeof(int)&&(field.Name=="sourceTapLeft"||field.Name=="sourceTapRight"||field.Name=="sourceDoubleLeft"||field.Name=="sourceDoubleRight"))value=(int)value+Mathf.RoundToInt(shift*60);
                field.SetValue(this,value);
            }
            RestoreSourceRealtimeAnchor(state["fields"]["sourceRealtimeValue"].Has);
            SourceAIState=(SourceAIStates)state["state"].I;aiPersona=(SourcePersona)state["persona"].I;SourceAIReactionTime=(float)state["reaction"].N;SourceAIProceduralStrikes=state["procedural"].B;
            aiRandom=new SourceAIRandom(state["random_seed"].I,state["random_draws"].I);
            SourceAIWalkTaps=state["walk_taps"].I;SourceAIKickSchedules=state["kick_schedules"].I;
            aiLeft.Restore(state["left"],shift);aiRight.Restore(state["right"],shift);aiDouble.Restore(state["double"],shift);
            float restoredRealtime=sourceRealtimeValue;
            foreach(var item in state["jobs"].A){
                var job=new SourceJob{done=item["done"].B,realtime=item["realtime"].B};job.due=(job.realtime?restoredRealtime:game.Clock)+(float)item["due"].N;
                for(int i=item["stack"].A.Count-1;i>=0;i--){var iterator=RestoreSourceIterator(item["stack"][i],shift);job.stack.Push(iterator);if(iterator.GetType().DeclaringType==typeof(SourceHandRelay))job.relayScope=true;}sourceJobs.Add(job);
            }
            for(int i=0;i<sourceJobs.Count;i++){int child=state["jobs"][i]["child"].I;if(child>=0&&child<sourceJobs.Count)sourceJobs[i].child=sourceJobs[child];}
            // Input capture is transient; autonomous state resumes on ReadAI.
            aiDriving=true;
            if(sourcePointLeft!=null)sourcePointLeft.localPosition=sourceNubLeft;
            if(sourcePointRight!=null)sourcePointRight.localPosition=sourceNubRight;
        }
        sealed class SourceAIRandom:System.Random {
            public readonly int seed;
            public int draws {get;private set;}
            public SourceAIRandom(int seed,int skip=0):base(seed){this.seed=seed;for(int i=0;i<skip;i++)base.NextDouble();draws=skip;}
            public override double NextDouble(){draws++;return base.NextDouble();}
            public override int Next(int maxValue){if(maxValue<0)throw new ArgumentOutOfRangeException(nameof(maxValue));return (int)(NextDouble()*maxValue);}
            public override int Next(int minValue,int maxValue){if(minValue>maxValue)throw new ArgumentOutOfRangeException(nameof(maxValue));return minValue+Next(maxValue-minValue);}
        }
    }
}
