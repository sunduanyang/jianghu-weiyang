using System;
using System.Collections.Generic;
using Jianghu;
using UnityEngine;
using UnityEngine.UI;

// Mounted PlayerTouchControls + DynamicJoystick/Joystick, APK ARM64
// 0x35135d8, 0x36c6654..0x36c69a0, 0x36cbb00..0x36cc4ac.
// The caller retains pointer capture, button hit testing and the PC mouse policy.
public sealed class OriginalInputAdapter {
    sealed class View {
        public J source;
        public RectTransform rect,background,handle;
        public Canvas canvas;
        public Camera eventCamera;
        public CanvasGroup group;
        public Vector2 input,centerCanvas,handlePosition,desktopAim,dragStartAim;
        public bool down,lockCenter,hasDesktopAim;
        public int pointer;
        public Vector2 Radius=>background!=null?background.sizeDelta*.5f:V(source["background_size"])*.5f;
    }
    readonly Dictionary<int,View> pointers=new Dictionary<int,View>();
    readonly View left=new View(),right=new View();
    J source;
    Transform hudRoot;
    Canvas rootCanvas;
    CanvasScaler scaler;
    HorizontalLayoutGroup layout;
    readonly List<RectTransform> bottomOffsets=new List<RectTransform>();
    bool loaded,pcMode,visible,fixedJoysticks,externalViewport;
    int width,height;
    float canvasScale=1;
    public OriginalInputAdapter(){}
    public OriginalInputAdapter(J originalSource){source=originalSource;}
    public bool PCMode {get{Ensure();return pcMode;}set{Ensure();if(pcMode!=value){pcMode=value;RefreshViewport(true);}}}
    public bool VisibleJoysticks {get{Ensure();return visible;}}
    public bool FixedJoysticks {get{Ensure();return fixedJoysticks;}}
    public float CanvasScale {get{Ensure();RefreshViewport(false);return canvasScale;}}
    public Vector2 ReferenceResolution {get{Ensure();return ReferenceFor(width>0?width:Screen.width,height>0?height:Screen.height);}}
    static Vector2 V(J d)=>new Vector2((float)d["x"].N,(float)d["y"].N);
    void Ensure() {
        if(loaded)return;
        if(source==null){var asset=Resources.Load<TextAsset>("OriginalInputSource");if(asset==null)throw new InvalidOperationException("OriginalInputSource manifest missing");source=J.Parse(asset.text);}
        left.source=source["views"]["screen_left"];right.source=source["views"]["screen_right"];
        pcMode=source["tablet_canvas"]["pc_mode"].B;visible=source["options"]["visible"].B;fixedJoysticks=source["options"]["fixed"].B;
        loaded=true;
    }
    // Optional binding to the imported original HUD. With source RectTransforms,
    // Unity performs the same camera projection and anchoring as Joystick.duo.
    // A null HUD still supports the original drag math for captured PC pointers.
    public void Bind(Transform originalHudRoot) {
        Ensure();Reset();hudRoot=originalHudRoot;
        scaler=Find<CanvasScaler>(source["canvas"]["scaler"]);rootCanvas=Find<Canvas>(source["canvas"]["source"]);
        layout=Find<HorizontalLayoutGroup>(source["tablet_canvas"]["horizontal_layout_group"]);
        bottomOffsets.Clear();foreach(var n in source["tablet_canvas"]["bottom_offset_elements"].A){var t=Find<RectTransform>(n);if(t!=null)bottomOffsets.Add(t);}
        BindView(left);BindView(right);RefreshViewport(true);
    }
    T Find<T>(J reference)where T:Component {
        if(hudRoot==null)return null;
        string path=reference["path"].S;int slash=path.IndexOf('/');
        Transform t=slash<0?hudRoot:hudRoot.Find(path.Substring(slash+1));
        return t==null?null:t.GetComponent<T>();
    }
    void BindView(View v) {
        v.rect=Find<RectTransform>(v.source["source"]);v.background=Find<RectTransform>(v.source["background"]);v.handle=Find<RectTransform>(v.source["handle"]);
        v.canvas=v.rect==null?null:v.rect.GetComponentInParent<Canvas>();v.eventCamera=null;v.group=v.rect==null?null:v.rect.GetComponent<CanvasGroup>();
        if(v.background!=null){v.background.pivot=new Vector2(.5f,.5f);v.background.gameObject.SetActive(false);}
        if(v.handle!=null){v.handle.anchorMin=v.handle.anchorMax=v.handle.pivot=new Vector2(.5f,.5f);v.handle.anchoredPosition=Vector2.zero;}
        if(v.group!=null)v.group.alpha=visible?1:0;
    }
    public void SetOptions(bool visibleJoysticks,bool fixedJoysticks) {
        Ensure();visible=visibleJoysticks;this.fixedJoysticks=fixedJoysticks;
        foreach(var v in new[]{left,right})if(v.group!=null)v.group.alpha=visible?1:0;
    }
    static bool NativeNarrowAspect(float width,float height,float threshold) {
        // ARM FDIV rounds to float32 before FCMP. Unity's Mono can keep the
        // direct C# division in wider precision (1280/720 becomes '<' instead
        // of equal). Materialize the actual float32 bits before comparison.
        float ratio=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(width/height));
        return ratio<threshold;
    }
    Vector2 ReferenceFor(float w,float h) {
        J rules=source["tablet_canvas"];
        float x=pcMode?(float)rules["pc_width"].N:NativeNarrowAspect(w,h,(float)rules["aspect_threshold"].N)?(float)rules["tablet_width"].N:(float)rules["standard_width"].N;
        return new Vector2(x,(float)source["canvas"]["reference_resolution"]["y"].N);
    }
    // TabletModeCanvas.Start/Update/dmp, 0x36aef4c/0x36af100/0x36aefb8.
    // PC mode is an explicit original setting; Windows does not select it.
    public void UpdateViewport(){Ensure();RefreshViewport(false);}
    public void SetViewport(int pixelWidth,int pixelHeight){Ensure();externalViewport=true;ApplyViewport(pixelWidth,pixelHeight,true);}
    public void UseScreenViewport(){Ensure();externalViewport=false;RefreshViewport(true);}
    void RefreshViewport(bool force){if(externalViewport)ApplyViewport(width,height,force);else ApplyViewport(Screen.width,Screen.height,force);}
    void ApplyViewport(int w,int h,bool force) {
        if(w<=0||h<=0)throw new ArgumentOutOfRangeException("viewport");
        if(!force&&width==w&&height==h)return;width=w;height=h;
        Vector2 reference=ReferenceFor(w,h);int mode=source["canvas"]["screen_match_mode"].I;
        if(mode==1)canvasScale=Mathf.Min(w/reference.x,h/reference.y);
        else if(mode==2)canvasScale=Mathf.Max(w/reference.x,h/reference.y);
        else {float match=(float)source["canvas"]["match_width_or_height"].N;canvasScale=Mathf.Pow(2,Mathf.Lerp(Mathf.Log(w/reference.x,2),Mathf.Log(h/reference.y,2),match));}
        if(scaler!=null){scaler.uiScaleMode=(CanvasScaler.ScaleMode)source["canvas"]["scale_mode"].I;scaler.screenMatchMode=(CanvasScaler.ScreenMatchMode)mode;scaler.referenceResolution=reference;}
        if(rootCanvas!=null)rootCanvas.scaleFactor=canvasScale;
        J rules=source["tablet_canvas"],padding=rules[pcMode?"pc_padding":NativeNarrowAspect(w,h,(float)rules["aspect_threshold"].N)?"tablet_padding":"standard_padding"];
        if(layout!=null)layout.padding=new RectOffset(padding["left"].I,padding["right"].I,padding["top"].I,padding["bottom"].I);
        foreach(var rect in bottomOffsets){var p=rect.anchoredPosition;p.y=(float)padding["offset_y"].N;rect.anchoredPosition=p;}
        if(rootCanvas!=null)Canvas.ForceUpdateCanvases();
    }
    public bool PointerDown(int pointerId,Vector2 screenPosition,bool screenLeft,bool lockCenter=false) {
        Ensure();RefreshViewport(false);View v=screenLeft?left:right;
        if(v.down||pointers.ContainsKey(pointerId))return false;
        v.down=true;v.lockCenter=lockCenter;v.pointer=pointerId;pointers.Add(pointerId,v);
        // Desktop gestures continue from the last aim. Physical capture is
        // released normally; the logical hand control stays held at this aim.
        if(!lockCenter){v.desktopAim=Vector2.zero;v.hasDesktopAim=false;}
        v.dragStartAim=lockCenter&&v.hasDesktopAim?v.desktopAim:Vector2.zero;
        // Source DynamicJoystick.OnPointerDown executes this twice. The base
        // OnPointerDown tail-calls OnDrag (0x36cbf8c), even on a zero drag.
        for(int pass=0;pass<2;pass++){SetCenter(v,screenPosition);if(v.background!=null)v.background.gameObject.SetActive(true);Drag(v,screenPosition);}
        return true;
    }
    public void PointerDrag(int pointerId,Vector2 screenPosition) {
        Ensure();RefreshViewport(false);if(pointers.TryGetValue(pointerId,out var v))Drag(v,screenPosition);
    }
    Camera EventCamera(View v)=>v.eventCamera;
    void SetCenter(View v,Vector2 p) {
        v.centerCanvas=new Vector2(p.x/canvasScale,p.y/canvasScale);
        if(v.rect==null||v.background==null)return;
        if(RectTransformUtility.ScreenPointToLocalPointInRectangle(v.rect,p,EventCamera(v),out var local)) {
            Vector2 pivotOffset=Vector2.Scale(v.rect.pivot,v.rect.sizeDelta);
            Vector2 anchorOffset=Vector2.Scale(v.background.anchorMax,v.rect.sizeDelta);
            v.background.anchoredPosition=local+pivotOffset-anchorOffset;
        } else v.background.anchoredPosition=Vector2.zero;
    }
    void Drag(View v,Vector2 p) {
        // Native OnDrag refreshes lah before projecting. duo on pointer-down
        // sees its previously stored value; the source's second pass then
        // recenters with the newly selected camera.
        v.eventCamera=v.canvas!=null&&v.canvas.renderMode==RenderMode.ScreenSpaceCamera?v.canvas.worldCamera:null;
        Vector2 radius=v.Radius;
        float scale=v.canvas!=null?v.canvas.scaleFactor:canvasScale;
        Vector2 center=v.background!=null?RectTransformUtility.WorldToScreenPoint(EventCamera(v),v.background.position):v.centerCanvas*scale;
        Vector2 input=new Vector2((p.x-center.x)/(radius.x*scale),(p.y-center.y)/(radius.y*scale));
        float range=(float)v.source["handle_range"].N;
        if(v.lockCenter&&range>0)input+=new Vector2(v.dragStartAim.x/(radius.x*range),v.dragStartAim.y/(radius.y*range));
        int axis=v.source["axis_options"].I;if(axis==2)input.x=0;else if(axis==1)input.y=0;
        float magnitude=input.magnitude;Vector2 normalized=input.normalized;
        float threshold=fixedJoysticks?(float)source["options"]["fixed_threshold"].N:(float)v.source["move_threshold"].N;
        // Retain the source moving center for touch input. Desktop captures
        // lock this center until release and only clamp the handle to its rim.
        if(!v.lockCenter&&magnitude>threshold) {
            Vector2 offset=Vector2.Scale(normalized*(magnitude-threshold),radius);
            if(v.background!=null)v.background.anchoredPosition+=offset;else v.centerCanvas+=offset;
        }
        if(magnitude<=(float)v.source["dead_zone"].N)input=Vector2.zero;else if(magnitude>1)input=normalized;
        v.input=input;v.handlePosition=Vector2.Scale(input,radius)*(float)v.source["handle_range"].N;
        if(v.lockCenter){v.desktopAim=v.handlePosition;v.hasDesktopAim=true;}
        if(v.handle!=null)v.handle.anchoredPosition=v.handlePosition;
        // snapX/snapY apply only to Horizontal/Vertical/Direction getters.
        // PlayerInputHandler reads the handle Transform.localPosition instead.
    }
    public void PointerUp(int pointerId) {
        if(!pointers.TryGetValue(pointerId,out var v))return;
        if(v.background!=null)v.background.gameObject.SetActive(false);
        v.input=v.handlePosition=Vector2.zero;if(v.handle!=null)v.handle.anchoredPosition=Vector2.zero;
        v.down=v.lockCenter=false;pointers.Remove(pointerId);
    }
    public void Reset(bool clearDesktopPose=true) {
        PointerUp(left.pointer);PointerUp(right.pointer);pointers.Clear();
        if(clearDesktopPose)foreach(var v in new[]{left,right}){v.desktopAim=v.dragStartAim=Vector2.zero;v.hasDesktopAim=false;}
    }
    public bool IsDown(bool screenLeft)=>(screenLeft?left:right).down;
    public Vector2 GetCenterScreen(bool screenLeft) {
        Ensure();var v=screenLeft?left:right;
        return v.background!=null?RectTransformUtility.WorldToScreenPoint(EventCamera(v),v.background.position):v.centerCanvas*canvasScale;
    }
    public Vector2 GetNub(bool screenLeft){Ensure();var v=screenLeft?left:right;return v.handle!=null?(Vector2)v.handle.localPosition:v.handlePosition;}
    public Vector2 GetNetworkNub(bool screenLeft)=>QuantizeNub(GetNub(screenLeft),(float)source["network"]["maximum_magnitude"].N);
    // GetNetworkInput encodes at 1/1024 using half-away-from-zero rounding;
    // FixedUpdateNetwork decodes then cwh checks finite and clamps to 155.
    public static Vector2 QuantizeNub(Vector2 p,float maximumMagnitude=155) {
        if(!float.IsFinite(p.x)||!float.IsFinite(p.y))return Vector2.zero;
        p.x=(int)(p.x*1024+(p.x<0?-.5f:.5f))/1024f;p.y=(int)(p.y*1024+(p.y<0?-.5f:.5f))/1024f;
        return Vector2.ClampMagnitude(p,maximumMagnitude);
    }
    public void ReadOriginalFields(out Vector2 nubLeft,out Vector2 nubRight,out bool handDownLeft,out bool handDownRight) {
        Ensure();nubLeft=GetNetworkNub(false);nubRight=GetNetworkNub(true);handDownLeft=right.down;handDownRight=left.down;
    }
    public void ApplyToRig(OriginalRig rig,bool sustainDesktopHold=true) {
        ReadOriginalFields(out var nubLeft,out var nubRight,out var downLeft,out var downRight);
        if(sustainDesktopHold) {
            if(right.hasDesktopAim){nubLeft=QuantizeNub(right.desktopAim);downLeft=true;}
            if(left.hasDesktopAim){nubRight=QuantizeNub(left.desktopAim);downRight=true;}
        }
        // The rig method stores the first nub in native leftInput and crosses
        // active flags, matching 0x365ac48. Its old parameter names are cosmetic.
        rig.SetOriginalHandInputs(nubLeft,nubRight,downLeft,downRight);
        rig.SetDesktopHandPoseTargets(QuantizeNub(right.desktopAim),QuantizeNub(left.desktopAim),right.hasDesktopAim,left.hasDesktopAim);
    }
}
