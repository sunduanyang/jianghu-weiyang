using System;
using System.Collections;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;
using Random = UnityEngine.Random;

// AIHandsInputRelayer, APK ARM64 0x3417d28..0x3419180. The original
// AnimationClips and weighted bundle lists are used without resampling.
public sealed class OriginalHandAnimations : MonoBehaviour {
    public bool Defending;
    public string CurrentClip { get; private set; }
    OriginalRig owner;
    Animation animationSource;
    Dictionary<string,AnimationClip> clips;
    string[] attackUp=Array.Empty<string>(),attackDown=Array.Empty<string>();
    Vector3 rest;
    float guardX,lastStartOffset;
    bool initialized;
    readonly List<Motion> motions=new List<Motion>();
    sealed class Motion {public Vector3 start,target;public float age,duration;public int axes;public bool outCirc,world;}
    public void Initialize(OriginalRig rig,J data,Dictionary<string,AnimationClip> allClips) {
        owner=rig;clips=allClips;animationSource=rig.Resolve<Animation>(data["MainAnimation"]);
        Defending=data["Defending"].B;
        // Start, 0x3417d28: both stored positions include the original +.15 X.
        rest=transform.localPosition+Vector3.right*.15f;guardX=rest.x;
        initialized=true;Invoke(nameof(StartDefendingCoroutine),1f);
    }
    public void SetBundle(bool twoHanded,int category) {
        string[] categories={"Dagger","Sword","Axe","Mace","Hammer","Flail","Stick","Spear","Shield"};
        if(category<0||category>=categories.Length)throw new ArgumentOutOfRangeException(nameof(category));
        var bundle=Bundles[categories[category]+(twoHanded?"_2H":"_1H")];
        attackUp=Names(bundle["up"]);attackDown=Names(bundle["down"]);
        foreach(var names in new[]{attackUp,attackDown})foreach(string name in names) {
            if(!clips.TryGetValue(name,out var clip))throw new InvalidOperationException("Original AI clip missing: "+name);
            animationSource.AddClip(clip,name);
        }
    }
    static J bundles;
    static J Bundles {get {if(bundles==null)bundles=J.Parse(Resources.Load<TextAsset>("OriginalAIBundles").text);return bundles;}}
    static string[] Names(J list) {var result=new List<string>();foreach(var n in list.A)result.Add(n.S);return result.ToArray();}
    // Attack, 0x341884c. The native icall is AnimationState.set_time,
    // not set_speed: this advances the clip's starting time by up to .275s.
    public void Attack(int direction,float aggression) {
        var names=direction==1?attackUp:attackDown;
        if(animationSource==null||names.Length==0)throw new InvalidOperationException("Original AI animation bundle was not configured");
        animationSource.Stop();Defending=false;
        switch(aggression) {
            case 1:lastStartOffset=Random.Range(.05f,.2f);break;
            case 2:lastStartOffset=Random.Range(.05f,.25f);break;
            case 3:lastStartOffset=Random.Range(.15f,.35f);break;
            case 4:lastStartOffset=Random.Range(.25f,.45f);break;
            case 5:lastStartOffset=Random.Range(.45f,.55f);break;
        }
        CurrentClip=names[Random.Range(0,names.Length)];
        animationSource.Play(CurrentClip);animationSource[CurrentClip].time=lastStartOffset*.5f;
    }
    public void Rewind(){if(animationSource!=null)animationSource.Rewind();}
    public void Stop(){if(animationSource!=null)animationSource.Stop();}
    // StartDefendingCoroutine 0x341867c retries after .5s while the player
    // weapon COM reference is unavailable; bs.MoveNext is 0x3418e20.
    void StartDefendingCoroutine() {
        if(!initialized||owner==null)return;
        if(owner.OriginalAIPlayerWeaponCOM==null){Invoke(nameof(StartDefendingCoroutine),.5f);return;}
        StartCoroutine(Defend());
    }
    IEnumerator Defend() {
        while(owner!=null) {
            float reaction=owner.OriginalAIReactionTime;
            yield return new WaitForSeconds(Random.Range(reaction*.5f,reaction));
            if(!Defending) {
                Tween(rest,7,Random.Range(reaction*.5f,reaction*2),false);
            } else {
                Stop();
                Tween(new Vector3(guardX,0,0),1,Random.Range(reaction/2.5f,reaction),true);
                var target=owner.OriginalAIPlayerWeaponCOM;
                if(target!=null)Tween(new Vector3(0,target.position.y+Random.Range(-.1f,.1f),0),2,Random.Range(reaction/2.5f,reaction),true,true);
            }
            yield return new WaitForSeconds(Random.Range(reaction*.5f,reaction*2));
        }
    }
    void Tween(Vector3 target,int axes,float duration,bool outCirc,bool world=false) {
        // 0x36f3e6c is DOMoveY, while 0x36f4390 is DOLocalMoveX.
        motions.Add(new Motion{start=world?transform.position:transform.localPosition,target=target,axes=axes,duration=duration,outCirc=outCirc,world=world});
    }
    void Update() {
        // DOTween's default easing is OutQuad. Guard X/Y tweens explicitly
        // select Ease.OutCirc (21). New tweens are evaluated last as originally.
        for(int i=0;i<motions.Count;) {
            var m=motions[i];m.age+=Time.deltaTime;float t=Mathf.Clamp01(m.duration<=0?1:m.age/m.duration);
            float q=m.outCirc?Mathf.Sqrt(1-(t-1)*(t-1)):1-(1-t)*(1-t);
            Vector3 p=m.world?transform.position:transform.localPosition,v=Vector3.LerpUnclamped(m.start,m.target,q);
            if((m.axes&1)!=0)p.x=v.x;if((m.axes&2)!=0)p.y=v.y;if((m.axes&4)!=0)p.z=v.z;
            if(m.world)transform.position=p;else transform.localPosition=p;
            if(t>=1)motions.RemoveAt(i);else i++;
        }
    }
}

