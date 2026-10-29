using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Scripting;

namespace Jianghu {
// Render-only strain/cache/allocation acceptance. The optional saved-frame
// fixture restores the existing engine while paused, then copies exact A/B
// points to independent renderers; no physics rules or poses are adjusted.
public sealed class CharacterBindingRefinementSmoke:MonoBehaviour {
    string output;readonly List<J> checks=new List<J>(),samples=new List<J>();readonly List<string> errors=new List<string>();
    sealed class Reference {public string id;public Mesh mesh;public Vector3[] bind;public int[] triangles;public bool[] rigid;}
    sealed class Metrics {
        public double minimum=1,maximum=1,rigidError,step;public int triangles,flexible,flips;public bool finite=true,topology=true;
        public string worst="";
        public bool Pass=>triangles>0&&flexible>0&&minimum>=.647&&maximum<=1.353&&rigidError<.001&&flips==0&&finite&&topology;
        public J Facts()=>J.O("minimum_principal_stretch",minimum,"maximum_principal_stretch",maximum,"rigid_stretch_error",rigidError,"maximum_vertex_step_world",step,"triangles",triangles,"flexible_triangles",flexible,"inverted_triangles",flips,"finite",finite,"topology_unchanged",topology,"worst_triangle",worst);
        public void Merge(Metrics m){if(m.minimum<minimum||m.maximum>maximum)worst=m.worst;minimum=Math.Min(minimum,m.minimum);maximum=Math.Max(maximum,m.maximum);rigidError=Math.Max(rigidError,m.rigidError);step=Math.Max(step,m.step);triangles+=m.triangles;flexible+=m.flexible;flips+=m.flips;finite&=m.finite;topology&=m.topology;}
    }
    sealed class AllocationCounter {
        public bool threadSupported,heapSupported;public long threadCalibration,heapCalibration;
        public bool Supported=>threadSupported||heapSupported;
        public string Method=>threadSupported?"GC.GetAllocatedBytesForCurrentThread":"Profiler.GetMonoUsedSizeLong with GC disabled";
        public J Facts()=>J.O("method",Method,"supported",Supported,"calibration_object_bytes",65536,"thread_counter_calibration_delta",threadCalibration,"thread_counter_supported",threadSupported,"mono_heap_calibration_delta",heapCalibration,"mono_heap_supported",heapSupported,"scope",threadSupported?"current main thread; synchronous Renderer.Render calls only":"global managed used heap; synchronous Renderer.Render calls only; collection disabled only inside measured interval and restored in finally");
        public long Measure(Action run){
            if(threadSupported){long before=GC.GetAllocatedBytesForCurrentThread();run();return GC.GetAllocatedBytesForCurrentThread()-before;}
            // Unity Mono can expose a thread allocation API that always
            // returns zero. A calibrated managed-heap delta with GC paused
            // measures the retained allocations without accepting that zero.
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            var prior=GarbageCollector.GCMode;
            try{GarbageCollector.GCMode=GarbageCollector.Mode.Disabled;long before=Profiler.GetMonoUsedSizeLong();run();return Profiler.GetMonoUsedSizeLong()-before;}
            finally{GarbageCollector.GCMode=prior;}
        }
    }
    static AllocationCounter CalibrateAllocations(){
        var result=new AllocationCounter();long before=GC.GetAllocatedBytesForCurrentThread();var retained=new byte[65536];retained[0]=17;retained[retained.Length-1]=29;result.threadCalibration=GC.GetAllocatedBytesForCurrentThread()-before;GC.KeepAlive(retained);result.threadSupported=result.threadCalibration>=65536;
        if(!result.threadSupported){
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();var prior=GarbageCollector.GCMode;
            try{GarbageCollector.GCMode=GarbageCollector.Mode.Disabled;long used=Profiler.GetMonoUsedSizeLong();var heapRetained=new byte[65536];heapRetained[0]=31;heapRetained[heapRetained.Length-1]=43;result.heapCalibration=Profiler.GetMonoUsedSizeLong()-used;GC.KeepAlive(heapRetained);result.heapSupported=result.heapCalibration>=65536;}
            finally{GarbageCollector.GCMode=prior;}
        }
        return result;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(GameClient.Arg("-characterBindingRefinementSmoke").Length==0)return;if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Binding refinement acceptance requires an isolated saveDir");Application.Quit(2);return;}new GameObject("Render binding refinement acceptance").AddComponent<CharacterBindingRefinementSmoke>();}
    void OnEnable(){Application.logMessageReceived+=Log;}void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??J.O()));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());}
    IEnumerator Start(){
        output=GameClient.Arg("-characterBindingRefinementSmoke");Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"renders"));
        var stack=new Stack<IEnumerator>();stack.Push(Run());while(stack.Count>0){object value=null;bool more=false;try{more=stack.Peek().MoveNext();if(more)value=stack.Peek().Current;}catch(Exception e){errors.Add(e.ToString());stack.Clear();break;}if(!more){stack.Pop();continue;}if(value is IEnumerator nested&&!(value is CustomYieldInstruction))stack.Push(nested);else yield return value;}
        Check("no runtime errors",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));
        File.WriteAllText(Path.Combine(output,"refinement-samples.json"),new J(samples).ToString());bool pass=checks.All(c=>c["pass"].B);
        File.WriteAllText(Path.Combine(output,"character-binding-refinement-native.json"),J.O("pass",pass,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"physics_rules_modified",false,"measurement_basis","actual mesh triangles versus original calibrated world triangles; saved physics frames are restored paused and read without stepping").ToString());Application.Quit(pass?0:1);
    }
    static object Field(object item,string name)=>item.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(item);
    static List<Reference> References(CharacterRenderer renderer){
        var pieces=new List<object>();foreach(object p in (IEnumerable)Field(renderer,"body"))pieces.Add(p);pieces.Add(Field(renderer,"head"));
        return pieces.Select(p=>new Reference{id=((J)Field(p,"part"))["id"].S,mesh=(Mesh)Field(p,"mesh"),bind=((Vector3[])Field(p,"bind")).ToArray(),triangles=((Mesh)Field(p,"mesh")).triangles,rigid=((bool[])Field(p,"rigidTriangles")).ToArray()}).ToList();
    }
    static Dictionary<string,Vector3[]> Positions(List<Reference> parts,float scale=1)=>parts.ToDictionary(p=>p.id,p=>p.mesh.vertices.Select(v=>v/scale).ToArray());
    static Metrics Measure(List<Reference> parts,float scale,Dictionary<string,Vector3[]> prior=null){
        var result=new Metrics();foreach(var p in parts){var current=p.mesh.vertices;result.topology&=p.mesh.triangles.SequenceEqual(p.triangles)&&current.Length==p.bind.Length;
            for(int i=0;i<current.Length;i++){current[i]/=scale;var v=current[i];result.finite&=!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsInfinity(v.x)&&!float.IsInfinity(v.y);if(prior!=null&&prior.TryGetValue(p.id,out var previous))result.step=Math.Max(result.step,Vector3.Distance(v,previous[i]));}
            for(int i=0;i<p.triangles.Length;i+=3){int ia=p.triangles[i],ib=p.triangles[i+1],ic=p.triangles[i+2];Vector3 a=p.bind[ib]-p.bind[ia],b=p.bind[ic]-p.bind[ia],u=current[ib]-current[ia],v=current[ic]-current[ia];double det=(double)a.x*b.y-(double)a.y*b.x;if(Math.Abs(det)<1e-10)continue;
                double xx=(u.x*b.y-v.x*a.y)/det,xy=(u.y*b.y-v.y*a.y)/det,yx=(v.x*a.x-u.x*b.x)/det,yy=(v.y*a.x-u.y*b.x)/det,g00=xx*xx+xy*xy,g11=yx*yx+yy*yy,g01=xx*yx+xy*yy,disc=Math.Sqrt((g00-g11)*(g00-g11)+4*g01*g01),low=Math.Sqrt(Math.Max(0,(g00+g11-disc)*.5)),high=Math.Sqrt(Math.Max(0,(g00+g11+disc)*.5));
                if(low<result.minimum||high>result.maximum)result.worst=p.id+":"+(i/3);result.minimum=Math.Min(result.minimum,low);result.maximum=Math.Max(result.maximum,high);result.triangles++;
                if(((double)u.x*v.y-(double)u.y*v.x)*det<=0)result.flips++;if(p.rigid[i/3])result.rigidError=Math.Max(result.rigidError,Math.Max(Math.Abs(low-1),Math.Abs(high-1)));else result.flexible++;
            }
        }return result;
    }
    static float Delta(Dictionary<string,Vector3[]> a,Dictionary<string,Vector3[]> b,Vector3 offset,string part=""){
        float maximum=0;foreach(var pair in a){if(part.Length>0&&pair.Key!=part)continue;if(!b.TryGetValue(pair.Key,out var now)||now.Length!=pair.Value.Length)return float.PositiveInfinity;for(int i=0;i<now.Length;i++)maximum=Mathf.Max(maximum,Vector3.Distance(now[i]-pair.Value[i],offset));}return maximum;
    }
    static Vector2 Rotate(Vector2 point,float degrees){float angle=degrees*Mathf.Deg2Rad;return new Vector2(point.x*Mathf.Cos(angle)-point.y*Mathf.Sin(angle),point.x*Mathf.Sin(angle)+point.y*Mathf.Cos(angle));}
    static void Segment(CharacterPose p,string name,Vector2 a,float angle,float length){p.Add(name,a,a+Rotate(Vector2.up,angle)*length);}
    static ulong Fingerprint(CharacterPose p){unchecked{ulong hash=1469598103934665603;void Add(float v){hash=(hash^(uint)v.GetHashCode())*1099511628211;}foreach(var pair in p.Bones){Add(pair.Value.A.x);Add(pair.Value.A.y);Add(pair.Value.B.x);Add(pair.Value.B.y);}Add(p.FootNear.x);Add(p.FootNear.y);Add(p.FootFar.x);Add(p.FootFar.y);hash^=p.Left?1UL:0UL;foreach(var name in p.Detached)hash=(hash^(uint)name.GetHashCode())*1099511628211;return hash;}}
    static CharacterPose Motion(CharacterPose bind,string mode,float angle){
        var p=bind.Snapshot();float f=p.Left?-1:1;float bend=mode=="combined"?Mathf.Sin(angle*Mathf.Deg2Rad)*75:angle;
        if(mode=="bend"||mode=="twist"||mode=="combined"){
            float belly=mode=="twist"?bend*.35f:bend,chest=bend;Segment(p,"belly",p.Bones["pelvis"].B,f*belly,.15f);Segment(p,"torso",p.Bones["belly"].B,f*chest,.20f);Segment(p,"neck",p.Bones["torso"].B,f*(mode=="twist"?-bend*.4f:bend),.09f);Segment(p,"head",p.Bones["neck"].B,f*(mode=="twist"?-bend*.6f:bend),.30f);
            Vector2 old=bind.Bones["torso"].B,next=p.Bones["torso"].B;foreach(string key in bind.Bones.Keys.Where(k=>k.StartsWith("upper")||k.StartsWith("forearm")||k.StartsWith("hand"))){var bone=bind.Bones[key];p.Add(key,next+Rotate(bone.A-old,f*chest),next+Rotate(bone.B-old,f*chest));}
        }
        if(mode=="head"||mode=="combined"){float head=mode=="combined"?Mathf.Sin(angle*Mathf.Deg2Rad*1.7f)*90:angle;Segment(p,"neck",p.Bones["torso"].B,f*head*.15f,.09f);Segment(p,"head",p.Bones["neck"].B,f*head,.30f);}
        if(mode=="skirt"||mode=="combined")foreach(string side in new[]{"Near","Far"}){
            float rotation=(side=="Near"?1:-1)*f*(mode=="combined"?Mathf.Sin(angle*Mathf.Deg2Rad*1.3f)*95:angle);var thigh=bind.Bones["thigh"+side];var shin=bind.Bones["shin"+side];Vector2 knee=thigh.A+Rotate(thigh.B-thigh.A,rotation),ankle=knee+Rotate(shin.B-shin.A,-rotation*.6f);p.Add("thigh"+side,thigh.A,knee);p.Add("shin"+side,knee,ankle);Vector2 old=side=="Near"?bind.FootNear:bind.FootFar;var foot=bind.Bones["foot"+side];p.Add("foot"+side,foot.A+ankle-old,foot.B+ankle-old);if(side=="Near")p.FootNear=ankle;else p.FootFar=ankle;
        }
        p.BindPose=bind;return p;
    }
    void SaveTexture(Texture source,string file){var prior=RenderTexture.active;var temp=RenderTexture.GetTemporary(source.width,source.height);Graphics.Blit(source,temp);RenderTexture.active=temp;var image=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();RenderTexture.active=prior;RenderTexture.ReleaseTemporary(temp);File.WriteAllBytes(Path.Combine(output,"renders",file+".png"),image.EncodeToPNG());Destroy(image);}
    IEnumerator Sweeps(){
        bool immutable=true;var all=new Metrics();int poses=0;
        foreach(string kind in new[]{"adult","woman","child","red-lotus"})foreach(bool left in new[]{false,true}){
            var look=new CharacterLook{Role="m-youth-farmer",Sex=kind=="woman"?1:0,Stage=kind=="child"?0:1,Width=2,Art=kind=="red-lotus"?kind:""};var bind=CharacterPose.Bind(left);float scale=look.DisplayScale;string label=kind+(left?"-left":"-right");
            using(var renderer=new CharacterRenderer(768,asset:look.Art)){
                var rest=bind.Snapshot();rest.BindPose=bind;renderer.Render(look,rest,scale,draw:false);var references=References(renderer);J probe=renderer.Probe();
                Check(label+" renderer exposes stretch bounds and calibration identity",probe["minimum_principal_stretch"].Has&&probe["maximum_principal_stretch"].Has&&Math.Abs(probe["principal_stretch_minimum_limit"].N-.65)<.00001&&Math.Abs(probe["principal_stretch_maximum_limit"].N-1.35)<.00001&&probe["rebind_count"].Has&&probe["profile_fingerprint"].Has&&probe["calibration_source"].Has,probe);
                foreach(string mode in new[]{"bend","twist","head","skirt","combined"}){
                    var group=new Metrics();Dictionary<string,Vector3[]> prior=null;
                    for(int angle=-100;angle<=100;angle+=2){var pose=Motion(bind,mode,angle);ulong before=Fingerprint(pose),beforeBind=Fingerprint(bind);bool picture=(kind=="adult"||kind=="red-lotus")&&(angle==-100||angle==0||angle==100);renderer.Render(look,pose,scale,fitAll:true,draw:picture);var measured=Measure(references,scale,prior);prior=Positions(references,scale);group.Merge(measured);immutable&=Fingerprint(pose)==before&&Fingerprint(bind)==beforeBind;poses++;
                        if(picture)SaveTexture(renderer.Texture,label+"-"+mode+"-"+angle);samples.Add(J.O("appearance",label,"sweep",mode,"degrees",angle,"actual_mesh",measured.Facts()));if(angle%20==0)yield return null;
                    }
                    all.Merge(group);Check(label+" "+mode+" continuous actual mesh strain",group.Pass&&group.step<.12,group.Facts());
                }
            }
        }
        Check("all continuous sweeps preserve original world triangle shapes within local strain bounds",all.Pass&&poses==4040,J.O("pose_samples",poses,"actual_mesh",all.Facts()));Check("rendering never changes input skeleton points or calibration",immutable);
    }
    static CharacterPose Scaled(CharacterPose source,float factor){var p=new CharacterPose{Left=source.Left,FootNear=source.FootNear*factor,FootFar=source.FootFar*factor};foreach(var pair in source.Bones)p.Add(pair.Key,pair.Value.A*factor,pair.Value.B*factor);p.Detached.UnionWith(source.Detached);return p;}
    void CalibrationAndAllocation(){
        var allocation=CalibrateAllocations();Check("managed allocation measurement detects a retained 64 KiB object",allocation.Supported,allocation.Facts());
        var look=new CharacterLook{Role="m-youth-farmer",Stage=1,Width=2};var bind=CharacterPose.Bind();using(var renderer=new CharacterRenderer(256)){
            var pose=bind.Snapshot();pose.BindPose=bind;long coldBytes=allocation.Measure(()=>renderer.Render(look,pose,draw:false));var first=References(renderer);var original=Positions(first);var firstProbe=renderer.Probe();int count=firstProbe["rebind_count"].I;samples.Add(J.O("benchmark","cold first Render on this renderer instance","managed_allocated_bytes",coldBytes,"includes_first_calibration",true,"measurement",allocation.Facts()));
            for(int i=0;i<24;i++){pose.BindPose=bind.Snapshot();renderer.Render(look,pose,draw:false);}var equivalent=renderer.Probe();Check("equivalent new BindPose objects do not recalibrate",firstProbe["rebind_count"].Has&&equivalent["rebind_count"].I==count,J.O("before",firstProbe,"after",equivalent));
            var larger=Scaled(bind,1.18f);var enlarged=larger.Snapshot();enlarged.BindPose=larger;renderer.Render(look,enlarged,draw:false);var changed=renderer.Probe();var resized=Positions(References(renderer));float sizeError=0;foreach(var part in original)for(int i=0;i<part.Value.Length;i++)sizeError=Mathf.Max(sizeError,Vector3.Distance(resized[part.Key][i],part.Value[i]*1.18f));
            float freshError;using(var fresh=new CharacterRenderer(256)){fresh.Render(look,enlarged,draw:false);freshError=Delta(resized,Positions(References(fresh)),Vector3.zero);}
            Check("changed explicit BindPose recalibrates the same appearance",changed["rebind_count"].Has&&changed["rebind_count"].I==count+1&&sizeError<.00004&&freshError<.00004,J.O("maximum_expected_scale_error_world",sizeError,"fresh_renderer_error_world",freshError,"before",firstProbe,"after",changed));
            var oldHead=larger.Bones["head"];larger.Add("head",oldHead.A,oldHead.B+Vector2.up*.025f);var mutated=larger.Snapshot();mutated.BindPose=larger;int beforeMutation=renderer.Probe()["rebind_count"].I;renderer.Render(look,mutated,draw:false);float mutationError;using(var fresh=new CharacterRenderer(256)){fresh.Render(look,mutated,draw:false);mutationError=Delta(Positions(References(renderer)),Positions(References(fresh)),Vector3.zero);}
            Check("in-place explicit BindPose edits invalidate the calibration fingerprint",renderer.Probe()["rebind_count"].Has&&renderer.Probe()["rebind_count"].I==beforeMutation+1&&mutationError<.00004,J.O("fresh_renderer_error_world",mutationError,"before_count",beforeMutation,"after",renderer.Probe()));larger.Add("head",oldHead.A,oldHead.B);renderer.Render(look,enlarged,draw:false);
            var reflected=larger.Snapshot(true);var left=reflected.Snapshot();left.BindPose=reflected;renderer.Render(look,left,draw:false);var mirrored=Positions(References(renderer));float mirrorError=0;foreach(var part in resized)for(int i=0;i<part.Value.Length;i++)mirrorError=Mathf.Max(mirrorError,Vector3.Distance(mirrored[part.Key][i],new Vector3(-part.Value[i].x,part.Value[i].y,0)));
            renderer.Render(look,enlarged,draw:false);float roundTrip=Delta(resized,Positions(References(renderer)),Vector3.zero);Check("left right round trip preserves calibrated vertices",mirrorError<.00004&&roundTrip<.00004,J.O("mirror_error_world",mirrorError,"round_trip_error_world",roundTrip,"probe",renderer.Probe()));
            pose.BindPose=bind;renderer.Render(look,pose,draw:false);float restored=Delta(original,Positions(References(renderer)),Vector3.zero);Check("returning to the original explicit BindPose restores original dimensions",restored<.00004,J.O("maximum_vertex_error_world",restored,"probe",renderer.Probe()));
            const int iterations=720;bool steady=allocation.Supported;double maximumFit=0,maximumFixed=0;var modes=new List<J>();
            foreach(bool facing in new[]{false,true}){
                var motionBind=bind.Snapshot(facing);var prepared=new CharacterPose[120];for(int i=0;i<prepared.Length;i++){prepared[i]=CharacterPose.Sample("walk",i/(float)prepared.Length,facing);prepared[i].BindPose=motionBind;}for(int i=0;i<240;i++)renderer.Render(look,prepared[i%prepared.Length],fitAll:true,draw:false);
                int beforeCount=renderer.Probe()["rebind_count"].I;long fitBytes=allocation.Measure(()=>{for(int i=0;i<iterations;i++)renderer.Render(look,prepared[i%prepared.Length],fitAll:true,draw:false);});double perFrame=fitBytes/(double)iterations;int afterCount=renderer.Probe()["rebind_count"].I;
                long fixedBytes=allocation.Measure(()=>{for(int i=0;i<iterations;i++)renderer.Render(look,prepared[i%prepared.Length],fitAll:false,draw:false);});double simplePerFrame=fixedBytes/(double)iterations;steady&=fitBytes>=0&&fixedBytes>=0&&perFrame<=256&&simplePerFrame<=256&&beforeCount==afterCount;
                maximumFit=Math.Max(maximumFit,perFrame);maximumFixed=Math.Max(maximumFixed,simplePerFrame);modes.Add(J.O("left",facing,"allocated_bytes_fit_all",fitBytes,"allocated_bytes_fixed_frame",fixedBytes,"bytes_per_frame_fit_all",perFrame,"bytes_per_frame_fixed_frame",simplePerFrame,"rebind_count_before",beforeCount,"rebind_count_after",afterCount));
            }
            Check("steady warmed Render draw false allocates at most 256 managed bytes per frame",steady,J.O("iterations_per_mode_and_facing",iterations,"facings",new J(modes),"bytes_per_frame_fit_all",maximumFit,"bytes_per_frame_fixed_frame",maximumFixed,"limit_bytes_per_frame",256,"warmup_frames_per_facing",240,"input_poses_preallocated",true,"draw",false,"measurement",allocation.Facts()));
        }
    }
    void Detached(){
        foreach(string art in new[]{"","red-lotus"})using(var renderer=new CharacterRenderer(256,asset:art)){
            var look=new CharacterLook{Role="m-youth-farmer",Stage=1,Width=2,Art=art};var bind=CharacterPose.Bind();var rest=bind.Snapshot();rest.BindPose=bind;renderer.Render(look,rest,draw:false);var references=References(renderer);var baseline=Positions(references);
            foreach(string cut in new[]{"head","neck","torso","belly","thighNear","thighFar"}){
                var pose=bind.Snapshot();pose.BindPose=bind;pose.Detached.Add(cut);var moved=new HashSet<string>();
                if(cut.StartsWith("thigh")){string side=cut.Substring(5);moved.UnionWith(new[]{cut,"shin"+side,"foot"+side});if(side=="Near")pose.FootNear+=new Vector2(.6f,.3f);else pose.FootFar+=new Vector2(.6f,.3f);}
                else {moved.Add("head");if(cut!="head")moved.Add("neck");if(cut=="torso"||cut=="belly"){moved.Add("torso");foreach(string key in bind.Bones.Keys.Where(k=>k.StartsWith("upper")||k.StartsWith("forearm")||k.StartsWith("hand")))moved.Add(key);}if(cut=="belly")moved.Add("belly");}
                foreach(string key in moved){var bone=pose.Bones[key];pose.Add(key,bone.A+new Vector2(.6f,.3f),bone.B+new Vector2(.6f,.3f));}ulong signature=Fingerprint(pose);renderer.Render(look,pose,draw:false);var now=Positions(references);float attachedError=0,unrelatedError=0;
                foreach(string part in baseline.Keys){bool head=part=="head-neck",chest=part=="torso",skirt=part=="pelvis"||part.Contains("skirt");if(head)attachedError=Mathf.Max(attachedError,Delta(baseline,now,cut.StartsWith("thigh")?Vector3.zero:new Vector3(.6f,.3f,0),part));if(chest&&(cut=="torso"||cut=="belly"))attachedError=Mathf.Max(attachedError,Delta(baseline,now,new Vector3(.6f,.3f,0),part));if(skirt)unrelatedError=Mathf.Max(unrelatedError,Delta(baseline,now,Vector3.zero,part));}
                var metrics=Measure(references,1);Check((art.Length==0?"standard":"red-lotus")+" detached "+cut+" does not pull unrelated painted pieces",attachedError<.00004&&unrelatedError<.00004&&metrics.Pass&&Fingerprint(pose)==signature,J.O("maximum_detached_piece_translation_error_world",attachedError,"maximum_unrelated_skirt_error_world",unrelatedError,"actual_mesh",metrics.Facts()));
            }
        }
    }
    static J PoseJson(CharacterPose p){var bones=J.O();foreach(var pair in p.Bones)bones[pair.Key]=J.O("a",new J(new List<J>{new J(pair.Value.A.x),new J(pair.Value.A.y)}),"b",new J(new List<J>{new J(pair.Value.B.x),new J(pair.Value.B.y)}));return J.O("left",p.Left,"bones",bones,"foot_near",new J(new List<J>{new J(p.FootNear.x),new J(p.FootNear.y)}),"foot_far",new J(new List<J>{new J(p.FootFar.x),new J(p.FootFar.y)}),"detached",new J(p.Detached.Select(s=>new J(s)).ToList()));}
    IEnumerator SavedFrameReplay(){
        GameClient client=null;JianghuApp app=null;float deadline=Time.realtimeSinceStartup+120;while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has&&app!=null)break;yield return null;}
        Check("isolated backend for read-only saved-frame capture is ready",client!=null&&client.Ready&&app!=null);if(client==null||!client.Ready||app==null)yield break;
        string workspace=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../.."));string path=GameClient.Arg("-bindingReplayFrame",Path.Combine(workspace,"output/character-proportions-20261029/binding-regression/physical-frame.json"));Check("historical exact physics frame is available",File.Exists(path),J.O("path",path));if(!File.Exists(path))yield break;
        string original=File.ReadAllText(path);yield return client.Request("/api/ai/config",J.O("enabled",false),j=>{});yield return client.Request("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);app.ShowBattleForSmoke();deadline=Time.realtimeSinceStartup+20;while(app.PhysicsArena==null&&Time.realtimeSinceStartup<deadline)yield return null;
        Check("paused existing engine fixture opens for exact pose capture",app.PhysicsArena!=null);if(app.PhysicsArena==null)yield break;var arena=app.PhysicsArena;arena.PauseCampaign();typeof(PhysicsDuel).GetMethod("RestoreCampaign",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(arena,new object[]{J.Parse(original)});arena.PauseCampaign();string before=arena.CaptureCampaign().ToString();var rows=new List<J>();
        foreach(var actor in new[]{arena.player,arena.enemy}){
            var pose=(CharacterPose)typeof(PhysicsDuel.Fighter).GetMethod("CharacterModulePose",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(actor,null);var bind=(CharacterPose)Field(actor,"characterBindingPose");pose.BindPose=bind;var look=CharacterArt.Look(actor.CharacterAppearanceId);float scale=actor.Reference?1:.5f;
            using(var renderer=new CharacterRenderer(768,asset:look.Art)){renderer.Render(look,pose,scale,fitAll:true);var references=References(renderer);var metrics=Measure(references,scale);ulong signature=Fingerprint(pose);for(int i=0;i<30;i++)renderer.Render(look,pose,scale,fitAll:true,draw:false);Check("exact saved physical pose renders within strain bounds "+rows.Count,metrics.Pass&&Fingerprint(pose)==signature,metrics.Facts());SaveTexture(renderer.Texture,"saved-exact-"+rows.Count);rows.Add(J.O("appearance",actor.CharacterAppearanceId,"display_scale",scale,"source_template",actor.SourceRigTemplate,"pose",PoseJson(pose),"bind_pose",PoseJson(bind),"actual_mesh",metrics.Facts()));}
        }
        string after=arena.CaptureCampaign().ToString();Check("exact replay does not step or change the restored physical frame",before==after&&File.ReadAllText(path)==original,J.O("captured_seconds",arena.Clock,"source_file_unchanged",File.ReadAllText(path)==original,"exact_physics_state_equal",before==after));File.WriteAllText(Path.Combine(output,"restored-physical-before.json"),before);File.WriteAllText(Path.Combine(output,"restored-physical-after.json"),after);File.WriteAllText(Path.Combine(output,"exact-replay-poses.json"),J.O("schema",1,"source_frame",path,"frames",new J(rows)).ToString());arena.PauseCampaign();
    }
    IEnumerator Run(){Screen.SetResolution(1680,1000,FullScreenMode.Windowed);yield return Sweeps();CalibrationAndAllocation();Detached();yield return SavedFrameReplay();}
}
}
