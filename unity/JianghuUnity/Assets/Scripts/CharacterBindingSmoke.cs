using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Jianghu {
// Independent acceptance: inspect the meshes Unity actually draws, rather
// than treating correct joint anchors as proof that the artwork is rigid.
public sealed class CharacterBindingSmoke:MonoBehaviour {
    string output;GameClient client;JianghuApp app;
    readonly List<J> checks=new List<J>(),cases=new List<J>();
    readonly List<string> errors=new List<string>();
    sealed class Geometry {
        public Vector3[] points;public Vector2[] uv;public int[] triangles;public bool[] rigid;
    }
    sealed class Metrics {
        public float edge,area,shear,softMin=1;public int edges,triangles,flips,softTriangles,softFlips;public bool uv=true,topology=true,finite=true;
        public J Facts()=>J.O("rigid_edge_relative_error",edge,"rigid_area_relative_error",area,"foot_shear_cosine",shear,"rigid_edges_sampled",edges,"rigid_triangles_sampled",triangles,"rigid_triangle_flips",flips,"flexible_triangle_samples",softTriangles,"flexible_min_signed_area_ratio",softMin,"flexible_triangle_flips",softFlips,"uv_unchanged",uv,"topology_unchanged",topology,"finite",finite);
        public void Merge(Metrics b){edge=Mathf.Max(edge,b.edge);area=Mathf.Max(area,b.area);shear=Mathf.Max(shear,b.shear);softMin=Mathf.Min(softMin,b.softMin);edges+=b.edges;triangles+=b.triangles;flips+=b.flips;softTriangles+=b.softTriangles;softFlips+=b.softFlips;uv&=b.uv;topology&=b.topology;finite&=b.finite;}
        public bool Pass=>edges>0&&triangles>0&&edge<.001f&&area<.002f&&flips==0&&uv&&topology&&finite;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){
        if(GameClient.Arg("-characterBindingSmoke").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Character binding acceptance requires an isolated saveDir");Application.Quit(2);return;}
        new GameObject("Production character binding integrity acceptance").AddComponent<CharacterBindingSmoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string trace,LogType kind){if(kind==LogType.Error||kind==LogType.Exception)errors.Add(message);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??J.O()));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());}
    IEnumerator Start(){
        output=GameClient.Arg("-characterBindingSmoke");Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"renders"));
        // Step nested test coroutines here as well, so a fixture exception
        // still produces a failed JSON report instead of a hanging player.
        var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0){bool more=false;object value=null;try{more=stack.Peek().MoveNext();if(more)value=stack.Peek().Current;}catch(Exception e){errors.Add(e.ToString());stack.Clear();break;}if(!more){stack.Pop();continue;}if(value is IEnumerator nested&&!(value is CustomYieldInstruction))stack.Push(nested);else yield return value;}
        Check("no runtime errors",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));
        File.WriteAllText(Path.Combine(output,"binding-cases.json"),new J(cases).ToString());
        bool pass=checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"character-binding-native.json"),J.O("pass",pass,"scope",GameClient.Arg("-characterBindingScope")=="formal"?"shared character meshes and formal source combat":"shared character meshes, formal source combat and legacy non-reference prototype","checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"measurement_basis","actual Unity MeshFilter vertices, UVs and triangles; old affine reconstructed on identical inputs").ToString());Application.Quit(pass?0:1);
    }
    static GameObject RenderRoot(CharacterRenderer renderer)=>(GameObject)typeof(CharacterRenderer).GetField("root",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer);
    static CharacterRenderer CombatRenderer(PhysicsDuel.Fighter actor)=>(CharacterRenderer)typeof(PhysicsDuel.Fighter).GetField("characterRenderer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(actor);
    static CharacterPose CombatPose(PhysicsDuel.Fighter actor)=>(CharacterPose)typeof(PhysicsDuel.Fighter).GetMethod("CharacterModulePose",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(actor,null);
    static bool BoundedProbe(J probe)=>probe["piece_metrics"].A.Count>0&&probe["inverted_triangles"].I==0&&probe["minimum_triangle_area_ratio"].N>=.195&&probe["weight_error"].N<.00001&&probe["attachment_error_world"].N<.00002&&probe["piece_metrics"].A.All(p=>p["flexible_displacement_world"].N<=p["flexible_limit_world"].N+.00002&&p["flexible_blend"].N>=0&&p["flexible_blend"].N<=1);
    static J Definition(CharacterLook look){
        // Use the SAME source artwork as the actual renderer. Comparing a
        // Cast24 hand to the old generic hand's tip gives a false grip error.
        string resource=CharacterCastCatalog.ResourcePath(look.Art);
        return resource=="Characters"?CharacterRenderer.Catalog:J.Parse(Resources.Load<TextAsset>(resource+"/index").text);
    }
    static Dictionary<string,Geometry> Read(CharacterRenderer renderer,CharacterLook look){
        // Classify the actual post-calibration weights, including custom
        // skirt pieces. Names such as "pelvis" do not define rigid regions.
        var masks=new Dictionary<Mesh,bool[]>();
        var pieces=new List<object>();foreach(var piece in (IEnumerable)typeof(CharacterRenderer).GetField("body",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer))pieces.Add(piece);
        pieces.Add(typeof(CharacterRenderer).GetField("head",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(renderer));
        foreach(var piece in pieces){var type=piece.GetType();var mesh=(Mesh)type.GetField("mesh").GetValue(piece);var weights=(float[][])type.GetField("weights").GetValue(piece);var bones=(string[])type.GetField("bones").GetValue(piece);string primary=(string)type.GetField("primary").GetValue(piece);bool flexible=(bool)type.GetField("flexible").GetValue(piece);int main=Array.IndexOf(bones,primary);masks[mesh]=Enumerable.Range(0,mesh.vertexCount).Select(i=>!flexible||main>=0&&weights[i][main]>.99999f).ToArray();}
        var result=new Dictionary<string,Geometry>();
        foreach(var filter in RenderRoot(renderer).GetComponentsInChildren<MeshFilter>()){
            var mesh=filter.sharedMesh;string id=filter.gameObject.name;bool[] rigid=masks.TryGetValue(mesh,out var mask)?mask:Enumerable.Repeat(true,mesh.vertexCount).ToArray();
            if(!filter.GetComponent<MeshRenderer>().enabled)continue;
            result[id]=new Geometry{points=mesh.vertices,uv=mesh.uv,triangles=mesh.triangles,rigid=rigid};
        }return result;
    }
    static float Cross(Vector3 a,Vector3 b)=>a.x*b.y-a.y*b.x;
    static Metrics Compare(Dictionary<string,Geometry> baseline,Dictionary<string,Geometry> actual){
        var result=new Metrics();
        foreach(var pair in baseline){
            if(!actual.TryGetValue(pair.Key,out var now)){result.topology=false;continue;}
            var before=pair.Value;result.uv&=before.uv.SequenceEqual(now.uv);result.topology&=before.triangles.SequenceEqual(now.triangles)&&before.points.Length==now.points.Length;
            result.finite&=now.points.All(v=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsInfinity(v.x)&&!float.IsInfinity(v.y));
            if(before.points.Length!=now.points.Length)continue;
            var used=new HashSet<long>();
            for(int t=0;t<before.triangles.Length;t+=3){
                int a=before.triangles[t],b=before.triangles[t+1],c=before.triangles[t+2];
                foreach(var edge in new[]{new[]{a,b},new[]{b,c},new[]{c,a}}){
                    int i=edge[0],j=edge[1];if(!before.rigid[i]||!before.rigid[j])continue;
                    long key=((long)Math.Min(i,j)<<32)+(uint)Math.Max(i,j);if(!used.Add(key))continue;
                    float length=(before.points[i]-before.points[j]).magnitude;if(length<1e-6f)continue;
                    result.edge=Mathf.Max(result.edge,Mathf.Abs((now.points[i]-now.points[j]).magnitude/length-1));result.edges++;
                }
                Vector3 ba=before.points[b]-before.points[a],ca=before.points[c]-before.points[a],nb=now.points[b]-now.points[a],nc=now.points[c]-now.points[a];
                float old=Cross(ba,ca),current=Cross(nb,nc);if(Mathf.Abs(old)<1e-9f)continue;
                if(!before.rigid[a]||!before.rigid[b]||!before.rigid[c]){result.softTriangles++;result.softMin=Mathf.Min(result.softMin,current/old);if(old*current<=0)result.softFlips++;continue;}
                result.area=Mathf.Max(result.area,Mathf.Abs(current/old-1));result.triangles++;if(old*current<=0)result.flips++;
                if(pair.Key.Contains("foot")){
                    // Deformation gradient from one actual triangle. A rigid
                    // transform has perpendicular, equal-length columns.
                    Vector3 x=(nb*ca.y-nc*ba.y)/old,y=(nc*ba.x-nb*ca.x)/old;
                    if(x.magnitude*y.magnitude>1e-9f)result.shear=Mathf.Max(result.shear,Mathf.Abs(Vector3.Dot(x,y)/(x.magnitude*y.magnitude)));
                }
            }
        }return result;
    }
    static J SourceFootShape(CharacterLook look,Dictionary<string,Geometry> actual){
        // A stable but already flattened bind pose passes temporal edge tests.
        // Compare each actual foot triangle directly with its source pixels:
        // F^T F must be a scalar identity, allowing rotation or reflection.
        var rows=new List<J>();double maxShear=0,maxRatio=1;int triangles=0;bool finite=true;
        foreach(var definition in Definition(look)["body_meshes"].A.Where(p=>p["part"]["bone"].S.StartsWith("foot"))){
            string id=definition["part"]["id"].S;if(!actual.TryGetValue(id,out var mesh)){finite=false;continue;}
            var source=definition["vertices"].A.Select(PhysicsDuel.Point).ToArray();double shear=0,ratio=1,minScale=double.PositiveInfinity,maxScale=0;int count=0;
            for(int t=0;t<mesh.triangles.Length;t+=3){
                int ia=mesh.triangles[t],ib=mesh.triangles[t+1],ic=mesh.triangles[t+2];Vector2 a=source[ib]-source[ia],b=source[ic]-source[ia];double det=(double)a.x*b.y-(double)a.y*b.x;if(Math.Abs(det)<1e-8)continue;
                Vector3 u=mesh.points[ib]-mesh.points[ia],v=mesh.points[ic]-mesh.points[ia];
                double xx=(u.x*b.y-v.x*a.y)/det,xy=(u.y*b.y-v.y*a.y)/det,yx=(v.x*a.x-u.x*b.x)/det,yy=(v.y*a.x-u.y*b.x)/det;
                double g00=xx*xx+xy*xy,g11=yx*yx+yy*yy,g01=xx*yx+xy*yy,trace=g00+g11,discriminant=Math.Sqrt((g00-g11)*(g00-g11)+4*g01*g01);
                double high=(trace+discriminant)*.5,low=(trace-discriminant)*.5;
                if(low<=0||double.IsNaN(low)||double.IsInfinity(high)){finite=false;ratio=1e9;continue;}
                shear=Math.Max(shear,Math.Abs(g01)/Math.Sqrt(g00*g11));ratio=Math.Max(ratio,Math.Sqrt(high/low));minScale=Math.Min(minScale,Math.Sqrt(low));maxScale=Math.Max(maxScale,Math.Sqrt(high));count++;
            }
            triangles+=count;maxShear=Math.Max(maxShear,shear);maxRatio=Math.Max(maxRatio,ratio);rows.Add(J.O("part",id,"triangles",count,"maximum_normalized_gram_off_diagonal",shear,"maximum_singular_value_ratio",ratio,"minimum_source_to_world_scale",double.IsInfinity(minScale)?0:minScale,"maximum_source_to_world_scale",maxScale));
        }
        bool pass=finite&&rows.Count==2&&triangles>=4&&maxShear<.001&&maxRatio<1.002;
        return J.O("pass",pass,"feet",new J(rows),"source_triangle_samples",triangles,"maximum_normalized_gram_off_diagonal",maxShear,"maximum_singular_value_ratio",maxRatio,"finite",finite,"shear_cosine_limit",.001,"singular_value_ratio_limit",1.002,"basis","source artwork vertices versus actual first-render mesh; uniform scale rotation and reflection allowed");
    }
    static CharacterPose Copy(CharacterPose source){var p=new CharacterPose{Left=source.Left,FootNear=source.FootNear,FootFar=source.FootFar};foreach(var item in source.Bones)p.Add(item.Key,item.Value.A,item.Value.B);p.Detached.UnionWith(source.Detached);return p;}
    static Vector2 Rotate(Vector2 v,float degrees){float a=degrees*Mathf.Deg2Rad;return new Vector2(v.x*Mathf.Cos(a)-v.y*Mathf.Sin(a),v.x*Mathf.Sin(a)+v.y*Mathf.Cos(a));}
    static void Segment(CharacterPose p,string id,Vector2 start,float degrees,float length){p.Add(id,start,start+Rotate(Vector2.up,degrees)*length);}
    static CharacterPose Stress(string name,bool left){
        var p=CharacterPose.Bind(left);float f=left?-1:1;
        if(name.StartsWith("walk"))return CharacterPose.Sample("walk",float.Parse(name.Substring(4),System.Globalization.CultureInfo.InvariantCulture),left);
        if(name=="raise-and-fold")foreach(string side in new[]{"Near","Far"}){
            var u=p.Bones["upper"+side];Segment(p,"upper"+side,u.A,-f*35,(u.B-u.A).magnitude);var a=p.Bones["upper"+side];
            Segment(p,"forearm"+side,a.B,f*110,(p.Bones["forearm"+side].B-p.Bones["forearm"+side].A).magnitude);
            Segment(p,"hand"+side,p.Bones["forearm"+side].B,f*110,.098234f);
        }
        if(name=="deep-bend"){
            Vector2 pivot=p.Bones["pelvis"].B;foreach(string id in p.Bones.Keys.Where(k=>!k.StartsWith("thigh")&&!k.StartsWith("shin")&&!k.StartsWith("foot")&&k!="pelvis").ToArray()){
                var b=p.Bones[id];p.Add(id,pivot+Rotate(b.A-pivot,f*62),pivot+Rotate(b.B-pivot,f*62));
            }
            foreach(string side in new[]{"Near","Far"}){var t=p.Bones["thigh"+side];Segment(p,"thigh"+side,t.A,f*120,.3929377f);Segment(p,"shin"+side,p.Bones["thigh"+side].B,f*225,.3939543f);}
            foreach(string side in new[]{"Near","Far"}){Vector2 old=side=="Near"?p.FootNear:p.FootFar,next=p.Bones["shin"+side].B;var foot=p.Bones["foot"+side];p.Add("foot"+side,foot.A+next-old,foot.B+next-old);if(side=="Near")p.FootNear=next;else p.FootFar=next;}
        }
        if(name=="high-kick"){
            Segment(p,"thighNear",p.Bones["thighNear"].A,-f*95,.3929377f);Segment(p,"shinNear",p.Bones["thighNear"].B,-f*50,.3939543f);
            Vector2 old=p.FootNear;p.FootNear=p.Bones["shinNear"].B;var foot=p.Bones["footNear"];p.Add("footNear",foot.A+p.FootNear-old,foot.B+p.FootNear-old);
        }
        if(name=="neck-turn"||name=="neck-limit"){var n=p.Bones["neck"];Segment(p,"neck",n.A,f*(name=="neck-limit"?2:30),.09f);Segment(p,"head",p.Bones["neck"].B,f*(name=="neck-limit"?42:85),.30f);}
        if(name=="physical-endpoint-drift"){
            // Intentional solver/endpoint mismatch. This is a visual stress
            // fixture, never a mutation of live gameplay bodies or rules.
            foreach(string key in p.Bones.Keys.Where(k=>k.StartsWith("upper")||k.StartsWith("forearm")||k.StartsWith("hand")||k.StartsWith("foot")).ToArray()){
                var b=p.Bones[key];p.Add(key,b.A,b.A+(b.B-b.A)*(key.EndsWith("Near")?1.4f:.7f));
            }
            p.FootNear+=new Vector2(f*.09f,.065f);p.FootFar+=new Vector2(-f*.075f,-.035f);
        }
        if(name.StartsWith("detach-")){
            string cut=name.Substring(7);p.Detached.Add(cut);var moved=new HashSet<string>{"head"};if(cut!="head")moved.Add("neck");if(cut=="torso"||cut=="belly"){moved.Add("torso");foreach(string key in p.Bones.Keys.Where(k=>k.StartsWith("upper")||k.StartsWith("forearm")||k.StartsWith("hand")))moved.Add(key);}if(cut=="belly")moved.Add("belly");
            foreach(string key in moved){var bone=p.Bones[key];Vector2 delta=new Vector2(f*.6f,.25f);p.Add(key,bone.A+delta,bone.B+delta);}
        }
        return p;
    }
    // The former production mapping, reconstructed mathematically on the
    // identical source samples; no old executable or rewritten art is used.
    static Dictionary<string,Geometry> OldAffine(CharacterLook look,CharacterPose pose){
        var result=new Dictionary<string,Geometry>();
        foreach(var d in Definition(look)["body_meshes"].A){
            var part=d["part"];string id=part["id"].S;if(id=="torso"||id=="pelvis")continue;
            string bone=part["bone"].S;Vector2 a=pose.Bones[bone].A,b=pose.Bones[bone].B,heel=a;
            if(bone.StartsWith("foot"))a=bone.EndsWith("Near")?pose.FootNear:pose.FootFar;
            a.y=-a.y;b.y=-b.y;heel.y=-heel.y;
            Vector2 pivot=PhysicsDuel.Point(part["pivot"]),tip=PhysicsDuel.Point(part["tip"]),h=PhysicsDuel.Point(part["heel_pivot"]);float width=(float)part["size"][0].N;
            if(pose.Left){pivot.x=width-pivot.x;tip.x=width-tip.x;h.x=width-h.x;}
            Vector2 s=tip-pivot,delta=b-a;var matrix=Matrix4x4.identity;
            if(part["heel_pivot"].Has){h-=pivot;Vector2 t=heel-a;float det=s.x*h.y-h.x*s.y;matrix.m00=(delta.x*h.y-t.x*s.y)/det;matrix.m01=(t.x*s.x-delta.x*h.x)/det;matrix.m10=(delta.y*h.y-t.y*s.y)/det;matrix.m11=(t.y*s.x-delta.y*h.x)/det;}
            else {float breadth=part["breadth"].Has?(float)part["breadth"].N:1;Vector2 q=new Vector2(-s.y,s.x),tq=new Vector2(-delta.y,delta.x)*breadth;float sq=s.sqrMagnitude;matrix.m00=(delta.x*s.x+tq.x*q.x)/sq;matrix.m01=(delta.x*s.y+tq.x*q.y)/sq;matrix.m10=(delta.y*s.x+tq.y*q.x)/sq;matrix.m11=(delta.y*s.y+tq.y*q.y)/sq;}
            matrix.m03=a.x-matrix.m00*pivot.x-matrix.m01*pivot.y;matrix.m13=a.y-matrix.m10*pivot.x-matrix.m11*pivot.y;
            var points=d["vertices"].A.Select(v=>{Vector2 source=PhysicsDuel.Point(v);if(pose.Left)source.x=width-source.x;Vector3 world=matrix.MultiplyPoint3x4(source);return new Vector3(world.x,-world.y,0);}).ToArray();
            result[id]=new Geometry{points=points,rigid=Enumerable.Repeat(true,points.Length).ToArray(),uv=d["uv"].A.Select(PhysicsDuel.Point).ToArray(),triangles=d["triangles"].A.Select(v=>v.I).ToArray()};
        }return result;
    }
    void SaveTexture(Texture texture,string name){
        var prior=RenderTexture.active;var temp=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32);Graphics.Blit(texture,temp);RenderTexture.active=temp;
        var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,pixels.width,pixels.height),0,0);pixels.Apply();RenderTexture.active=prior;RenderTexture.ReleaseTemporary(temp);File.WriteAllBytes(Path.Combine(output,"renders",name+".png"),pixels.EncodeToPNG());Destroy(pixels);
    }
    J AlphaSeams(CharacterRenderer renderer,CharacterPose pose,float scale=1){
        // Inspect the rasterized alpha, including texture cutouts. Geometric
        // anchors can coincide while the visible sleeve still has a crack.
        var texture=renderer.Texture;var previous=RenderTexture.active;RenderTexture.active=texture;
        var image=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();RenderTexture.active=previous;
        var alpha=image.GetPixels32();int width=image.width,height=image.height;Destroy(image);Rect frame=renderer.FrameRect;
        Func<Vector2,Vector2Int> pixel=p=>new Vector2Int(Mathf.RoundToInt((p.x*scale-frame.xMin)/frame.width*width),Mathf.RoundToInt((p.y*scale-frame.yMin)/frame.height*height));
        Func<int,int,bool> solid=(x,y)=>x>=0&&x<width&&y>=0&&y<height&&alpha[y*width+x].a>=48;
        int search=Mathf.Max(2,Mathf.CeilToInt(.045f*scale/frame.width*width));
        Func<Vector2Int,Vector2Int> nearest=p=>{float best=float.PositiveInfinity;var selected=new Vector2Int(-1,-1);for(int y=Mathf.Max(0,p.y-search);y<=Mathf.Min(height-1,p.y+search);y++)for(int x=Mathf.Max(0,p.x-search);x<=Mathf.Min(width-1,p.x+search);x++){float d=(x-p.x)*(x-p.x)+(y-p.y)*(y-p.y);if(d<best&&solid(x,y)){best=d;selected=new Vector2Int(x,y);}}return selected;};
        var rows=new List<J>();float maximum=0;int disconnected=0;
        var pairs=new List<string[]>();foreach(string side in new[]{"Near","Far"}){pairs.Add(new[]{"elbow"+side,"upper"+side,"forearm"+side});pairs.Add(new[]{"wrist"+side,"forearm"+side,"hand"+side});pairs.Add(new[]{"knee"+side,"thigh"+side,"shin"+side});}pairs.Add(new[]{"head-base","neck","head"});
        foreach(var pair in pairs){
            var parent=pose.Bones[pair[1]];var child=pose.Bones[pair[2]];Vector2 joint=(parent.B+child.A)*.5f;
            var centre=pixel(joint);var opaque=nearest(centre);float clearance=opaque.x<0?.1f:Vector2.Distance(centre,opaque)*frame.width/width/scale;maximum=Mathf.Max(maximum,clearance);
            var start=nearest(pixel(parent.B+(parent.A-parent.B).normalized*.055f));var target=nearest(pixel(child.A+(child.B-child.A).normalized*.055f));
            bool connected=false;int radius=Mathf.CeilToInt(.13f*scale/frame.width*width);int xmin=Mathf.Max(0,centre.x-radius),xmax=Mathf.Min(width-1,centre.x+radius),ymin=Mathf.Max(0,centre.y-radius),ymax=Mathf.Min(height-1,centre.y+radius);
            if(start.x>=xmin&&start.x<=xmax&&start.y>=ymin&&start.y<=ymax&&target.x>=xmin&&target.x<=xmax&&target.y>=ymin&&target.y<=ymax){
                var queue=new Queue<Vector2Int>();var visited=new HashSet<int>();queue.Enqueue(start);visited.Add(start.y*width+start.x);
                while(queue.Count>0&&!connected){var at=queue.Dequeue();if(at==target){connected=true;break;}for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int x=at.x+dx,y=at.y+dy,key=y*width+x;if(x<xmin||x>xmax||y<ymin||y>ymax||visited.Contains(key)||!solid(x,y))continue;visited.Add(key);queue.Enqueue(new Vector2Int(x,y));}}
            }
            if(!connected)disconnected++;rows.Add(J.O("joint",pair[0],"opaque_path_connected",connected,"centre_clearance_world",clearance,"physical_endpoint_gap_world",Vector2.Distance(parent.B,child.A)));
        }
        return J.O("alpha_threshold",48,"joints",new J(rows),"disconnected",disconnected,"maximum_clearance_world",maximum,"criterion","actual alpha component must connect opaque parent and child interior near the joint");
    }
    IEnumerator Picture(string name){for(int i=0;i<3;i++)yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);}
    IEnumerator GeometryCases(){
        var total=new Metrics();int appearances=0,poseCount=0;bool probePass=true;
        foreach(string kind in new[]{"adult-man","adult-woman","elder","child","monk","red-lotus"}){
            var look=new CharacterLook{Role=kind=="monk"?"m-monk":"m-youth-farmer",Sex=kind=="adult-woman"?1:0,Stage=kind=="child"?0:kind=="elder"?2:1,Width=kind=="adult-woman"?4:0,Art=kind=="red-lotus"?"red-lotus":""};
            using(var renderer=new CharacterRenderer(768,asset:look.Art))foreach(bool left in new[]{false,true}){
                renderer.Render(look,CharacterPose.Bind(left),draw:false);var baseline=Read(renderer,look);var oldBaseline=OldAffine(look,CharacterPose.Bind(left));
                var sourceShape=SourceFootShape(look,baseline);Check(kind+(left?" left":" right")+" initial foot mesh preserves source shape",sourceShape["pass"].B,J.O("actual",sourceShape,"old_affine",SourceFootShape(look,oldBaseline)));
                foreach(string action in new[]{"bind","walk0.125","walk0.375","walk0.625","walk0.875","raise-and-fold","deep-bend","high-kick","neck-limit","neck-turn","physical-endpoint-drift","detach-head","detach-neck","detach-belly","detach-torso"}){
                    var pose=Stress(action,left);bool picture=kind=="adult-man"||kind=="red-lotus";
                    renderer.Render(look,pose,draw:picture,fitAll:true);var actual=Read(renderer,look);var metrics=Compare(baseline,actual);total.Merge(metrics);poseCount++;
                    var old=Compare(oldBaseline,OldAffine(look,pose));var probe=renderer.Probe();probePass&=BoundedProbe(probe);var row=J.O("appearance",kind,"left",left,"pose",action,"artificial_over_limit_stress",action=="neck-turn"||action=="physical-endpoint-drift","new_mesh",metrics.Facts(),"old_affine",old.Facts(),"renderer",probe);cases.Add(row);
                    if(picture){SaveTexture(renderer.Texture,kind+(left?"-left-":"-right-")+action);if(action=="bind"||action=="raise-and-fold"||action.StartsWith("walk")||action=="neck-limit"||action=="deep-bend"){
                        var alpha=AlphaSeams(renderer,pose);row["rendered_alpha_seams"]=alpha;
                        Check("actual alpha seam coverage "+kind+(left?" left ":" right ")+action,alpha["disconnected"].I==0&&alpha["maximum_clearance_world"].N<.035,alpha);
                    }}
                    if(action.StartsWith("detach-")){
                        string key=baseline.Keys.First(k=>k.Contains("head and neck"));var before=baseline[key];var after=actual[key];Vector3 delta=new Vector3(left?-.6f:.6f,.25f,0);float error=0;
                        for(int i=0;i<before.points.Length;i++)error=Mathf.Max(error,Vector3.Distance(after.points[i]-before.points[i],delta));
                        Check(kind+(left?" left ":" right ")+action+" detached head follows its own rigid frame",error<.00002f,J.O("all_head_vertices",before.points.Length,"maximum_translation_error_world",error,"renderer",probe));
                    }
                    if(action=="physical-endpoint-drift")Check(kind+(left?" left":" right")+" same-input old/new deformation comparison",metrics.Pass&&old.edge>.15f&&old.area>.25f&&old.shear>.05f&&metrics.shear<.001f,row);
                    yield return null;
                }
                appearances++;
            }
        }
        Check("actual rigid meshes preserve edges areas UVs and triangle winding across all stress poses",total.Pass&&appearances==12,J.O("appearances_and_facings",appearances,"poses",poseCount,"measurements",total.Facts()));
        Check("actual flexible meshes avoid flipped or collapsed triangles across all stress poses",total.softTriangles>0&&total.softFlips==0&&total.softMin>.025f,total.Facts());
        Check("flexible displacement weights and every triangle remain within explicit production bounds",probePass);
    }
    IEnumerator Run(){
        Screen.SetResolution(1680,1000,FullScreenMode.Windowed);
        yield return GeometryCases();
        float deadline=Time.realtimeSinceStartup+120;while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has)break;yield return null;}
        Check("isolated production backend ready",client!=null&&client.Ready&&client.World.Has);if(client==null||!client.Ready||!client.World.Has)yield break;
        yield return client.Request("/api/ai/config",J.O("enabled",false),j=>{});
        yield return client.Request("/api/intervene",J.O("action","challenge","npc_id",1),j=>app.Martial=j);app.ShowBattleForSmoke();
        deadline=Time.realtimeSinceStartup+20;while(app.PhysicsArena==null&&Time.realtimeSinceStartup<deadline)yield return null;
        Check("formal production duel opens",app.PhysicsArena!=null);if(app.PhysicsArena==null)yield break;
        var arena=app.PhysicsArena;arena.PauseCampaign();yield return Picture("01-formal-bind");
        Check("formal production duel uses both recovered source rigs",arena.player.SourceRigActive&&arena.enemy.SourceRigActive,J.O("player_source",arena.player.SourceRigActive,"enemy_source",arena.enemy.SourceRigActive,"player_reference",arena.player.Reference,"enemy_reference",arena.enemy.Reference,"player_source_template",arena.player.SourceRigTemplate,"enemy_source_template",arena.enemy.SourceRigTemplate));
        var actors=new[]{arena.player,arena.enemy};var baselines=new Dictionary<string,Geometry>[2];var live=new Metrics();float gap=0,grip=0,visualGrip=0,weaponGrip=0;int samples=0;
        for(int i=0;i<2;i++){
            var renderer=CombatRenderer(actors[i]);Check("formal renderer exists "+i,renderer!=null,actors[i].CharacterRenderFacts());if(renderer==null)yield break;
            baselines[i]=Read(renderer,CharacterArt.Look(actors[i].CharacterAppearanceId));
            var sourceLook=CharacterArt.Look(actors[i].CharacterAppearanceId);var sourceShape=SourceFootShape(sourceLook,baselines[i]);Check("formal initial foot mesh preserves source shape "+i,sourceShape["pass"].B,J.O("actual",sourceShape,"old_affine",SourceFootShape(sourceLook,OldAffine(sourceLook,CombatPose(actors[i])))));
            SaveTexture(renderer.Texture,"formal-"+(i==0?"player":"enemy")+"-bind");
            var seams=AlphaSeams(renderer,CombatPose(actors[i]),actors[i].Reference?1:.5f);Check("formal rendered alpha joint coverage "+i,seams["disconnected"].I==0&&seams["maximum_clearance_world"].N<.035,seams);
        }
        string frozen=arena.CaptureCampaign().ToString();yield return Picture("02-paused-render");
        Check("rendering and readback leave paused physics unchanged",frozen==arena.CaptureCampaign().ToString());
        arena.player.testOverride=arena.enemy.testOverride=true;arena.RunCampaign(true);float start=arena.player.Hip.x;string firstPhysical=arena.CaptureCampaign().ToString();bool liveProbePass=true;J worstGrip=J.O();
        for(int frame=0;frame<72;frame++){
            arena.player.move=frame<36?.65f:-.65f;
            yield return new WaitForSecondsRealtime(1f/30);yield return new WaitForEndOfFrame();
            for(int i=0;i<2;i++){
                var actor=actors[i];var renderer=CombatRenderer(actor);var actual=Read(renderer,CharacterArt.Look(actor.CharacterAppearanceId));live.Merge(Compare(baselines[i],actual));
                liveProbePass&=BoundedProbe(renderer.Probe());
                gap=Mathf.Max(gap,actor.Reference?actor.ReferenceSkeletonGap():actor.MaxJointGap());float currentGrip=Mathf.Max(actor.PrimaryGripError,actor.SupportGripError);if(currentGrip>grip){grip=currentGrip;worstGrip=GripFacts(actor);}
                visualGrip=Mathf.Max(visualGrip,VisualHandTipError(actor,actual));samples++;
                if(actor.primaryGrip!=null&&actor.primaryGrip.enabled)weaponGrip=Mathf.Max(weaponGrip,Vector2.Distance(actor.limbs["handNear"].B,actor.sword.WorldPoint(actor.weapon.Local(Vector2.zero,actor.facing))));
            }
            if(frame==23||frame==47||frame==71)yield return Picture("03-motion-"+frame);
        }
        arena.player.move=0;arena.PauseCampaign();
        for(int i=0;i<2;i++)SaveTexture(CombatRenderer(actors[i]).Texture,"formal-"+(i==0?"player":"enemy")+"-motion");
        Check("formal live meshes stay rigid during actual physics in both facings",live.Pass&&samples>=144,J.O("samples",samples,"player_facing",arena.player.facing,"enemy_facing",arena.enemy.facing,"player_travel",arena.player.Hip.x-start,"mesh",live.Facts()));
        Check("formal mesh samples include changed physical poses",firstPhysical!=arena.CaptureCampaign().ToString()&&arena.Clock>0,J.O("elapsed_physics_seconds",arena.Clock,"player_travel",arena.player.Hip.x-start));
        Check("formal flexible seams stay bounded and never flip triangles during physics",liveProbePass);
        Check("formal skeleton weapon grips and drawn hand tips remain connected",gap<.2f&&grip<.03f&&visualGrip<.003f&&weaponGrip<.03f,J.O("maximum_physical_joint_gap_world",gap,"maximum_physical_grip_error_world",grip,"maximum_drawn_hand_tip_to_physical_tip_world",visualGrip,"maximum_visible_weapon_handle_to_hand_world",weaponGrip,"worst_grip",worstGrip));
        File.WriteAllText(Path.Combine(output,"source-grip-diagnostics.json"),worstGrip.ToString());
        File.WriteAllText(Path.Combine(output,"physical-frame.json"),arena.CaptureCampaign().ToString());
        arena.gameObject.SetActive(false);app.enabled=false;
        if(GameClient.Arg("-characterBindingScope")!="formal")yield return LegacyFixture(actors[0].CharacterAppearanceId,actors[1].CharacterAppearanceId);
    }
    static float VisualHandTipError(PhysicsDuel.Fighter actor,Dictionary<string,Geometry> meshes){
        float maximum=0;var look=CharacterArt.Look(actor.CharacterAppearanceId);var catalog=Definition(look);
        foreach(string side in new[]{"Near","Far"}){
            var d=catalog["body_meshes"].A.First(p=>p["part"]["bone"].S=="hand"+side);string id=d["part"]["id"].S;if(!meshes.TryGetValue(id,out var actual))continue;
            var source=d["vertices"].A.Select(PhysicsDuel.Point).ToArray();var tip=PhysicsDuel.Point(d["part"]["tip"]);
            for(int t=0;t<actual.triangles.Length;t+=3){
                int ia=actual.triangles[t],ib=actual.triangles[t+1],ic=actual.triangles[t+2];Vector2 a=source[ib]-source[ia],b=source[ic]-source[ia],q=tip-source[ia];float det=a.x*b.y-a.y*b.x;if(Mathf.Abs(det)<.0001f)continue;
                float u=(q.x*b.y-q.y*b.x)/det,v=(a.x*q.y-a.y*q.x)/det;Vector3 drawn=actual.points[ia]+u*(actual.points[ib]-actual.points[ia])+v*(actual.points[ic]-actual.points[ia]);
                float scale=actor.Reference?1:.5f;Vector2 world=(Vector2)drawn/scale+new Vector2(actor.Hip.x,0);maximum=Mathf.Max(maximum,Vector2.Distance(world,actor.limbs["hand"+side].B));break;
            }
        }return maximum;
    }
    static J GripFacts(PhysicsDuel.Fighter actor){
        var rows=new List<J>();J Point(Vector2 p)=>J.O("x",p.x,"y",p.y);
        foreach(var pair in new[]{new KeyValuePair<string,FixedJoint2D>("primary",actor.primaryGrip),new KeyValuePair<string,FixedJoint2D>("support",actor.supportJoint),new KeyValuePair<string,FixedJoint2D>("offhand",actor.offhandGrip)}){
            var j=pair.Value;if(j==null)continue;var a=j.attachedRigidbody;var b=j.connectedBody;
            Vector2 aa=a.GetRelativePoint(Vector2.Scale(j.anchor,(Vector2)a.transform.lossyScale)),bb=b!=null?b.GetRelativePoint(Vector2.Scale(j.connectedAnchor,(Vector2)b.transform.lossyScale)):j.connectedAnchor;
            Vector2 at=j.transform.TransformPoint(j.anchor),bt=b!=null?(Vector2)b.transform.TransformPoint(j.connectedAnchor):j.connectedAnchor;
            rows.Add(J.O("grip",pair.Key,"enabled",j.enabled,"own",a.name,"connected",b==null?"":b.name,"anchor",Point(j.anchor),"connected_anchor",Point(j.connectedAnchor),"scaled_physics_gap",Vector2.Distance(aa,bb),"transform_gap",Vector2.Distance(at,bt),"own_lossy_scale",Point(a.transform.lossyScale),"connected_lossy_scale",Point(b==null?Vector2.one:(Vector2)b.transform.lossyScale)));
        }
        return J.O("source_rig",actor.SourceRigActive,"source_template",actor.SourceRigTemplate,"weapon",actor.weapon.id,"two_handed",actor.weapon.TwoHanded,"shield",actor.HasShield,"primary_error",actor.PrimaryGripError,"support_error",actor.SupportGripError,"near_hand_tip",Point(actor.limbs["handNear"].B),"far_hand_tip",Point(actor.limbs["handFar"].B),"visible_weapon_grip",Point(actor.sword.WorldPoint(actor.weapon.Local(Vector2.zero,actor.facing))),"joints",new J(rows));
    }
    static J FighterSpec(DuelWeapon weapon){
        return J.O("name","Binding visual fixture","vigor",120,"vigor_max",120,"stamina",100,"damage_scale",1,"damage_reduction",0,"handling_scale",1,"shield",false,
            "weapon",J.O("id",weapon.id,"name",weapon.name,"mass",weapon.mass,"cost",weapon.cost,"handling",weapon.handling,"damage",weapon.damage,"pressure",weapon.pressure,"distance",weapon.distance));
    }
    IEnumerator LegacyFixture(string playerLook,string enemyLook){
        // This independent fixture uses the older, differently proportioned
        // skeleton and a two-handed grip. Its renderer is the production one.
        var scene=SceneManager.CreateScene("Legacy character binding fixture",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Legacy character binding fixture");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);var arena=root.AddComponent<PhysicsDuel>();
        var weapon=DuelWeapon.All.First(w=>w.id=="greatsword");
        arena.campaign=new PhysicsDuel.CampaignOptions{playerSkin=playerLook,enemySkin=enemyLook,battle=J.O("fighters",new J(new List<J>{FighterSpec(weapon),FighterSpec(weapon)}))};
        root.SetActive(true);arena.PauseCampaign();arena.player.testOverride=arena.enemy.testOverride=true;
        yield return Picture("04-legacy-two-hand-bind");
        Check("legacy fixture uses its own proportions and two-handed weapon",!arena.ReferenceCombat&&!arena.player.Reference&&arena.player.weapon.TwoHanded&&arena.player.supportJoint!=null,J.O("head_bone_length",arena.player.head.length,"weapon",arena.player.weapon.id));
        var actors=new[]{arena.player,arena.enemy};var baselines=new Dictionary<string,Geometry>[2];bool ready=true;
        for(int i=0;i<2;i++){var renderer=CombatRenderer(actors[i]);ready&=renderer!=null;if(renderer!=null){baselines[i]=Read(renderer,CharacterArt.Look(actors[i].CharacterAppearanceId));var sourceShape=SourceFootShape(CharacterArt.Look(actors[i].CharacterAppearanceId),baselines[i]);Check("legacy initial foot mesh preserves source shape "+i,sourceShape["pass"].B,sourceShape);}}
        Check("legacy uses production character meshes in both directions",ready);if(!ready){yield return SceneManager.UnloadSceneAsync(scene);yield break;}
        var metrics=new Metrics();float visualGrip=0,grip=0,jointGap=0;int samples=0;
        float startX=arena.player.Hip.x;string firstPhysical=arena.CaptureCampaign().ToString();arena.RunCampaign(true);
        for(int tick=0;tick<48;tick++){
            arena.player.move=tick<24?.55f:-.55f;yield return new WaitForSecondsRealtime(1f/30);yield return new WaitForEndOfFrame();
            for(int i=0;i<2;i++){var actor=actors[i];var actual=Read(CombatRenderer(actor),CharacterArt.Look(actor.CharacterAppearanceId));metrics.Merge(Compare(baselines[i],actual));visualGrip=Mathf.Max(visualGrip,VisualHandTipError(actor,actual));grip=Mathf.Max(grip,actor.PrimaryGripError,actor.SupportGripError);jointGap=Mathf.Max(jointGap,actor.MaxJointGap());samples++;}
        }
        arena.player.move=0;arena.PauseCampaign();yield return Picture("05-legacy-two-hand-motion");
        for(int i=0;i<2;i++)SaveTexture(CombatRenderer(actors[i]).Texture,"legacy-two-hand-"+(i==0?"player":"enemy")+"-motion");
        Check("legacy proportions preserve actual mesh rigidity during physics",metrics.Pass&&samples>=96,J.O("samples",samples,"mesh",metrics.Facts()));
        Check("legacy samples include changed physical poses",firstPhysical!=arena.CaptureCampaign().ToString()&&Mathf.Abs(arena.player.Hip.x-startX)>.005f,J.O("travel",arena.player.Hip.x-startX,"clock",arena.Clock));
        Check("legacy drawn hands track both physical grips",visualGrip<.003f&&grip<.2f,J.O("maximum_drawn_hand_tip_to_physical_tip_world",visualGrip,"maximum_physical_grip_error_world",grip,"maximum_physical_joint_gap_world",jointGap));
        File.WriteAllText(Path.Combine(output,"legacy-physical-frame.json"),arena.CaptureCampaign().ToString());
        yield return SceneManager.UnloadSceneAsync(scene);
    }
}
}