// Original optional cv procedural strikes (default enemy prefab disables
// this route). Constants below are read from ELF PT_LOAD mappings, rather
// than interpreting packed Vector2 literals as individual floats.
public static class OriginalStrikeCurves {
    public enum Kind {SwingUp,SwingDown,Stab}
    public static readonly Vector2 Rest=new Vector2(.425f,.55f);
    public static readonly Vector2 RestLeft=new Vector2(.39f,.7f);
    public static readonly Vector2 RestRight=new Vector2(.07f,.7f);
    public struct Profile {
        public float duration,windupSnap,windupEnd,strikeEnd,windupAngle,windupRadius,strikeFromAngle,strikeToAngle,strikeRadius,followAngle,followRadius;
        public bool holdExtension;
        // cv/Profile.Default 0x3515e48.
        public static Profile Default(Kind kind,bool twoHanded) {
            int k=(int)kind;
            float[] strikeEnds={.66f,.687f,.66f},windAngles={115,106,115},windRadii={2.34f,2.14f,2.34f};
            float[] fromAngles={71,76,131},toAngles={-7,-16,-4},radii={2.36f,1.85f,2.36f};
            float[] followAngles={-76,-40,18},followRadii={.94f,.55f,2.48f},snaps={.18f,.4f,.18f};
            return new Profile {duration=twoHanded?1:.83f,windupSnap=snaps[k],windupEnd=.6f,strikeEnd=strikeEnds[k],windupAngle=windAngles[k],windupRadius=windRadii[k],strikeFromAngle=fromAngles[k],strikeToAngle=toAngles[k],strikeRadius=radii[k],followAngle=followAngles[k],followRadius=followRadii[k],holdExtension=kind==Kind.Stab};
        }
        // cv/Profile.Varied 0x3515f40. Keep all random draws in native order.
        public Profile Varied(float amount) {
            float a=Mathf.Clamp01(amount);var p=this;
            p.duration*=Random.Range(1-.3f*a,1+.25f*a);
            p.windupSnap*=Random.Range(1-.35f*a,1+.35f*a);
            p.windupEnd+=a*Random.Range(-.14f,.1f);
            p.windupAngle+=a*Random.Range(-22f,22f);
            p.windupRadius*=Random.Range(1-.22f*a,1+.28f*a);
            p.strikeFromAngle+=a*Random.Range(-18f,18f);
            p.strikeToAngle+=a*Random.Range(-14f,14f);
            p.strikeRadius*=Random.Range(1-.15f*a,1+.2f*a);
            p.followAngle+=a*Random.Range(-25f,25f);
            if(!p.holdExtension)p.followRadius*=Random.Range(1-.25f*a,1+.25f*a);
            p.windupSnap=Mathf.Clamp(p.windupSnap,.06f,.5f);
            p.windupEnd=Mathf.Clamp(p.windupEnd,p.windupSnap+.05f,.8f);
            p.strikeEnd=Mathf.Clamp(p.windupEnd+.06f,p.windupEnd+.03f,.93f);
            return p;
        }
    }
    // cv.Evaluate 0x35142c8.
    public static Vector2 Evaluate(Profile p,float time,Vector2 rest,float aim) {
        float t=Mathf.Clamp01(time),restAngle=Mathf.Atan2(rest.y,rest.x)*Mathf.Rad2Deg,restRadius=rest.magnitude;
        float angle,radius;
        if(t<p.windupSnap) {
            float q=t/Mathf.Max(p.windupSnap,.0001f);
            angle=Mathf.LerpAngle(restAngle,p.windupAngle+aim,q);radius=Mathf.Lerp(restRadius,p.windupRadius,q);
        } else if(t<p.windupEnd) {
            float q=(t-p.windupSnap)/Mathf.Max(p.windupEnd-p.windupSnap,.0001f);
            angle=Mathf.LerpAngle(p.windupAngle+aim,p.strikeFromAngle+aim,q);
            radius=Mathf.Lerp(p.windupRadius,p.strikeRadius,Mathf.Clamp01(q*.3f));
        } else if(t<p.strikeEnd) {
            float q=(t-p.windupEnd)/Mathf.Max(p.strikeEnd-p.windupEnd,.0001f);
            angle=Mathf.LerpAngle(p.strikeFromAngle+aim,p.strikeToAngle+aim,q);radius=Mathf.Lerp(p.windupRadius,p.strikeRadius,q);
        } else {
            float q=(t-p.strikeEnd)/Mathf.Max(1-p.strikeEnd,.0001f),fast=Mathf.Clamp01(q*2.2f);
            angle=Mathf.LerpAngle(p.strikeToAngle+aim,p.followAngle+aim,fast);radius=Mathf.Lerp(p.strikeRadius,p.followRadius,fast);
            float back=Mathf.Clamp01((q-.6f)/.4f);angle=Mathf.LerpAngle(angle,restAngle,back);radius=Mathf.Lerp(radius,restRadius,back);
        }
        return new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad))*radius;
    }
    // cv.Evaluate2H 0x3515520.
    public static void Evaluate2H(Profile p,float time,float aim,out Vector2 left,out Vector2 right,bool rightDominant=true,float supportRadius=.55f,float supportAngle=23) {
        Vector2 main=Evaluate(p,time,rightDominant?RestRight:RestLeft,aim);
        p.windupRadius*=supportRadius;p.strikeRadius*=supportRadius;p.followRadius*=supportRadius;
        Vector2 support=Evaluate(p,time,rightDominant?RestLeft:RestRight,aim+supportAngle);
        left=rightDominant?support:main;right=rightDominant?main:support;
    }
    // cv.AimDegrees 0x35156f0.
    public static float AimDegrees(Transform anchor,Vector3 origin,Vector3 target,float offset) {
        Vector3 d=anchor.InverseTransformDirection(target-origin);
        return Mathf.Atan2(d.y,anchor.lossyScale.x>=0?d.x:-d.x)*Mathf.Rad2Deg+offset;
    }
}
