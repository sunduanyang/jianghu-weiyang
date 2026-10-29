using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Jianghu {
// Acceptance only. Reads the production meshes and APIs; never updates a
// combat body, joint, rule, source picture, or character appearance record.
public sealed class CharacterCast24Smoke : MonoBehaviour {
    const string CatalogPath="Characters/Cast24/catalog";
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    string output;float expires;
    readonly List<J> checks=new List<J>(),frames=new List<J>(),apiRows=new List<J>();
    readonly List<string> errors=new List<string>();
    readonly List<Color32[]> tiles=new List<Color32[]>();
    sealed class Piece {
        public string id,bone,primary,texture;public J part,definition;
        public Vector2[] source,uv;public Vector3[] vertices;public int[] triangles;
        public float[][] weights;public string[] bones;public MeshRenderer renderer;
    }
    sealed class Raster {
        public Color32[] pixels;public int width,height,solid,clear,border,minX,minY,maxX,maxY;
        public bool Pass=>solid>128&&clear>pixels.Length/4&&border==0;
        public J Facts()=>J.O("width",width,"height",height,"visible_pixels",solid,"transparent_pixels",clear,"outer_two_pixel_border_visible",border,"alpha_bounds",new J(new List<J>{new J(minX),new J(minY),new J(maxX),new J(maxY)}));
    }
    sealed class Shape {
        public int pieces,triangles,edges;public float shear,ratio=1,scaleError,anchorError,sourceFit,edgeError,areaError,heelError,tipError;
        public bool finite=true,uv=true,topology=true,singleBone=true;
        public bool Pass=>pieces==17&&triangles>=34&&edges>=51&&finite&&uv&&topology&&singleBone&&shear<.002f&&ratio<1.004f&&scaleError<.003f&&anchorError<.0001f&&sourceFit<.0002f&&edgeError<.002f&&areaError<.004f;
        public J Facts()=>J.O("pieces",pieces,"source_triangles",triangles,"mesh_edges",edges,"source_shear_cosine",shear,"source_singular_value_ratio",ratio,"source_to_bone_scale_relative_error",scaleError,"primary_anchor_error_world",anchorError,"source_vertex_fit_error_world",sourceFit,"rigid_edge_relative_error",edgeError,"rigid_area_relative_error",areaError,"fixed_uv",uv,"fixed_topology",topology,"single_bone_weights",singleBone,"finite",finite,"foot_reference_heel_error_world",heelError,"foot_reference_tip_error_world",tipError,"foot_reference_errors_are_diagnostic",true);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){
        if(GameClient.Arg("-characterCast24Smoke").Length==0)return;
        if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Cast24 acceptance requires an isolated -saveDir");Application.Quit(2);return;}
        new GameObject("Production Cast24 native acceptance").AddComponent<CharacterCast24Smoke>();
    }
    void OnEnable(){Application.logMessageReceived+=Log;}
    void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??J.O()));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());}
    IEnumerator Start(){
        output=GameClient.Arg("-characterCast24Smoke");
        try{Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"renders"));}
        catch(Exception e){Debug.LogError("Cannot create Cast24 acceptance output: "+e.Message);Application.Quit(2);yield break;}
        expires=Time.realtimeSinceStartup+900;
        // Unity normally stops an iterator on an exception. Walk nested
        // iterators ourselves so every failure still leaves a report and exits.
        var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0){
            bool more=false;object value=null;
            try{if(Time.realtimeSinceStartup>expires)throw new TimeoutException("Cast24 acceptance exceeded 900 seconds");more=stack.Peek().MoveNext();if(more)value=stack.Peek().Current;}
            catch(Exception e){errors.Add(e.ToString());stack.Clear();break;}
            if(!more){stack.Pop();continue;}
            if(value is IEnumerator nested&&!(value is CustomYieldInstruction))stack.Push(nested);else yield return value;
        }
        Check("no runtime errors or unhandled acceptance exceptions",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));
        File.WriteAllText(Path.Combine(output,"cast24-frames.json"),new J(frames).ToString());
        File.WriteAllText(Path.Combine(output,"cast24-api.json"),new J(apiRows).ToString());
        bool pass=checks.Count>0&&checks.All(c=>c["pass"].B);
        File.WriteAllText(Path.Combine(output,"character-cast24-native.json"),J.O("pass",pass,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"measurement_basis","actual MeshFilter vertices UV topology and material textures versus authored source vertices and independently measured bone axes; raster alpha readback; production person and combat APIs","slider_gpu_readback_channel_tolerance_bytes",1,"slider_gpu_readback_channel_tolerance_normalized",1d/255,"slider_geometry_and_material_equality","exact; framebuffer equality is reported separately and allows one quantization level per channel","save_dir",GameClient.Arg("-saveDir"),"physics_rules_modified",false,"source_pixel_fidelity_checked_by_this_suite",false).ToString());
        Application.Quit(pass?0:1);
    }
    static object Field(object item,string name){if(item==null)throw new InvalidOperationException("Missing object for "+name);var f=item.GetType().GetField(name,Fields);if(f==null)throw new MissingFieldException(item.GetType().Name,name);return f.GetValue(item);}
    static GameObject Root(CharacterRenderer r)=>(GameObject)Field(r,"root");
    static bool Finite(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsInfinity(p.x)&&!float.IsInfinity(p.y);
    static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
    static Vector2 Turn(Vector2 p,float degrees){float a=degrees*Mathf.Deg2Rad;return new Vector2(p.x*Mathf.Cos(a)-p.y*Mathf.Sin(a),p.x*Mathf.Sin(a)+p.y*Mathf.Cos(a));}
    static Vector2 SourceVector(Vector2 p,bool left)=>new Vector2(left?-p.x:p.x,-p.y);
    static Vector2 Anchor(CharacterPose p,string bone){if(bone.StartsWith("foot",StringComparison.Ordinal))return bone.EndsWith("Near",StringComparison.Ordinal)?p.FootNear:p.FootFar;var b=p.Bones[bone];return bone=="pelvis"||bone=="torso"||bone=="neck"||bone.StartsWith("hand",StringComparison.Ordinal)?b.B:b.A;}
    static Vector2 MoveOnBone(Vector2 point,CharacterPose bind,CharacterPose current,string primary){var a=bind.Bones[primary];var b=current.Bones[primary];float angle=Vector2.SignedAngle(a.B-a.A,b.B-b.A);return Anchor(current,primary)+Turn(point-Anchor(bind,primary),angle);}
    static void Axis(J part,CharacterPose bind,out Vector2 pivot,out Vector2 axis){
        string bone=part["bone"].S;
        if(bone=="spine"){pivot=bind.Bones["pelvis"].B;axis=bind.Bones["torso"].B-pivot;}
        else if(bone=="pelvis"){pivot=bind.Bones[bone].B;axis=bind.Bones[bone].A-pivot;}
        else{var b=bind.Bones[bone];pivot=bone.StartsWith("foot",StringComparison.Ordinal)?(bone.EndsWith("Near",StringComparison.Ordinal)?bind.FootNear:bind.FootFar):b.A;axis=b.B-b.A;}
    }
    static Vector2 ExpectedPoint(Piece piece,Vector2 source,CharacterPose bind,CharacterPose pose,float scale){
        Axis(piece.part,bind,out var pivot,out var axis);
        Vector2 authoredPivot=PhysicsDuel.Point(piece.part["pivot"]),axisStart=piece.part["heel_pivot"].Has?PhysicsDuel.Point(piece.part["heel_pivot"]):authoredPivot;
        Vector2 sourceAxis=SourceVector(PhysicsDuel.Point(piece.part["tip"])-axisStart,bind.Left),delta=SourceVector(source-authoredPivot,bind.Left);
        float sourceLength=sourceAxis.magnitude;if(sourceLength<.001f||axis.magnitude<.000001f)throw new InvalidOperationException("Zero authored or physical bone axis: "+piece.id);
        Vector2 direction=sourceAxis/sourceLength,target=axis.normalized;
        Vector2 atBind=pivot+(target*Vector2.Dot(delta,direction)+new Vector2(-target.y,target.x)*Cross(direction,delta))*(axis.magnitude/sourceLength);
        return MoveOnBone(atBind,bind,pose,piece.primary)*scale;
    }
    static List<Piece> ReadPieces(CharacterRenderer renderer,J definition){
        var raw=new List<object>();foreach(var p in (IEnumerable)Field(renderer,"body"))raw.Add(p);raw.Add(Field(renderer,"head"));
        var filters=Root(renderer).GetComponentsInChildren<MeshFilter>(true);var result=new List<Piece>();
        for(int n=0;n<raw.Count;n++){
            var item=raw[n];bool isHead=n==raw.Count-1;J d=isHead?definition["heads"][0]:definition["body_meshes"][n];
            J part=isHead?J.O("id","head","bone","head","pivot",d["pivot"],"tip",d["tip"],"size",d["size"],"texture",d["texture"]):d["part"];
            var original=(Mesh)Field(item,"mesh");var filter=filters.Single(f=>f.sharedMesh==original);var mesh=filter.sharedMesh;
            Vector2[] source=(isHead?d["mesh"]["vertices"]:d["vertices"]).A.Select(PhysicsDuel.Point).ToArray();
            Vector2[] uv=isHead?source.Select(p=>new Vector2((float)d["uv"][0].N+p.x/(float)d["size"][0].N*(float)d["uv"][2].N,(float)d["uv"][1].N+(1-p.y/(float)d["size"][1].N)*(float)d["uv"][3].N)).ToArray():d["uv"].A.Select(PhysicsDuel.Point).ToArray();
            string expectedPrimary=d["primary_bone"].S;if(expectedPrimary.Length==0)expectedPrimary=part["primary_bone"].S;if(expectedPrimary.Length==0)expectedPrimary=isHead?"head":d["bones"][0].S;
            result.Add(new Piece{id=part["id"].S,bone=part["bone"].S,primary=expectedPrimary,texture=part["texture"].S,part=part,definition=d,source=source,uv=mesh.uv,vertices=mesh.vertices,triangles=mesh.triangles,weights=(float[][])Field(item,"weights"),bones=(string[])Field(item,"bones"),renderer=filter.GetComponent<MeshRenderer>()});
            if(!uv.SequenceEqual(mesh.uv))throw new InvalidOperationException("Actual mesh UV differs from source definition: "+part["id"].S);
            var authoredTriangles=(isHead?d["mesh"]["triangles"]:d["triangles"]).A.Select(v=>v.I).ToArray();
            if(!authoredTriangles.SequenceEqual(mesh.triangles))throw new InvalidOperationException("Actual mesh topology differs from source definition: "+part["id"].S);
        }
        return result;
    }
    static Shape Measure(List<Piece> actual,List<Piece> baseline,CharacterPose bind,CharacterPose pose,float scale){
        var total=new Shape();
        foreach(var piece in actual){
            total.pieces++;var before=baseline.First(p=>p.id==piece.id);
            total.uv&=before.uv.SequenceEqual(piece.uv);total.topology&=before.triangles.SequenceEqual(piece.triangles)&&before.vertices.Length==piece.vertices.Length;
            total.finite&=piece.vertices.All(Finite);total.singleBone&=piece.bones.Length==1&&piece.bones[0]==piece.primary&&piece.weights.Length==piece.source.Length&&piece.weights.All(w=>w.Length==1&&Mathf.Abs(w[0]-1)<.000001f)&&piece.renderer.enabled;
            if(piece.source.Length!=piece.vertices.Length)throw new InvalidOperationException("Source vertex count differs for "+piece.id);
            Axis(piece.part,bind,out var pivot,out var targetAxis);Vector2 axisStart=piece.part["heel_pivot"].Has?PhysicsDuel.Point(piece.part["heel_pivot"]):PhysicsDuel.Point(piece.part["pivot"]);
            float expectedScale=targetAxis.magnitude/Vector2.Distance(axisStart,PhysicsDuel.Point(piece.part["tip"]))*scale;
            for(int i=0;i<piece.vertices.Length;i++)total.sourceFit=Mathf.Max(total.sourceFit,Vector2.Distance(piece.vertices[i],ExpectedPoint(piece,piece.source[i],bind,pose,scale)));
            bool first=true;var used=new HashSet<long>();
            for(int t=0;t<piece.triangles.Length;t+=3){
                int ia=piece.triangles[t],ib=piece.triangles[t+1],ic=piece.triangles[t+2];
                Vector2 a=piece.source[ib]-piece.source[ia],b=piece.source[ic]-piece.source[ia],u=piece.vertices[ib]-piece.vertices[ia],v=piece.vertices[ic]-piece.vertices[ia];
                double det=(double)a.x*b.y-(double)a.y*b.x;if(Math.Abs(det)<1e-7)continue;
                double xx=(u.x*b.y-v.x*a.y)/det,xy=(u.y*b.y-v.y*a.y)/det,yx=(v.x*a.x-u.x*b.x)/det,yy=(v.y*a.x-u.y*b.x)/det;
                double g00=xx*xx+xy*xy,g11=yx*yx+yy*yy,g01=xx*yx+xy*yy,disc=Math.Sqrt((g00-g11)*(g00-g11)+4*g01*g01),low=(g00+g11-disc)*.5,high=(g00+g11+disc)*.5;
                if(low<=0||double.IsNaN(low)||double.IsInfinity(high)){total.finite=false;continue;}
                total.shear=Mathf.Max(total.shear,(float)(Math.Abs(g01)/Math.Sqrt(g00*g11)));total.ratio=Mathf.Max(total.ratio,(float)Math.Sqrt(high/low));total.scaleError=Mathf.Max(total.scaleError,Mathf.Abs((float)Math.Sqrt((g00+g11)*.5)/expectedScale-1));total.triangles++;
                if(first){
                    Vector2 Project(Vector2 q){q-=piece.source[ia];return (Vector2)piece.vertices[ia]+new Vector2((float)(xx*q.x+yx*q.y),(float)(xy*q.x+yy*q.y));}
                    Vector2 pin=PhysicsDuel.Point(piece.part["pivot"]);total.anchorError=Mathf.Max(total.anchorError,Vector2.Distance(Project(pin),ExpectedPoint(piece,pin,bind,pose,scale)));
                    if(piece.bone.StartsWith("foot",StringComparison.Ordinal)){
                        total.heelError=Mathf.Max(total.heelError,Vector2.Distance(Project(axisStart),MoveOnBone(bind.Bones[piece.bone].A,bind,pose,piece.primary)*scale));
                        total.tipError=Mathf.Max(total.tipError,Vector2.Distance(Project(PhysicsDuel.Point(piece.part["tip"])),MoveOnBone(bind.Bones[piece.bone].B,bind,pose,piece.primary)*scale));
                    }
                    first=false;
                }
                float oldArea=Cross((Vector2)(before.vertices[ib]-before.vertices[ia]),(Vector2)(before.vertices[ic]-before.vertices[ia]));
                if(Mathf.Abs(oldArea)>1e-10f)total.areaError=Mathf.Max(total.areaError,Mathf.Abs(Cross(u,v)/oldArea-1));
                foreach(var edge in new[]{new[]{ia,ib},new[]{ib,ic},new[]{ic,ia}}){int x=edge[0],y=edge[1];long key=((long)Math.Min(x,y)<<32)+(uint)Math.Max(x,y);if(!used.Add(key))continue;float length=(before.vertices[x]-before.vertices[y]).magnitude;if(length<1e-7f)continue;total.edgeError=Mathf.Max(total.edgeError,Mathf.Abs((piece.vertices[x]-piece.vertices[y]).magnitude/length-1));total.edges++;}
            }
        }
        return total;
    }
    Raster Read(Texture texture,string file="",int downsample=0,int alphaThreshold=32){
        int width=downsample>0?downsample:texture.width,height=downsample>0?downsample:texture.height;
        var prior=RenderTexture.active;var temp=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32);var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
        try{
            Graphics.Blit(texture,temp);RenderTexture.active=temp;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            if(file.Length>0)File.WriteAllBytes(Path.Combine(output,"renders",file+".png"),image.EncodeToPNG());
            var result=new Raster{pixels=image.GetPixels32(),width=width,height=height,minX=width,minY=height,maxX=-1,maxY=-1};
            for(int y=0;y<height;y++)for(int x=0;x<width;x++){
                int alpha=result.pixels[y*width+x].a;if(alpha==0)result.clear++;if(alpha<alphaThreshold)continue;
                result.solid++;result.minX=Math.Min(result.minX,x);result.minY=Math.Min(result.minY,y);result.maxX=Math.Max(result.maxX,x);result.maxY=Math.Max(result.maxY,y);
                if(x<2||y<2||x>=width-2||y>=height-2)result.border++;
            }
            return result;
        }finally{RenderTexture.active=prior;RenderTexture.ReleaseTemporary(temp);Destroy(image);}
    }
    J FootGround(CharacterRenderer renderer,List<Piece> pieces,float displayScale,string label,Color32[] neutralPixels){
        var renderers=Root(renderer).GetComponentsInChildren<MeshRenderer>(true);var enabled=renderers.Select(r=>r.enabled).ToArray();var feet=pieces.Where(p=>p.bone.StartsWith("foot",StringComparison.Ordinal)).ToArray();
        var camera=Root(renderer).GetComponentInChildren<Camera>();var originalTarget=camera.targetTexture;string originalComposition=Composition(renderer).ToString();
        var target=new RenderTexture(512,512,0,RenderTextureFormat.ARGB32){name="Cast24 isolated shoe sole acceptance"};target.Create();var prior=RenderTexture.active;var rows=new List<J>();float maximum=0;bool visible=true;
        try{
            foreach(var foot in feet){
                for(int i=0;i<renderers.Length;i++)renderers[i].enabled=renderers[i]==foot.renderer;
                renderer.RenderCrop(target,new Rect(0,0,1,1));var raster=Read(target,label+"-sole-"+foot.id,alphaThreshold:48);bool nonempty=raster.solid>0;visible&=nonempty;
                float sole=nonempty?(renderer.FrameRect.yMin+(raster.minY+.5f)*renderer.FrameRect.height/raster.height)/displayScale:999f;
                maximum=Mathf.Max(maximum,Mathf.Abs(sole));rows.Add(J.O("part",foot.id,"bone",foot.bone,"sole_y_world_unscaled",sole,"opaque_pixels",raster.solid,"lowest_visible_pixel_y",raster.minY,"alpha_threshold",48,"pixel_height_world_unscaled",renderer.FrameRect.height/raster.height/displayScale,"within_ground_tolerance",nonempty&&Mathf.Abs(sole)<.01f));
            }
        }finally{
            for(int i=0;i<renderers.Length;i++)renderers[i].enabled=enabled[i];RenderTexture.active=prior;target.Release();Destroy(target);
        }
        bool statesRestored=renderers.Select((r,i)=>r.enabled==enabled[i]).All(s=>s)&&camera.targetTexture==originalTarget&&Composition(renderer).ToString()==originalComposition;
        bool mainTextureUnchanged=neutralPixels!=null&&neutralPixels.SequenceEqual(Read(renderer.Texture).pixels);
        return J.O("pass",feet.Length==2&&visible&&maximum<.01f&&statesRestored&&mainTextureUnchanged,"feet",new J(rows),"feet_count",feet.Length,"maximum_absolute_sole_y_world_unscaled",maximum,"tolerance_world_unscaled",.01,"alpha_threshold",48,"display_scale",displayScale,"renderer_camera_target_and_composition_restored",statesRestored,"neutral_surface_pixels_unchanged",mainTextureUnchanged,"measurement","each actual shoe MeshRenderer rendered alone into a separate 512px target; minimum alpha>=48 pixel centre mapped through FrameRect; main character Texture is not rendered or overwritten");
    }
    static J PixelDifference(Color32[] before,Color32[] after,int width){
        if(before==null||after==null||before.Length!=after.Length)return J.O("exact",false,"same_pixel_count",false,"before_pixels",before==null?0:before.Length,"after_pixels",after==null?0:after.Length);
        int changed=0,visibleChanged=0,first=-1,maxR=0,maxG=0,maxB=0,maxA=0;long absoluteDifference=0;
        for(int i=0;i<before.Length;i++){
            var a=before[i];var b=after[i];int r=Math.Abs(a.r-b.r),g=Math.Abs(a.g-b.g),blue=Math.Abs(a.b-b.b),alpha=Math.Abs(a.a-b.a);
            maxR=Math.Max(maxR,r);maxG=Math.Max(maxG,g);maxB=Math.Max(maxB,blue);maxA=Math.Max(maxA,alpha);absoluteDifference+=(long)r+g+blue+alpha;
            if(r+g+blue+alpha==0)continue;changed++;if(a.a>0||b.a>0)visibleChanged++;if(first<0)first=i;
        }
        J Colour(Color32 c)=>J.O("r",c.r,"g",c.g,"b",c.b,"a",c.a);
        return J.O("exact",changed==0,"same_pixel_count",true,"pixels_compared",before.Length,"changed_pixels",changed,"changed_visible_pixels",visibleChanged,"changed_fully_transparent_pixels",changed-visibleChanged,"maximum_absolute_rgba_difference",J.O("r",maxR,"g",maxG,"b",maxB,"a",maxA),"maximum_absolute_channel_difference",Math.Max(Math.Max(maxR,maxG),Math.Max(maxB,maxA)),"total_absolute_channel_difference",absoluteDifference,"first_changed_pixel",first<0?J.O():J.O("index",first,"x",first%width,"y",first/width,"before",Colour(before[first]),"after",Colour(after[first])));
    }
    static bool WithinGpuQuantization(J difference)=>difference["same_pixel_count"].B&&difference["maximum_absolute_channel_difference"].I<=1;
    static int FloatBits(float value)=>BitConverter.ToInt32(BitConverter.GetBytes(value),0);
    static J VertexDifference(List<Piece> before,List<Piece> after){
        int changed=0,changedUV=0,vertices=0;float maximum=0;bool topology=before.Count==after.Count,triangles=true;
        foreach(var a in before){
            var b=after.FirstOrDefault(p=>p.id==a.id);if(b==null||a.vertices.Length!=b.vertices.Length||a.uv.Length!=b.uv.Length){topology=false;continue;}
            for(int i=0;i<a.vertices.Length;i++){var x=a.vertices[i];var y=b.vertices[i];float distance=Vector3.Distance(x,y);vertices++;if(FloatBits(x.x)!=FloatBits(y.x)||FloatBits(x.y)!=FloatBits(y.y)||FloatBits(x.z)!=FloatBits(y.z))changed++;maximum=Mathf.Max(maximum,distance);}
            for(int i=0;i<a.uv.Length;i++)if(FloatBits(a.uv[i].x)!=FloatBits(b.uv[i].x)||FloatBits(a.uv[i].y)!=FloatBits(b.uv[i].y))changedUV++;
            triangles&=a.triangles.SequenceEqual(b.triangles);
        }
        return J.O("exact_mesh",topology&&triangles&&changed==0&&changedUV==0,"same_vertex_counts",topology,"vertices_compared",vertices,"changed_vertices",changed,"changed_uv_vertices",changedUV,"same_triangle_indices",triangles,"maximum_vertex_difference_world",maximum,"float_comparison","exact IEEE-754 bits");
    }
    static J MaterialSnapshot(List<Piece> pieces){
        J Bits(params float[] values)=>new J(values.Select(v=>new J(FloatBits(v).ToString("x8"))).ToList());
        var materials=new List<J>();
        foreach(var piece in pieces){
            var material=piece.renderer.sharedMaterial;var shader=material.shader;var properties=new List<J>();
            for(int i=0;i<shader.GetPropertyCount();i++){
                string name=shader.GetPropertyName(i);var type=shader.GetPropertyType(i);var property=J.O("name",name,"type",type.ToString());
                if(type==UnityEngine.Rendering.ShaderPropertyType.Texture){var texture=material.GetTexture(name);var scale=material.GetTextureScale(name);var offset=material.GetTextureOffset(name);property["texture_entity_id"]=new J(texture==null?"none":texture.GetEntityId().ToString());property["texture_name"]=new J(texture==null?"":texture.name);property["scale_offset_float_bits"]=Bits(scale.x,scale.y,offset.x,offset.y);}
                else if(type==UnityEngine.Rendering.ShaderPropertyType.Color){var value=material.GetColor(name);property["float_bits"]=Bits(value.r,value.g,value.b,value.a);}
                else if(type==UnityEngine.Rendering.ShaderPropertyType.Vector){var value=material.GetVector(name);property["float_bits"]=Bits(value.x,value.y,value.z,value.w);}
                else if(type==UnityEngine.Rendering.ShaderPropertyType.Float||type==UnityEngine.Rendering.ShaderPropertyType.Range)property["float_bits"]=Bits(material.GetFloat(name));
                else property["integer_value"]=new J(material.GetInteger(name));
                properties.Add(property);
            }
            materials.Add(J.O("part",piece.id,"material_entity_id",material.GetEntityId().ToString(),"shader_entity_id",shader.GetEntityId().ToString(),"render_queue",material.renderQueue,"properties",new J(properties)));
        }
        return new J(materials);
    }
    static J Composition(CharacterRenderer renderer){var c=Root(renderer).GetComponentInChildren<Camera>();var f=renderer.FrameRect;return J.O("frame",J.O("x",f.x,"y",f.y,"width",f.width,"height",f.height),"camera_world",J.O("x",c.transform.position.x,"y",c.transform.position.y,"z",c.transform.position.z),"camera_orthographic_size",c.orthographicSize,"texture_width",renderer.Texture.width,"texture_height",renderer.Texture.height);}
    void SavePixels(Color32[] pixels,int width,int height,string file){
        var image=new Texture2D(width,height,TextureFormat.RGBA32,false);try{image.SetPixels32(pixels);image.Apply();File.WriteAllBytes(Path.Combine(output,"renders",file+".png"),image.EncodeToPNG());}finally{Destroy(image);}
    }
    static CharacterPose Fixture(string name,bool left){
        var p=CharacterPose.Sample("bind",0,left);float f=left?-1:1;
        if(name.StartsWith("walk-",StringComparison.Ordinal))p=CharacterPose.Sample("walk",int.Parse(name.Substring(5))/8f,left);
        if(name=="raised-arms")foreach(string side in new[]{"Near","Far"}){
            Vector2 shoulder=p.Bones["upper"+side].A;float angle=f*(side=="Near"?-145:-115);
            foreach(string bone in new[]{"upper"+side,"forearm"+side,"hand"+side}){var b=p.Bones[bone];p.Add(bone,shoulder+Turn(b.A-shoulder,angle),shoulder+Turn(b.B-shoulder,angle));}
        }
        if(name=="deep-bend"){
            Vector2 waist=p.Bones["pelvis"].B;
            foreach(string nameOfBone in p.Bones.Keys.Where(k=>k!="pelvis"&&!k.StartsWith("thigh",StringComparison.Ordinal)&&!k.StartsWith("shin",StringComparison.Ordinal)&&!k.StartsWith("foot",StringComparison.Ordinal)).ToArray()){
                var b=p.Bones[nameOfBone];p.Add(nameOfBone,waist+Turn(b.A-waist,f*65),waist+Turn(b.B-waist,f*65));
            }
        }
        if(name=="endpoint-drift")foreach(string bone in p.Bones.Keys.Where(k=>k.StartsWith("upper",StringComparison.Ordinal)||k.StartsWith("forearm",StringComparison.Ordinal)||k.StartsWith("hand",StringComparison.Ordinal)||k.StartsWith("foot",StringComparison.Ordinal)).ToArray()){
            var b=p.Bones[bone];p.Add(bone,b.A,b.A+(b.B-b.A)*(bone.EndsWith("Near",StringComparison.Ordinal)?1.35f:.72f));
        }
        p.BindPose=CharacterPose.Bind(left);return p;
    }
    static string ExplicitLook(J entry){string id=entry["id"].S;int sex=id.StartsWith("f-",StringComparison.Ordinal)?1:0,stage=id.Contains("child")?0:id.Contains("elder")?2:1;return CharacterLook.Prefix+id+"~"+id+"~0~0~0~2~0~0~0~0~0~"+sex+"~"+stage+"~0~"+entry["asset"].S;}
    void InspectCharacter(J entry){
        string id=entry["id"].S,path="Characters/Cast24/"+id,asset=entry["asset"].S;var text=Resources.Load<TextAsset>(path+"/index");
        Check(id+" resource definition exists",text!=null,J.O("resource",path+"/index"));if(text==null)return;J definition=J.Parse(text.text);
        var bodies=definition["body_meshes"].A;var textures=bodies.Select(d=>d["part"]["texture"].S).Concat(definition["heads"].A.Select(h=>h["texture"].S)).ToArray();
        var required=new[]{"pelvis","belly","torso","neck","upperNear","forearmNear","handNear","upperFar","forearmFar","handFar","thighNear","shinNear","footNear","thighFar","shinFar","footFar"};
        bool contract=definition["preserve_source_colour"].B&&definition["single_head"].B&&definition["literal_source_parts"].B&&bodies.Count==16&&definition["heads"].A.Count==1&&bodies.Select(d=>d["part"]["id"].S).Distinct().Count()==16&&required.All(b=>bodies.Any(d=>d["part"]["bone"].S==b))&&textures.Length==17&&textures.Distinct().Count()==17&&textures.All(t=>t.Length>0&&Resources.Load<Texture2D>(path+"/"+t)!=null);
        Check(id+" has seventeen independent colour-preserving single-head source resources",contract,J.O("body_meshes",bodies.Count,"heads",definition["heads"].A.Count,"independent_textures",textures.Distinct().Count(),"textures",new J(textures.Select(t=>new J(t)).ToList()),"definition_flags",J.O("preserve_source_colour",definition["preserve_source_colour"],"single_head",definition["single_head"],"literal_source_parts",definition["literal_source_parts"])));
        if(!contract)return;
        var look=CharacterArt.Look(ExplicitLook(entry));Check(id+" explicit appearance resolves the new asset",look.Art==asset,J.O("appearance",look.Id,"art",look.Art));
        var sample=J.O("sex",id.StartsWith("f-",StringComparison.Ordinal)?"女":"男","age_months",(id.Contains("child")?12:id.Contains("elder")?70:28)*12,"appearance",J.O("head",id,"version",2));
        string automatic=CharacterLook.ForPerson(id,sample),oldId=ExplicitLook(entry).Substring(0,ExplicitLook(entry).LastIndexOf('~'));
        Check(id+" person API and existing appearance IDs automatically select the same new asset",CharacterLook.Parse(automatic).Art==asset&&CharacterLook.Parse(oldId).Art==asset,J.O("for_person",automatic,"old_appearance",oldId,"expected_asset",asset));
        foreach(bool left in new[]{false,true})using(var renderer=new CharacterRenderer(768,asset:asset)){
            Check(id+" renderer reads its own directory "+left,(string)Field(renderer,"resourcePath")==path,J.O("actual",(string)Field(renderer,"resourcePath"),"expected",path));
            List<Piece> baseline=null;Color32[] neutralPixels=null;J neutralComposition=J.O(),neutralMaterials=J.O(),repeatPixels=J.O(),repeatGeometry=J.O(),repeatMaterials=J.O();bool allShape=true,allRaster=true,material=true;int samples=0;var sampleFacts=new List<J>();
            foreach(string poseName in new[]{"standing","walk-0","walk-1","walk-2","walk-3","walk-4","walk-5","walk-6","walk-7","raised-arms","deep-bend","endpoint-drift"}){
                var pose=Fixture(poseName,left);renderer.Render(look,pose,look.DisplayScale,fitAll:true);var actual=ReadPieces(renderer,definition);if(baseline==null)baseline=actual;
                var shape=Measure(actual,baseline,pose.BindPose,pose,look.DisplayScale);allShape&=shape.Pass;bool save=poseName=="standing"||poseName=="walk-2"||poseName=="raised-arms"||poseName=="deep-bend";
                string name=id+(left?"-left-":"-right-")+poseName;var raster=Read(renderer.Texture,save?name:"");allRaster&=raster.Pass;
                foreach(var piece in actual)material&=piece.renderer.sharedMaterial.mainTexture==Resources.Load<Texture2D>(path+"/"+piece.texture);
                frames.Add(J.O("character",id,"facing",left?"left":"right","pose",poseName,"shape",shape.Facts(),"raster",raster.Facts(),"saved",save));
                sampleFacts.Add(J.O("pose",poseName,"geometry_pass",shape.Pass,"raster_pass",raster.Pass));samples++;
                if(poseName=="standing"){
                    neutralPixels=raster.pixels;neutralComposition=Composition(renderer);neutralMaterials=MaterialSnapshot(actual);if(!left)tiles.Add(Read(renderer.Texture,downsample:256).pixels);
                    var soles=FootGround(renderer,actual,look.DisplayScale,id+(left?"-left":"-right"),neutralPixels);Check(id+" both actual shoe soles reach display ground "+left,soles["pass"].B,soles);
                    // Same renderer, original look, identical standing pose:
                    // this isolates framebuffer quantization from sliders.
                    renderer.Render(look,Fixture("standing",left),look.DisplayScale,fitAll:true);var repeat=Read(renderer.Texture);var repeatMeshes=ReadPieces(renderer,definition);
                    repeatPixels=PixelDifference(neutralPixels,repeat.pixels,repeat.width);repeatGeometry=VertexDifference(baseline,repeatMeshes);repeatMaterials=MaterialSnapshot(repeatMeshes);
                }
            }
            Check(id+" fixed UV single-bone pieces preserve source shape across poses "+left,allShape&&samples==12,J.O("samples",samples,"states",new J(sampleFacts)));
            Check(id+" full figures have transparent margins and never touch the frame "+left,allRaster);
            Check(id+" every actual rendered mesh uses its independent source texture "+left,material);
            Check(id+" head has full source scale "+left,Math.Abs(renderer.Probe()["head_visual_scale"].N-1)<.00001,renderer.Probe());
            var alternate=CharacterLook.Parse(ExplicitLook(entry));alternate.Skin=3;alternate.Hair=3;alternate.Palette=7;alternate.Width=4;alternate.Grey=5;alternate.Eyes=2;alternate.Brows=2;alternate.Lips=2;alternate.Outfit=1;
            renderer.Render(alternate,Fixture("standing",left),alternate.DisplayScale,fitAll:true);
            var alternateRaster=Read(renderer.Texture);var colourDifference=PixelDifference(neutralPixels,alternateRaster.pixels,alternateRaster.width);bool exact=colourDifference["exact"].B;
            var alternateMeshes=ReadPieces(renderer,definition);var alternateComposition=Composition(renderer);J freshDifference,freshGeometry,freshComposition;
            var geometryDifference=VertexDifference(baseline,alternateMeshes);var alternateMaterials=MaterialSnapshot(alternateMeshes);bool materialExact=neutralMaterials.ToString()==alternateMaterials.ToString(),repeatMaterialExact=neutralMaterials.ToString()==repeatMaterials.ToString();
            bool accepted=WithinGpuQuantization(colourDifference)&&geometryDifference["exact_mesh"].B&&materialExact&&WithinGpuQuantization(repeatPixels)&&repeatGeometry["exact_mesh"].B&&repeatMaterialExact;bool saveDiagnostic=!exact||!accepted;
            string diagnostic=id+(left?"-left":"-right")+"-slider-diagnostic";
            using(var fresh=new CharacterRenderer(768,asset:asset)){
                fresh.Render(alternate,Fixture("standing",left),alternate.DisplayScale,fitAll:true);var freshRaster=Read(fresh.Texture);
                freshDifference=PixelDifference(alternateRaster.pixels,freshRaster.pixels,freshRaster.width);freshGeometry=VertexDifference(alternateMeshes,ReadPieces(fresh,definition));freshComposition=Composition(fresh);
                if(saveDiagnostic)SavePixels(freshRaster.pixels,freshRaster.width,freshRaster.height,diagnostic+"-fresh-alternate");
            }
            if(saveDiagnostic){SavePixels(neutralPixels,alternateRaster.width,alternateRaster.height,diagnostic+"-neutral");SavePixels(alternateRaster.pixels,alternateRaster.width,alternateRaster.height,diagnostic+"-alternate");}
            Check(id+" source colours and carved geometry survive appearance sliders "+left,accepted,J.O("neutral_vs_alternate_pixels",colourDifference,"framebuffer_exact",exact,"framebuffer_within_quantization_tolerance",WithinGpuQuantization(colourDifference),"channel_tolerance_bytes",1,"channel_tolerance_normalized",1d/255,"neutral_vs_alternate_geometry",geometryDifference,"material_textures_and_all_shader_properties_exact",materialExact,"neutral_materials",neutralMaterials,"alternate_materials",alternateMaterials,"same_look_same_stand_repeat_pixels",repeatPixels,"same_look_same_stand_repeat_geometry",repeatGeometry,"same_look_same_stand_repeat_materials_exact",repeatMaterialExact,"same_look_same_stand_repeat_within_quantization_tolerance",WithinGpuQuantization(repeatPixels),"alternate_matches_fresh_renderer_same_input",freshDifference["exact"],"fresh_comparison_is_diagnostic_only",true,"alternate_vs_fresh_pixels",freshDifference,"alternate_vs_fresh_geometry",freshGeometry,"neutral_composition",neutralComposition,"alternate_composition",alternateComposition,"fresh_composition",freshComposition,"diagnostic_images_saved",saveDiagnostic,"failure_images_saved",!accepted,"diagnostic_file_prefix",diagnostic,"acceptance_threshold","GPU readback permits at most 1/255 per RGBA channel; mesh vertices UV triangles and all shader texture colour vector float integer parameters must stay exact; repeated original input is an independent quantization control"));
        }
        foreach(string view in new[]{"canvas","portrait","body","bust"}){
            var method=typeof(CharacterArt).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic);
            var texture=(Texture)method.Invoke(null,new object[]{ExplicitLook(entry),"bind",0f,false,view,512,512});
            var studio=(CharacterRenderer)typeof(CharacterArt).GetField("studio",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var raster=Read(texture,id+"-ui-"+view);
            bool mapped=studio!=null&&(string)Field(studio,"resourcePath")==path;bool visible=raster.solid>128&&raster.clear>0;
            // Full canvas must keep its border clear; deliberate portrait and
            // bust compositions may crop shoulders at the bottom of the view.
            if(view=="canvas")visible&=raster.Pass;
            Check(id+" production "+view+" draws the new asset",mapped&&visible,J.O("resource_path",studio==null?"":(string)Field(studio,"resourcePath"),"raster",raster.Facts()));
        }
    }
    void ContactSheet(){
        if(tiles.Count==0)return;const int side=256,columns=6,rows=4;var pixels=new Color32[side*columns*side*rows];
        for(int i=0;i<Math.Min(24,tiles.Count);i++){int ox=(i%columns)*side,oy=(rows-1-i/columns)*side;for(int y=0;y<side;y++)Array.Copy(tiles[i],y*side,pixels,(oy+y)*(side*columns)+ox,side);}
        var sheet=new Texture2D(side*columns,side*rows,TextureFormat.RGBA32,false);sheet.SetPixels32(pixels);sheet.Apply();File.WriteAllBytes(Path.Combine(output,"cast24-contact-sheet.png"),sheet.EncodeToPNG());Destroy(sheet);
        Check("contact sheet contains all twenty-four independently rendered characters",tiles.Count==24,J.O("tiles",tiles.Count,"columns",columns,"rows",rows,"tile_size",side,"artwork_modified",false));
    }
    IEnumerator Picture(string name){for(int i=0;i<3;i++)yield return new WaitForEndOfFrame();var shot=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),shot.EncodeToPNG());Destroy(shot);}
    IEnumerator Production(J catalog){
        GameClient client=null;JianghuApp app=null;float deadline=Time.realtimeSinceStartup+120;
        while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has&&app!=null)break;yield return null;}
        Check("isolated production backend and UI are ready",client!=null&&client.Ready&&client.World.Has&&app!=null);if(client==null||!client.Ready||!client.World.Has||app==null)yield break;
        string beforeWorld=client.World.ToString();var people=client.World["npcs"].A;var sourcePeople=people.Take(48).Concat(new[]{client.World["hero"]}).Where(p=>p.Has).ToList();bool mapping=true;int sampled=0;
        foreach(var person in sourcePeople){
            bool hero=ReferenceEquals(person,client.World["hero"]);string id=app.PaperSkinFor(person,hero);var look=CharacterArt.Look(id);string prefix=person["sex"].S=="女"?"f-":"m-";int age=person["age_months"].Has?person["age_months"].I/12:28;
            string expected=age<16?prefix+"child":age>=60?prefix+"elder":catalog["characters"].A.Any(c=>c["id"].S==look.Role)?look.Role:prefix+"youth-farmer";
            bool found=catalog["characters"].A.Any(c=>c["asset"].S==look.Art)&&look.Art=="cast24-"+expected;mapping&=found;sampled++;
            apiRows.Add(J.O("person",person["id"],"sex",person["sex"],"age_months",person["age_months"],"job",person["job"],"role",look.Role,"appearance",id,"asset",look.Art,"expected_asset","cast24-"+expected,"known_cast24_asset",found));
            if(sampled<=4){Read(CharacterArt.Canvas(id,size:512),"production-person-"+sampled+"-figure");Read(CharacterArt.Canvas(id,portrait:true,size:512),"production-person-"+sampled+"-portrait");}
        }
        Check("real production API people automatically resolve Cast24 art",sampled>1&&mapping,J.O("sampled_people",sampled));
        Check("reading and rendering people does not mutate the API world",beforeWorld==client.World.ToString());
        if(people.Count>0){
            J detail=null;int npc=people[0]["id"].I;yield return client.Request("/api/npc/"+npc,null,j=>detail=j);
            Check("production NPC detail and roster resolve identical appearance",detail!=null&&app.PaperSkinFor(detail)==app.PaperSkinFor(people[0]),J.O("npc_id",npc));
        }
        yield return client.Request("/api/ai/config",J.O("enabled",false),j=>{});
        var opponent=people.FirstOrDefault(p=>p["alive"].B&&p["id"].I>0&&p["age_months"].I>=16*12);if(opponent==null){Check("an isolated production opponent is available",false);yield break;}
        J battle=null;yield return client.Request("/api/intervene",J.O("action","challenge","npc_id",opponent["id"]),j=>{battle=j;app.Martial=j;});
        Check("production challenge API returned battle state",battle!=null,battle);if(battle==null)yield break;app.ShowBattleForSmoke();
        deadline=Time.realtimeSinceStartup+25;while(app.PhysicsArena==null&&Time.realtimeSinceStartup<deadline)yield return null;
        Check("formal production battle opens with native characters",app.PhysicsArena!=null);if(app.PhysicsArena==null)yield break;
        var arena=app.PhysicsArena;arena.PauseCampaign();yield return Picture("production-battle-cast24");
        string frozen=arena.CaptureCampaign().ToString();File.WriteAllText(Path.Combine(output,"physics-before-render.json"),frozen);
        var actors=new[]{arena.player,arena.enemy};bool newArt=true,visible=true;
        for(int i=0;i<actors.Length;i++){
            var actor=actors[i];var renderer=(CharacterRenderer)Field(actor,"characterRenderer");var look=CharacterArt.Look(actor.CharacterAppearanceId);
            bool known=catalog["characters"].A.Any(c=>c["asset"].S==look.Art);newArt&=known&&renderer!=null;
            if(renderer!=null){string path=known?"Characters/Cast24/"+look.Art.Substring("cast24-".Length):"";newArt&=(string)Field(renderer,"resourcePath")==path;var raster=Read(renderer.Texture,"production-battle-"+i);visible&=raster.Pass;apiRows.Add(J.O("battle_actor",i,"appearance",actor.CharacterAppearanceId,"resource_path",(string)Field(renderer,"resourcePath"),"render",actor.CharacterRenderFacts(),"raster",raster.Facts()));}
            else visible=false;
        }
        for(int i=0;i<6;i++){foreach(var actor in actors){CharacterArt.Canvas(actor.CharacterAppearanceId,size:512);CharacterArt.Canvas(actor.CharacterAppearanceId,portrait:true,size:512);}yield return new WaitForEndOfFrame();}
        string after=arena.CaptureCampaign().ToString();File.WriteAllText(Path.Combine(output,"physics-after-render.json"),after);
        Check("formal battle and UI portraits use automatically mapped Cast24 resources",newArt&&visible);
        Check("paused physics snapshot stays byte-identical during rendering and GPU readback",frozen==after,J.O("before_length",frozen.Length,"after_length",after.Length,"comparison","ordinal exact serialized bytes"));
    }
    IEnumerator Run(){
        Screen.SetResolution(1680,1000,FullScreenMode.Windowed);
        string save=Path.GetFullPath(GameClient.Arg("-saveDir")),standard=Path.GetFullPath(Path.Combine(Application.persistentDataPath,"Saves"));
        Check("acceptance uses an explicit save directory separate from normal player saves",!string.Equals(save.TrimEnd('\\','/'),standard.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase),J.O("acceptance_save",save,"normal_player_save",standard));
        if(string.Equals(save.TrimEnd('\\','/'),standard.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))yield break;
        var source=Resources.Load<TextAsset>(CatalogPath);Check("Cast24 catalog is packaged as a Resources asset",source!=null);if(source==null)yield break;var catalog=J.Parse(source.text);var entries=catalog["characters"].A;
        bool valid=catalog["version"].I==1&&entries.Count==24&&entries.Select(c=>c["id"].S).Distinct().Count()==24&&entries.All(c=>c["id"].S.Length>0&&c["asset"].S=="cast24-"+c["id"].S);
        Check("catalog defines all twenty-four distinct production character assets",valid,J.O("version",catalog["version"],"characters",entries.Count));if(!valid)yield break;
        foreach(var entry in entries){try{InspectCharacter(entry);}catch(Exception e){errors.Add(entry["id"].S+": "+e);Check(entry["id"].S+" native acceptance completes",false,J.O("exception",e.ToString()));}yield return null;}
        ContactSheet();yield return Production(catalog);
    }
}
}
