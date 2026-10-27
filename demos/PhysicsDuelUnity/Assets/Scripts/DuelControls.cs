using System.Collections.Generic;
using UnityEngine;

// A floating stick is born at the press and keeps that origin until release.
// Mouse buttons select hands; touch uses the initial screen half.
public sealed class DuelControls {
    public sealed class Pad {
        public Vector2 center {get;private set;}
        public const float Radius=90,Deadzone=.10f;
        public bool captured;
        public Vector2 value;
        public bool HasDirection=>captured&&value.sqrMagnitude>0;
        public void Capture(Vector2 p){center=p;value=Vector2.zero;captured=true;}
        public void Move(Vector2 p){Vector2 d=Vector2.ClampMagnitude((p-center)/Radius,1);value=d.magnitude<Deadzone?Vector2.zero:new Vector2(d.x,-d.y);}
    }
    public readonly Pad left=new Pad(),right=new Pad();
    public readonly Rect back=new Rect(612,840,88,56),dash=new Rect(716,840,168,56),forward=new Rect(900,840,88,56);
    readonly Dictionary<int,int> owners=new Dictionary<int,int>();
    public bool DashPressed;
    public float MoveAxis {get {float axis=0;foreach(int owner in owners.Values){if(owner==3)axis--;if(owner==5)axis++;}return Mathf.Clamp(axis,-1,1);}}
    public bool HitTest(Vector2 p)=>back.Contains(p)||dash.Contains(p)||forward.Contains(p);
    public bool Arena(Vector2 p)=>p.x>=50&&p.x<=1550&&p.y>=165&&p.y<=810&&!(p.x>=1390&&p.y<=220)&&!(p.x<=210&&p.y<=220);
    public bool Begin(int id,Vector2 p) {
        if(owners.ContainsKey(id))return false;
        int control=back.Contains(p)?3:dash.Contains(p)?4:forward.Contains(p)?5:Arena(p)?id==-1?2:id==-2?1:p.x<800?1:2:0;
        if(control==0||control==1&&left.captured||control==2&&right.captured)return false;
        owners[id]=control;
        if(control<3)(control==1?left:right).Capture(p);
        if(control==4)DashPressed=true;
        return true;
    }
    public void Move(int id,Vector2 p){if(owners.TryGetValue(id,out int owner)&&owner<3)(owner==1?left:right).Move(p);}
    public void End(int id){if(!owners.TryGetValue(id,out int owner))return;owners.Remove(id);if(owner<3){Pad pad=owner==1?left:right;pad.captured=false;pad.value=Vector2.zero;}}
    public void ReleaseAll(){owners.Clear();left.captured=right.captured=false;left.value=right.value=Vector2.zero;DashPressed=false;}
    public void Poll(System.Func<Vector2,Vector2> canvas) {
        DashPressed=false;
        if(Input.touchCount>0) {
            End(-1);End(-2);
            var present=new HashSet<int>();foreach(Touch touch in Input.touches)present.Add(touch.fingerId);
            var vanished=new List<int>();foreach(int id in owners.Keys)if(id>=0&&!present.Contains(id))vanished.Add(id);foreach(int id in vanished)End(id);
            foreach(Touch touch in Input.touches) {
                Vector2 p=canvas(touch.position);
                if(touch.phase==TouchPhase.Began)Begin(touch.fingerId,p);
                else if(touch.phase==TouchPhase.Ended||touch.phase==TouchPhase.Canceled)End(touch.fingerId);
                else Move(touch.fingerId,p);
            }
        } else {
            var vanished=new List<int>();foreach(int id in owners.Keys)if(id>=0)vanished.Add(id);foreach(int id in vanished)End(id);
            Vector2 p=canvas(Input.mousePosition);
            for(int button=0;button<2;button++){int id=-1-button;if(Input.GetMouseButtonDown(button))Begin(id,p);if(Input.GetMouseButton(button))Move(id,p);else End(id);}
        }
    }
}
