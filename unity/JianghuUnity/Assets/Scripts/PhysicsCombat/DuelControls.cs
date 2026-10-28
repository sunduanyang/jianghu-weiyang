using System.Collections.Generic;
using UnityEngine;

// Touch retains independent floating pads. Mouse shares one movement delta
// between the held buttons and remembers both targets through DuelMouseHands.
public sealed class DuelControls {
    public bool reference;
    readonly Vector2[] referenceOrigin=new Vector2[2];
    public sealed class Pad {
        public Vector2 center {get;private set;}
        public const float Radius=90,Deadzone=.08f;
        public bool captured;
        public Vector2 value;
        public bool HasDirection=>captured&&value.sqrMagnitude>0;
        public void Capture(Vector2 p){center=p;value=Vector2.zero;captured=true;}
        public void Move(Vector2 p){Vector2 d=Vector2.ClampMagnitude((p-center)/Radius,1);value=d.magnitude<Deadzone?Vector2.zero:new Vector2(d.x,-d.y);}
    }
    public readonly Pad left=new Pad(),right=new Pad();
    public readonly DuelMouseHands mouse=new DuelMouseHands();
    public readonly Rect back=new Rect(612,840,88,56),dash=new Rect(716,840,168,56),forward=new Rect(900,840,88,56);
    readonly Dictionary<int,int> owners=new Dictionary<int,int>();
    readonly Dictionary<int,float> pressTimes=new Dictionary<int,float>();
    readonly HashSet<int> dragged=new HashSet<int>();
    float tapAt=-10;
    Vector2 tapPoint;
    int tapSide;
    public bool DashPressed;
    public float MoveAxis {get {float axis=0;foreach(int owner in owners.Values){if(owner==3||owner==6)axis--;if(owner==5||owner==7)axis++;}return Mathf.Clamp(axis,-1,1);}}
    public bool HitTest(Vector2 p)=>back.Contains(p)||dash.Contains(p)||forward.Contains(p);
    public bool Arena(Vector2 p)=>p.x>=50&&p.x<=1550&&p.y>=165&&p.y<=810&&!(p.x>=1390&&p.y<=220)&&!(p.x<=210&&p.y<=220);
    public bool Begin(int id,Vector2 p,float at=-1) {
        if(owners.ContainsKey(id))return false;
        float now=at<0?Time.unscaledTime:at;
        int control=back.Contains(p)?3:dash.Contains(p)?4:forward.Contains(p)?5:Arena(p)&&id>=0?p.x<800?1:2:0;
        if(control==0||control==1&&left.captured||control==2&&right.captured)return false;
        // On touch, a short tap followed by a held tap on the same side moves
        // the feet. A drag / a second simultaneous finger remains hand input.
        if(id>=0&&control<3&&control==tapSide&&now-tapAt<=(reference?.35f:.28f)&&(p-tapPoint).sqrMagnitude<3600){control=control==1?6:7;tapAt=-10;}
        owners[id]=control;
        pressTimes[id]=now;
        if(control<3)(control==1?left:right).Capture(p);
        if(control==4)DashPressed=true;
        return true;
    }
    public void Move(int id,Vector2 p){if(owners.TryGetValue(id,out int owner)&&owner<3){Pad pad=owner==1?left:right;pad.Move(p);if(reference)pad.value=Vector2.ClampMagnitude(new Vector2(p.x-pad.center.x,pad.center.y-p.y)/Pad.Radius,1);if((p-pad.center).sqrMagnitude>324)dragged.Add(id);}}
    public void End(int id,float at=-1,bool allowTap=true){if(!owners.TryGetValue(id,out int owner))return;owners.Remove(id);if(owner<3){Pad pad=owner==1?left:right;float now=at<0?Time.unscaledTime:at;if(allowTap&&id>=0&&!dragged.Contains(id)&&now-pressTimes[id]<.18f){tapAt=now;tapPoint=pad.center;tapSide=owner;}pad.captured=false;pad.value=Vector2.zero;}pressTimes.Remove(id);dragged.Remove(id);}
    public void ReleaseAll(bool keepMouseTargets=false){owners.Clear();pressTimes.Clear();dragged.Clear();tapAt=-10;left.captured=right.captured=false;left.value=right.value=Vector2.zero;DashPressed=false;if(keepMouseTargets)mouse.Freeze();else mouse.Clear();}
    public void PressMouse(bool primary,Vector2 point,Vector2 mainSeed,Vector2 offSeed,float at,float mainAxis,float offAxis){if(Arena(point))mouse.Press(primary,point,mainSeed,offSeed,at,mainAxis,offAxis);}
    public void Poll(System.Func<Vector2,Vector2> canvas,Vector2 mainSeed,Vector2 offSeed,float mainPixels,float offPixels,float mainAxis,float offAxis) {
        DashPressed=false;
        if(Input.touchCount>0) {
            mouse.Clear();
            End(-1);End(-2);
            var present=new HashSet<int>();foreach(Touch touch in Input.touches)present.Add(touch.fingerId);
            var vanished=new List<int>();foreach(int id in owners.Keys)if(id>=0&&!present.Contains(id))vanished.Add(id);foreach(int id in vanished)End(id,-1,false);
            foreach(Touch touch in Input.touches) {
                Vector2 p=canvas(touch.position);
                if(touch.phase==TouchPhase.Began)Begin(touch.fingerId,p);
                else if(touch.phase==TouchPhase.Ended||touch.phase==TouchPhase.Canceled)End(touch.fingerId,-1,touch.phase==TouchPhase.Ended);
                else Move(touch.fingerId,p);
            }
        } else {
            var vanished=new List<int>();foreach(int id in owners.Keys)if(id>=0)vanished.Add(id);foreach(int id in vanished)End(id,-1,false);
            Vector2 p=canvas(Input.mousePosition);
            float now=Time.unscaledTime;
            if(reference){
                for(int b=0;b<2;b++){
                    int id=-1-b;var pad=b==0?right:left;
                    if(Input.GetMouseButtonDown(b)){
                        if(HitTest(p))Begin(id,p);else if(Arena(p)){referenceOrigin[b]=p;pad.Capture(p);}
                    }
                    if(!Input.GetMouseButton(b)){End(id);pad.captured=false;pad.value=Vector2.zero;}
                    else if(pad.captured)pad.value=Vector2.ClampMagnitude(new Vector2(p.x-referenceOrigin[b].x,referenceOrigin[b].y-p.y)/155f,1);
                }
                return;
            }
            for(int button=0;button<2;button++){
                int id=-1-button;
                if(Input.GetMouseButtonDown(button)){
                    if(HitTest(p))Begin(id,p);else PressMouse(button==0,p,mainSeed,offSeed,now,mainAxis,offAxis);
                }
                if(!Input.GetMouseButton(button)){End(id);mouse.Release(button==0,now);}
            }
            if(Input.GetMouseButtonDown(2))mouse.Freeze();
            mouse.Move(p,mainPixels,offPixels,now);
        }
    }
}
