using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Jianghu {
// Display acceptance uses actual vertices and raster alpha against source
// pixels and anatomical landmarks, rather than an already distorted baseline.
public sealed class CharacterProportionSmoke:MonoBehaviour {
    string output;readonly List<J> checks=new List<J>(),frames=new List<J>();readonly List<string> errors=new List<string>();
    readonly Dictionary<int,Rect> sourceBounds=new Dictionary<int,Rect>();
    sealed class Raster {public Color32[] pixels;public int width,height;public Rect frame;public float minY,maxY;public int solid,clear;}
    sealed class HeadFacts {public float shear,ratio=1,scale,headHeight,referenceHeight,featureError;public int triangles;public Matrix4x4 affine;public J Facts()=>J.O("source_rigid_triangles",triangles,"source_shear_cosine",shear,"source_singular_value_ratio",ratio,"actual_source_to_world_scale",scale,"visible_crown_to_neck_height_world",headHeight,"unscaled_reference_head_height_world",referenceHeight,"actual_to_unscaled_head_height",headHeight/referenceHeight,"maximum_facial_feature_attachment_error_world",featureError);}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){if(GameClient.Arg("-characterProportionSmoke").Length==0)return;if(GameClient.Arg("-saveDir").Length==0){Debug.LogError("Character proportion acceptance requires an isolated saveDir");Application.Quit(2);return;}new GameObject("Character proportion and neutral stance acceptance").AddComponent<CharacterProportionSmoke>();}
    void OnEnable(){Application.logMessageReceived+=Log;}void OnDisable(){Application.logMessageReceived-=Log;}
    void Log(string s,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(s);}
    void Check(string name,bool pass,J facts=null){checks.Add(J.O("check",name,"pass",pass,"facts",facts??J.O()));File.WriteAllText(Path.Combine(output,"progress.json"),new J(checks).ToString());}
    IEnumerator Start(){
        output=GameClient.Arg("-characterProportionSmoke");Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"renders"));
        var stack=new Stack<IEnumerator>();stack.Push(Run());while(stack.Count>0){object value=null;bool more=false;try{more=stack.Peek().MoveNext();if(more)value=stack.Peek().Current;}catch(Exception e){errors.Add(e.ToString());stack.Clear();break;}if(!more){stack.Pop();continue;}if(value is IEnumerator nested&&!(value is CustomYieldInstruction))stack.Push(nested);else yield return value;}
        Check("no runtime errors",errors.Count==0,new J(errors.Select(e=>new J(e)).ToList()));
        File.WriteAllText(Path.Combine(output,"proportion-frames.json"),new J(frames).ToString());
        bool pass=checks.All(c=>c["pass"].B);File.WriteAllText(Path.Combine(output,"character-proportion-native.json"),J.O("pass",pass,"checks",new J(checks),"errors",new J(errors.Select(e=>new J(e)).ToList()),"physics_combat_tested",false,"prior_physical_limit","Existing source-rig primary weapon grip drift is tracked separately in binding-integrity-20261029/native-release; this suite does not alter or clear that result.").ToString());Application.Quit(pass?0:1);
    }
    static object Field(object item,string name)=>item.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(item);
    static GameObject Root(CharacterRenderer renderer)=>(GameObject)Field(renderer,"root");
    static CharacterLook Look(string kind)=>new CharacterLook{Role=kind=="monk"?"m-monk":"m-youth-farmer",Sex=kind.Contains("woman")||kind=="girl"?1:0,Stage=kind=="boy"||kind=="girl"?0:kind.StartsWith("elder")?2:1,Width=2,Variant=kind.EndsWith("-alt")?1:0};
    Raster Read(Texture texture,Rect frame,string file=""){
        var prior=RenderTexture.active;var temp=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32);Graphics.Blit(texture,temp);RenderTexture.active=temp;var image=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();RenderTexture.active=prior;RenderTexture.ReleaseTemporary(temp);
        if(file.Length>0)File.WriteAllBytes(Path.Combine(output,"renders",file+".png"),image.EncodeToPNG());
        var raster=new Raster{pixels=image.GetPixels32(),width=image.width,height=image.height,frame=frame,minY=float.PositiveInfinity,maxY=float.NegativeInfinity};Destroy(image);
        for(int y=0;y<raster.height;y++)for(int x=0;x<raster.width;x++){int alpha=raster.pixels[y*raster.width+x].a;if(alpha==0)raster.clear++;if(alpha<48)continue;raster.solid++;float world=frame.yMin+(y+.5f)*frame.height/raster.height;raster.minY=Mathf.Min(raster.minY,world);raster.maxY=Mathf.Max(raster.maxY,world);}return raster;
    }
    Rect SourceHeadBounds(int head){
        if(sourceBounds.TryGetValue(head,out var bounds))return bounds;var h=CharacterRenderer.Catalog["heads"][head];var texture=Resources.Load<Texture2D>("Characters/"+h["texture"].S);var raster=Read(texture,new Rect(0,0,1,1));
        float u=(float)h["uv"][0].N,v=(float)h["uv"][1].N,w=(float)h["uv"][2].N,t=(float)h["uv"][3].N;int width=h["size"][0].I,height=h["size"][1].I;int minX=width,minY=height,maxX=0,maxY=0;
        for(int y=0;y<h["neck_start"].I;y++)for(int x=0;x<width;x++){int tx=Mathf.Clamp(Mathf.FloorToInt((u+(x+.5f)/width*w)*raster.width),0,raster.width-1),ty=Mathf.Clamp(Mathf.FloorToInt((v+(1-(y+.5f)/height)*t)*raster.height),0,raster.height-1);if(raster.pixels[ty*raster.width+tx].a<48)continue;minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x+1);maxY=Math.Max(maxY,y+1);}
        bounds=new Rect(minX,minY,maxX-minX,maxY-minY);sourceBounds[head]=bounds;return bounds;
    }
    HeadFacts HeadShape(CharacterRenderer renderer,CharacterLook look,float displayScale){
        object piece=Field(renderer,"head");var source=(Vector2[])Field(piece,"points");var mesh=(Mesh)Field(piece,"mesh");var vertices=mesh.vertices;var triangles=mesh.triangles;var weights=(float[][])Field(piece,"weights");var facts=new HeadFacts();double scaleSum=0;
        for(int i=0;i<triangles.Length;i+=3){int ia=triangles[i],ib=triangles[i+1],ic=triangles[i+2];if(weights[ia][0]<.99999f||weights[ib][0]<.99999f||weights[ic][0]<.99999f)continue;Vector2 a=source[ib]-source[ia],b=source[ic]-source[ia];double det=(double)a.x*b.y-(double)a.y*b.x;if(Math.Abs(det)<1e-8)continue;Vector3 u=vertices[ib]-vertices[ia],v=vertices[ic]-vertices[ia];
            double xx=(u.x*b.y-v.x*a.y)/det,xy=(u.y*b.y-v.y*a.y)/det,yx=(v.x*a.x-u.x*b.x)/det,yy=(v.y*a.x-u.y*b.x)/det;double g00=xx*xx+xy*xy,g11=yx*yx+yy*yy,g01=xx*yx+xy*yy,disc=Math.Sqrt((g00-g11)*(g00-g11)+4*g01*g01),low=(g00+g11-disc)*.5,high=(g00+g11+disc)*.5;
            if(low<=0)throw new InvalidOperationException("A head source triangle collapsed");facts.shear=Mathf.Max(facts.shear,(float)(Math.Abs(g01)/Math.Sqrt(g00*g11)));facts.ratio=Mathf.Max(facts.ratio,(float)Math.Sqrt(high/low));scaleSum+=Math.Sqrt((g00+g11)*.5);
            if(facts.triangles==0){var m=Matrix4x4.identity;m.m00=(float)xx;m.m01=(float)yx;m.m10=(float)xy;m.m11=(float)yy;m.m03=vertices[ia].x-m.m00*source[ia].x-m.m01*source[ia].y;m.m13=vertices[ia].y-m.m10*source[ia].x-m.m11*source[ia].y;facts.affine=m;}facts.triangles++;
        }
        facts.scale=(float)(scaleSum/Math.Max(1,facts.triangles));Rect bounds=SourceHeadBounds(look.Head);Vector3[] corners={facts.affine.MultiplyPoint3x4(bounds.min),facts.affine.MultiplyPoint3x4(bounds.max),facts.affine.MultiplyPoint3x4(new Vector2(bounds.xMin,bounds.yMax)),facts.affine.MultiplyPoint3x4(new Vector2(bounds.xMax,bounds.yMin))};facts.headHeight=corners.Max(p=>p.y)-corners.Min(p=>p.y);
        var h=CharacterRenderer.Catalog["heads"][look.Head];float pixels=Vector2.Distance(PhysicsDuel.Point(h["pivot"]),PhysicsDuel.Point(h["tip"]));facts.referenceHeight=bounds.height*.3f/pixels*displayScale;
        foreach(string name in new[]{"eyes","brows","lips"}){var filter=Root(renderer).GetComponentsInChildren<MeshFilter>().First(f=>f.name==name);if(!filter.GetComponent<MeshRenderer>().enabled)continue;var rectangle=h["face_rects"][name];float x=(float)rectangle[0].N,y=(float)rectangle[1].N,w=(float)rectangle[2].N,t=(float)rectangle[3].N;Vector2[] corners2={new Vector2(x,y),new Vector2(x+w,y),new Vector2(x,y+t),new Vector2(x+w,y+t)};var actual=filter.sharedMesh.vertices;for(int i=0;i<4;i++)facts.featureError=Mathf.Max(facts.featureError,Vector3.Distance(actual[i],facts.affine.MultiplyPoint3x4(corners2[i])));}
        return facts;
    }
    static J Posture(CharacterPose p){
        float axis=0,knee=0,lean=0,handDrop=10,handSide=0,handBelowWaist=10;
        foreach(string name in new[]{"pelvis","belly","torso","neck","head"})axis=Mathf.Max(axis,Mathf.Abs(p.Bones[name].B.x-p.Bones[name].A.x));
        foreach(string side in new[]{"Near","Far"}){var thigh=p.Bones["thigh"+side];var shin=p.Bones["shin"+side];knee=Mathf.Max(knee,Vector2.Angle(thigh.B-thigh.A,shin.B-shin.A));lean=Mathf.Max(lean,Vector2.Angle(Vector2.down,thigh.B-thigh.A),Vector2.Angle(Vector2.down,shin.B-shin.A));var shoulder=p.Bones["upper"+side].A;var hand=p.Bones["hand"+side].B;handDrop=Mathf.Min(handDrop,shoulder.y-hand.y);handSide=Mathf.Max(handSide,Mathf.Abs(hand.x-shoulder.x));handBelowWaist=Mathf.Min(handBelowWaist,p.Bones["pelvis"].B.y-hand.y);}
        return J.O("maximum_axis_horizontal_drift_world",axis,"maximum_knee_flexion_degrees",knee,"maximum_leg_segment_lean_degrees",lean,"minimum_hand_drop_from_shoulder_world",handDrop,"maximum_hand_side_offset_world",handSide,"minimum_hand_below_waist_world",handBelowWaist,"near_ankle_y",p.FootNear.y,"far_ankle_y",p.FootFar.y,"lowest_ankle_y",Mathf.Min(p.FootNear.y,p.FootFar.y));
    }
    static bool Neutral(J p)=>p["maximum_axis_horizontal_drift_world"].N<.00001&&p["maximum_knee_flexion_degrees"].N<13.5&&p["maximum_leg_segment_lean_degrees"].N<11&&p["minimum_hand_drop_from_shoulder_world"].N>.55&&p["maximum_hand_side_offset_world"].N<.11&&p["minimum_hand_below_waist_world"].N>.2&&Math.Abs(p["near_ankle_y"].N-.13)<.00001&&Math.Abs(p["far_ankle_y"].N-.13)<.00001;
    static bool Shape(HeadFacts h)=>h.triangles>20&&h.shear<.001&&h.ratio<1.002&&h.featureError<.00003f;
    J Collar(Raster image,CharacterPose pose,float scale){
        Vector2 centre=pose.Bones["neck"].A*scale;int cx=Mathf.RoundToInt((centre.x-image.frame.xMin)/image.frame.width*image.width),cy=Mathf.RoundToInt((centre.y-image.frame.yMin)/image.frame.height*image.height);int radius=Mathf.CeilToInt(.11f*scale/image.frame.width*image.width),search=Mathf.Max(2,Mathf.CeilToInt(.025f*scale/image.frame.width*image.width));
        bool Solid(int x,int y)=>x>=0&&x<image.width&&y>=0&&y<image.height&&image.pixels[y*image.width+x].a>=48;
        Vector2Int Closest(int tx,int ty){float best=float.PositiveInfinity;var result=new Vector2Int(-1,-1);for(int y=ty-search;y<=ty+search;y++)for(int x=tx-search;x<=tx+search;x++){float d=(x-tx)*(x-tx)+(y-ty)*(y-ty);if(d<best&&Solid(x,y)){best=d;result=new Vector2Int(x,y);}}return result;}
        int step=Mathf.RoundToInt(.05f*scale/image.frame.height*image.height);var a=Closest(cx,cy-step);var b=Closest(cx,cy+step);var c=Closest(cx,cy);bool connected=false;var queue=new Queue<Vector2Int>();var visited=new HashSet<int>();if(a.x>=0){queue.Enqueue(a);visited.Add(a.y*image.width+a.x);}while(queue.Count>0&&!connected){var at=queue.Dequeue();if(at==b){connected=true;break;}for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int x=at.x+dx,y=at.y+dy,key=y*image.width+x;if(Math.Abs(x-cx)>radius||Math.Abs(y-cy)>radius||visited.Contains(key)||!Solid(x,y))continue;visited.Add(key);queue.Enqueue(new Vector2Int(x,y));}}
        float clearance=c.x<0?.1f:Vector2.Distance(c,new Vector2Int(cx,cy))*image.frame.width/image.width/scale;return J.O("opaque_parent_child_path",connected,"clearance_world",clearance,"alpha_threshold",48);
    }
    J FootGround(CharacterRenderer renderer,float scale){
        var all=Root(renderer).GetComponentsInChildren<MeshRenderer>();var enabled=all.Select(r=>r.enabled).ToArray();var target=new RenderTexture(512,512,0,RenderTextureFormat.ARGB32);target.Create();var rows=new List<J>();float maximum=0;int count=0;
        try{foreach(var foot in all.Where(r=>r.name.Contains("foot"))){for(int i=0;i<all.Length;i++)all[i].enabled=all[i]==foot;renderer.RenderCrop(target,new Rect(0,0,1,1));var raster=Read(target,renderer.FrameRect);float sole=raster.minY/scale;maximum=Mathf.Max(maximum,Mathf.Abs(sole));count++;rows.Add(J.O("part",foot.name,"actual_alpha_sole_y_world",sole,"opaque_pixels",raster.solid));}}
        finally{for(int i=0;i<all.Length;i++)all[i].enabled=enabled[i];target.Release();Destroy(target);}
        return J.O("feet",new J(rows),"count",count,"maximum_distance_to_display_ground_world",maximum);
    }
    IEnumerator Picture(string name){for(int i=0;i<3;i++)yield return new WaitForEndOfFrame();var shot=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),shot.EncodeToPNG());Destroy(shot);}
    IEnumerator Run(){
        Screen.SetResolution(1680,1000,FullScreenMode.Windowed);int caseCount=0;
        foreach(string kind in new[]{"adult-man","adult-man-alt","adult-woman","adult-woman-alt","elder-man","elder-man-alt","elder-woman","monk","boy","girl"})foreach(bool left in new[]{false,true}){
            var look=Look(kind);float scale=look.DisplayScale;string label=kind+(left?"-left":"-right");using(var renderer=new CharacterRenderer(768)){
                var neutral=CharacterPose.Sample("bind",0,left);neutral.BindPose=CharacterPose.Bind(left);renderer.Render(look,neutral,scale,fitAll:true);var head=HeadShape(renderer,look,scale);var raster=Read(renderer.Texture,renderer.FrameRect,label+"-standing");var posture=Posture(neutral);float fraction=head.headHeight/(raster.maxY-raster.minY);var collar=Collar(raster,neutral,scale);var soles=FootGround(renderer,scale);var probe=renderer.Probe();
                Check(label+" head keeps the original source shape",Shape(head),head.Facts());
                float expected=look.Stage==0?1:.72f;Check(label+" age appropriate head size measured from source pixels",Mathf.Abs(head.headHeight/head.referenceHeight-expected)<.003f&&(look.Stage==0?fraction>.23f:fraction<.215f)&&Math.Abs(probe["head_visual_scale"].N-expected)<.00001,J.O("head",head.Facts(),"actual_head_to_visible_body_height",fraction,"expected_source_scale",expected,"probe",probe));
                Check(label+" stands upright with resting arms and nearly straight legs",Neutral(posture),posture);
                Check(label+" both visible shoe soles reach display ground",soles["count"].I==2&&soles["maximum_distance_to_display_ground_world"].N<.04,soles);
                Check(label+" scaled neck joins the collar in actual alpha",collar["opaque_parent_child_path"].B&&collar["clearance_world"].N<.015,collar);
                Check(label+" full body is visible on a transparent surface",raster.solid>1000&&raster.clear>raster.pixels.Length*.5f,J.O("opaque_pixels",raster.solid,"transparent_pixels",raster.clear));
                bool walks=true,seams=true,headShape=true,blendZero=true;float minKnee=180,maxKnee=0,maxLift=0,maxHeadRatioError=0;Vector2 firstWrist=Vector2.zero;float wristTravel=0;
                for(int i=0;i<24;i++){
                    float phase=i/24f;var pose=CharacterPose.Sample("walk",phase,left);pose.BindPose=CharacterPose.Bind(left);bool picture=i%6==0;renderer.Render(look,pose,scale,fitAll:true,draw:picture);var motion=Posture(pose);var shape=HeadShape(renderer,look,scale);headShape&=Shape(shape);maxHeadRatioError=Mathf.Max(maxHeadRatioError,Mathf.Abs(shape.headHeight/shape.referenceHeight-expected));
                    minKnee=Mathf.Min(minKnee,(float)motion["maximum_knee_flexion_degrees"].N);maxKnee=Mathf.Max(maxKnee,(float)motion["maximum_knee_flexion_degrees"].N);maxLift=Mathf.Max(maxLift,pose.FootNear.y-.13f,pose.FootFar.y-.13f);walks&=Math.Abs(motion["lowest_ankle_y"].N-.13)<.0001&&motion["maximum_axis_horizontal_drift_world"].N<.00001&&motion["maximum_knee_flexion_degrees"].N<40;
                    var zero=CharacterPose.Sample("walk",phase,left,0);foreach(var bone in neutral.Bones)blendZero&=Vector2.Distance(zero.Bones[bone.Key].A,bone.Value.A)<.00001&&Vector2.Distance(zero.Bones[bone.Key].B,bone.Value.B)<.00001;
                    Vector2 wrist=pose.Bones["handNear"].B;if(i==0)firstWrist=wrist;wristTravel=Mathf.Max(wristTravel,Vector2.Distance(wrist,firstWrist));
                    J alpha=J.O();if(picture){var actual=Read(renderer.Texture,renderer.FrameRect,label+"-walk-"+i.ToString("D2"));alpha=Collar(actual,pose,scale);seams&=alpha["opaque_parent_child_path"].B&&alpha["clearance_world"].N<.015;}
                    frames.Add(J.O("case",label,"phase",phase,"posture",motion,"head",shape.Facts(),"collar_alpha",alpha));caseCount++;yield return null;
                }
                Check(label+" walking keeps a planted foot bounded knees and real arm motion",walks&&maxLift>.02f&&maxLift<=.0351f&&wristTravel>.04f,J.O("sampled_phases",24,"minimum_max_knee_flexion",minKnee,"maximum_knee_flexion",maxKnee,"maximum_foot_lift_world",maxLift,"hand_travel_world",wristTravel));
                Check(label+" walking preserves source head shape and age scale",headShape&&maxHeadRatioError<.003f,J.O("maximum_head_scale_error",maxHeadRatioError));
                Check(label+" walking preserves rendered collar connection",seams);
                Check(label+" zero walk blend exactly matches the neutral stance",blendZero);
            }
        }
        Check("all ten standard head types and both facings were sampled",caseCount==480,J.O("walk_frames",caseCount,"standing_frames",20));
        using(var renderer=new CharacterRenderer(768)){
            var look=Look("monk");var rows=new List<J>();bool pass=true;foreach(int stage in new[]{1,0,1}){look.Stage=stage;var pose=CharacterPose.Sample("bind",0);pose.BindPose=CharacterPose.Bind();renderer.Render(look,pose,look.DisplayScale,fitAll:true);var shape=HeadShape(renderer,look,look.DisplayScale);float expected=stage==0?1:.72f;pass&=Shape(shape)&&Mathf.Abs(shape.headHeight/shape.referenceHeight-expected)<.003f;Read(renderer.Texture,renderer.FrameRect,"monk-stage-switch-"+rows.Count+"-"+stage);rows.Add(J.O("stage",stage,"head_module",look.Head,"display_scale",look.DisplayScale,"source_geometry",shape.Facts(),"renderer",renderer.Probe()));}
            Check("same monk head module rebinds adult child adult proportions",pass,new J(rows));
        }
        GameClient client=null;JianghuApp app=null;float deadline=Time.realtimeSinceStartup+120;while(Time.realtimeSinceStartup<deadline){client=FindAnyObjectByType<GameClient>();app=FindAnyObjectByType<JianghuApp>();if(client!=null&&client.Ready&&client.World.Has&&app!=null)break;yield return null;}
        Check("isolated production UI is ready",client!=null&&client.Ready&&client.World.Has&&app!=null);if(client==null||!client.Ready||app==null)yield break;
        yield return client.Request("/api/ai/config",J.O("enabled",false),j=>{});int month=client.World["month"].I;
        foreach(int age in new[]{28,70,12})foreach(int sex in new[]{0,1}){app.AppearancePreview(sex,age,false,true);yield return Picture("production-ui-"+age+"-"+sex);}
        Check("display inspection preserves simulation month",client.World["month"].I==month);
    }
}
}
