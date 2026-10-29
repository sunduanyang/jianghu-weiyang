using System.Linq;
using Jianghu;
using UnityEngine;

// CameraLensModifier 0x36c292c and the source cameras' PositionComposer
// configuration. Source phase bindings and custom blend curves are retained.
public sealed class OriginalCameraFraming : MonoBehaviour {
    public bool zoomOut;
    public Transform environmentTarget;
    Camera cameraComponent;
    OriginalPreview preview;
    OriginalRig followed;
    Transform target;
    J source,canvas,cameraRecord,lens,composer,blendSettings,defaultBlend;
    AnimationCurve blendCurve;
    float narrowLens,blendDuration,blendTime,fromFov;
    Vector3 pose,fromPosition;
    string cameraName;
    bool initialized;
    void Start() {
        cameraComponent=GetComponent<Camera>();source=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog").Data;
        canvas=source["scene"].A.First(x=>x["script"].S=="CanvasAndCamera");
        blendSettings=source["scene"].A.FirstOrDefault(x=>x["script"].S=="CinemachineBlenderSettings");
        defaultBlend=source["scene"].A.First(x=>x["script"].S=="CinemachineBrain")["data"]["DefaultBlend"];
        preview=FindFirstObjectByType<OriginalPreview>();cameraComponent.orthographic=false;
    }
    string PhaseCamera() {
        if(preview!=null&&preview.Paused)return "InGame_Camera_Paused";
        int state=preview==null?4:preview.OriginalState;
        for(int i=0;i<canvas["data"]["networkedPairs"].A.Count;i++)if(canvas["data"]["networkedPairs"][i]["state"].I==state) {
            string key=canvas["assets"]["networkedPairs["+i+"].cinemachineCamera"].S;
            string path=source["assets"][key]["scene_path"].S;return path.Substring(path.LastIndexOf('/')+1);
        }
        return "InGame_Camera_Round";
    }
    void SelectCamera(string name) {
        string path="CanvasAndCamera/CinemachineCameras/"+name;
        cameraRecord=source["scene"].A.First(x=>x["script"].S=="CinemachineCamera"&&x["scene_path"].S==path);
        lens=cameraRecord["data"]["Lens"];
        composer=source["scene"].A.First(x=>x["script"].S=="CinemachinePositionComposer"&&x["scene_path"].S==path)["data"];
        narrowLens=(float)source["scene"].A.First(x=>x["script"].S=="CameraLensModifier"&&x["scene_path"].S==path)["data"]["lensAtNarrow"].N;
        J selected=defaultBlend;
        if(blendSettings!=null)foreach(var b in blendSettings["data"]["CustomBlends"].A) {
            bool from=b["From"].S==cameraName||b["From"].S=="**ANY CAMERA**",to=b["To"].S==name||b["To"].S=="**ANY CAMERA**";
            if(from&&to)selected=b["Blend"];
        }
        blendDuration=initialized?(float)selected["Time"].N:0;blendTime=0;
        var keys=selected["CustomCurve"]["m_Curve"].A;
        blendCurve=keys.Count>0?new AnimationCurve(keys.Select(k=>new Keyframe((float)k["time"].N,(float)k["value"].N,(float)k["inSlope"].N,(float)k["outSlope"].N){weightedMode=(WeightedMode)k["weightedMode"].I,inWeight=(float)k["inWeight"].N,outWeight=(float)k["outWeight"].N}).ToArray()):AnimationCurve.EaseInOut(0,0,1,1);
        if(selected["Style"].I==0)blendDuration=0;
        fromPosition=transform.position;fromFov=cameraComponent.fieldOfView;cameraName=name;
        cameraComponent.nearClipPlane=(float)lens["NearClipPlane"].N;cameraComponent.farClipPlane=(float)lens["FarClipPlane"].N;
        initialized=false;
    }
    void LateUpdate() {
        if(source==null)return;
        if(followed==null){followed=FindObjectsByType<OriginalRig>(FindObjectsSortMode.None).FirstOrDefault(r=>r.human);if(followed==null)return;target=followed.transform.Find("CameraTarget");}
        string selected=PhaseCamera();if(selected!=cameraName)SelectCamera(selected);
        if(target==null)return;
        // CameraTargetPositioner activates when vitality loses support. It
        // follows the source chest's x and keeps the target's original y.
        if((followed.dead||followed.passedOut)&&followed.chest!=null)target.position=new Vector3(followed.chest.position.x,target.position.y,0);
        bool followsPlayer=cameraName=="InGame_Camera_Round"||cameraName=="InGame_Camera_Paused";
        Vector3 tracking=followsPlayer?target.position:environmentTarget!=null?environmentTarget.position:Vector3.zero;
        var offset=composer["TargetOffset"];Vector3 desired=tracking+new Vector3((float)offset["x"].N,(float)offset["y"].N,(float)offset["z"].N-(float)composer["CameraDistance"].N);
        if(!initialized){pose=desired;initialized=true;}
        var damping=composer["Damping"];
        for(int axis=0;axis<3;axis++){string key=axis==0?"x":axis==1?"y":"z";float time=(float)damping[key].N;pose[axis]+=time<.0001f?desired[axis]-pose[axis]:(desired[axis]-pose[axis])*(1-Mathf.Exp(-4.60517019f*Time.unscaledDeltaTime/time));}
        float aspect=(float)Screen.width/Screen.height,t=Mathf.Clamp01((aspect-1.77777779f)/-.444444418f);
        float fieldOfView=Mathf.Lerp((float)lens["FieldOfView"].N,narrowLens,t)+(zoomOut?10:0);
        blendTime+=Time.unscaledDeltaTime;float amount=blendDuration<=0?1:blendCurve.Evaluate(Mathf.Clamp01(blendTime/blendDuration));
        transform.position=Vector3.LerpUnclamped(fromPosition,pose,amount);cameraComponent.fieldOfView=Mathf.LerpUnclamped(fromFov,fieldOfView,amount);
    }
}
