using System;
using UnityEngine;

public static class DuelSourceAIStrikeCurves {
    static float Range(System.Random random,float low,float high)=>Mathf.Lerp(low,high,(float)random.NextDouble());
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
        public Profile Varied(float amount,System.Random random) {
            float a=Mathf.Clamp01(amount);var p=this;
            p.duration*=Range(random,1-.3f*a,1+.25f*a);
            p.windupSnap*=Range(random,1-.35f*a,1+.35f*a);
            p.windupEnd+=a*Range(random,-.14f,.1f);
            p.windupAngle+=a*Range(random,-22f,22f);
            p.windupRadius*=Range(random,1-.22f*a,1+.28f*a);
            p.strikeFromAngle+=a*Range(random,-18f,18f);
            p.strikeToAngle+=a*Range(random,-14f,14f);
            p.strikeRadius*=Range(random,1-.15f*a,1+.2f*a);
            p.followAngle+=a*Range(random,-25f,25f);
            if(!p.holdExtension)p.followRadius*=Range(random,1-.25f*a,1+.25f*a);
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
    public static float AimDegrees(float facing,Vector3 origin,Vector3 target,float offset) {
        Vector3 d=target-origin;
        return Mathf.Atan2(d.y,facing>=0?d.x:-d.x)*Mathf.Rad2Deg+offset;
    }
}
