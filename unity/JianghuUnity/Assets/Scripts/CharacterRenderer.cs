using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jianghu {
// One fixed-UV mesh implementation serves cached UI figures and live combat.
// A renderer owns only visual objects; it never drives a rigidbody or a joint.
public sealed class CharacterRenderer:IDisposable {
    static int nextSlot;
    static J catalog;
    static readonly Dictionary<string,Texture2D> sources=new Dictionary<string,Texture2D>();
    readonly J definition;
    readonly string resourcePath;
    readonly GameObject root;
    readonly Camera camera;
    readonly List<Piece> body=new List<Piece>();
    readonly Piece head;
    readonly Piece[] features=new Piece[3];
    readonly Vector3 offset;
    int geometry=-1;
    bool boundLeft;
    CharacterLook look;
    CharacterPose pose;
    float outputScale;
    public RenderTexture Texture {get;private set;}
    public Rect PortraitUV {get;private set;}
    public Rect FrameRect {get;private set;}=new Rect(-1.3f,-.2f,2.6f,2.6f);
    public int Vertices=>body.Sum(p=>p.points.Length)+head.points.Length+(features[0].renderer.enabled?12:0);
    public int NeckLayer=>head.renderer.sortingOrder;
    public int CollarLayer=>body.First(p=>p.part["id"].S=="torso").renderer.sortingOrder;
    sealed class Piece {
        public Mesh mesh;public MeshRenderer renderer;public Material material;public J part,definition;
        public Vector2[] points;public Vector3[] vertices;public float[][] weights;public string[] bones;public Vector3[][] locals;
    }
    public static J Catalog {get{if(catalog==null)catalog=J.Parse(Resources.Load<TextAsset>("Characters/index").text);return catalog;}}
    Texture2D Source(string key){string path=resourcePath+"/"+key;if(!sources.TryGetValue(path,out var t)){t=Resources.Load<Texture2D>(path);if(t==null)throw new InvalidOperationException("Missing character module: "+path);sources[path]=t;}return t;}
    bool Original=>definition["preserve_source_colour"].B;
    J HeadDefinition=>definition["heads"][definition["single_head"].B?0:look.Head];
    public CharacterRenderer(int resolution=512,Transform parent=null,string asset=""){
        resourcePath=asset=="red-lotus"?"Characters/RedLotus":"Characters";
        definition=asset=="red-lotus"?J.Parse(Resources.Load<TextAsset>(resourcePath+"/index").text):Catalog;
        root=new GameObject("Character modules · shared UV");if(parent!=null)root.transform.SetParent(parent);
        offset=new Vector3(4000+16*nextSlot++,4000,0);
        var c=new GameObject("Character alpha camera");c.transform.SetParent(root.transform);c.transform.position=offset+new Vector3(0,1.1f,-5);
        camera=c.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.3f;camera.aspect=1;camera.cullingMask=1<<29;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.nearClipPlane=.1f;camera.farClipPlane=10;camera.allowHDR=false;camera.allowMSAA=true;
        Texture=new RenderTexture(resolution,resolution,16,RenderTextureFormat.ARGB32){name="Character mesh surface",antiAliasing=4};Texture.Create();camera.targetTexture=Texture;
        foreach(J d in definition["body_meshes"].A){
            var p=Create(d["part"]["id"].S,d["part"]["layer"].I,offset);p.part=d["part"];p.definition=d;
            p.points=d["vertices"].A.Select(PhysicsDuel.Point).ToArray();p.vertices=new Vector3[p.points.Length];p.mesh.vertices=p.vertices;p.mesh.uv=d["uv"].A.Select(PhysicsDuel.Point).ToArray();p.mesh.triangles=d["triangles"].A.Select(v=>v.I).ToArray();
            p.weights=d["weights"].A.Select(w=>w.A.Select(v=>(float)v.N).ToArray()).ToArray();
            if(d["skin"].B||p.part["id"].S=="torso"||p.part["id"].S=="pelvis")p.bones=d["bones"].A.Select(v=>v.S).ToArray();
            p.material.SetTexture("_ClipTex",Original?Texture2D.whiteTexture:Source("socket-mask"));body.Add(p);
            if(Original){p.material.SetTexture("_PartMap",Source("part-map"));p.material.SetFloat("_SourcePart",p.part["source_number"].I);}
        }
        head=Create("head and neck behind collar",definition["head_layer"].I,offset);
        head.points=new Vector2[165];head.vertices=new Vector3[165];var triangles=new List<int>();
        for(int y=0;y<=14;y++)for(int x=0;x<=10;x++)head.points[y*11+x]=new Vector2(x*51.2f,y*512f/14);
        for(int y=0;y<14;y++)for(int x=0;x<10;x++){int i=y*11+x;triangles.AddRange(new[]{i,i+1,i+11,i+1,i+12,i+11});}
        head.mesh.vertices=head.vertices;head.mesh.triangles=triangles.ToArray();head.bones=new[]{"head","neck","torso"};head.material.SetFloat("_Head",1);
        for(int i=0;i<3;i++){
            var p=Create(new[]{"eyes","brows","lips"}[i],65+i,offset);p.vertices=new Vector3[4];p.mesh.vertices=p.vertices;p.mesh.triangles=new[]{0,1,2,1,3,2};p.material.mainTexture=Original?Texture2D.whiteTexture:Source("features");features[i]=p;
        }
    }
    Piece Create(string name,int layer,Vector3 offset){
        var go=new GameObject(name);go.transform.SetParent(root.transform);go.transform.position=offset;go.layer=29;
        var mesh=new Mesh{name=name+" UV mesh"};mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=go.AddComponent<MeshRenderer>();renderer.sortingOrder=layer;
        var material=new Material(Resources.Load<Shader>("Characters/CharacterShader"));material.SetTexture("_ClipTex",Texture2D.whiteTexture);material.SetTexture("_ColorMask",Texture2D.blackTexture);renderer.sharedMaterial=material;
        return new Piece{mesh=mesh,renderer=renderer,material=material};
    }
    Matrix4x4 PartMatrix(J part,CharacterPose p){
        string bone=part["bone"].S;Vector2 a,b,heel=Vector2.zero;
        if(bone=="spine"){a=p.Bones["pelvis"].B;b=p.Bones["torso"].B;}
        else if(bone=="pelvis"){a=p.Bones[bone].B;b=p.Bones[bone].A;}
        else {a=p.Bones[bone].A;b=p.Bones[bone].B;if(bone.StartsWith("foot")){heel=a;a=bone.EndsWith("Near")?p.FootNear:p.FootFar;}}
        a.y=-a.y;b.y=-b.y;heel.y=-heel.y;
        Vector2 pivot=PhysicsDuel.Point(part["pivot"]),tip=PhysicsDuel.Point(part["tip"]),size=PhysicsDuel.Point(part["size"]),h=PhysicsDuel.Point(part["heel_pivot"]);
        if(p.Left){pivot.x=size.x-pivot.x;tip.x=size.x-tip.x;h.x=size.x-h.x;}
        Vector2 source=tip-pivot,delta=b-a;var m=Matrix4x4.identity;
        if(part["heel_pivot"].Has){
            h-=pivot;Vector2 t=heel-a;float det=source.x*h.y-h.x*source.y;
            m.m00=(delta.x*h.y-t.x*source.y)/det;m.m01=(t.x*source.x-delta.x*h.x)/det;m.m10=(delta.y*h.y-t.y*source.y)/det;m.m11=(t.y*source.x-delta.y*h.x)/det;
        }else{
            float breadth=part["breadth"].Has?(float)part["breadth"].N:1;if(bone=="spine"&&look.Sex==0)breadth*=1.06f;
            Vector2 q=new Vector2(-source.y,source.x),tq=new Vector2(-delta.y,delta.x)*breadth;float sq=source.sqrMagnitude;
            m.m00=(delta.x*source.x+tq.x*q.x)/sq;m.m01=(delta.x*source.y+tq.x*q.y)/sq;m.m10=(delta.y*source.x+tq.y*q.x)/sq;m.m11=(delta.y*source.y+tq.y*q.y)/sq;
        }
        m.m03=a.x-m.m00*pivot.x-m.m01*pivot.y;m.m13=a.y-m.m10*pivot.x-m.m11*pivot.y;return m;
    }
    static Vector3 Map(Matrix4x4 m,Vector2 point,J part,bool left){if(left)point.x=(float)part["size"][0].N-point.x;var v=m.MultiplyPoint3x4(point);return new Vector3(v.x,-v.y,0);}
    void BindPiece(Piece p,CharacterPose bind){
        if(p.bones==null)return;var m=PartMatrix(p.part,bind);p.locals=new Vector3[p.points.Length][];
        for(int i=0;i<p.points.Length;i++){var point=Map(m,p.points[i],p.part,bind.Left);p.locals[i]=p.bones.Select(n=>bind.Bones[n].Frame.inverse.MultiplyPoint3x4(point)).ToArray();}
    }
    void Rebind(){
        var bind=CharacterPose.Bind(pose.Left);var h=HeadDefinition;
        head.part=J.O("id","head-neck","bone","head","pivot",h["pivot"],"tip",h["tip"],"size",h["size"],"breadth",Original?1:.94+look.Width*.03,"layer",35);
        if(Original){head.material.SetTexture("_PartMap",Source("part-map"));head.material.SetFloat("_SourcePart",h["source_number"].I);}
        if(h["mesh"].Has){
            head.points=h["mesh"]["vertices"].A.Select(PhysicsDuel.Point).ToArray();head.vertices=new Vector3[head.points.Length];head.mesh.Clear();head.mesh.vertices=head.vertices;
            head.mesh.triangles=h["mesh"]["triangles"].A.Select(v=>v.I).ToArray();
        }
        var uv=h["uv"];float u=(float)uv[0].N,v=(float)uv[1].N,w=(float)uv[2].N,t=(float)uv[3].N;
        head.mesh.uv=head.points.Select(p=>new Vector2(u+p.x/(float)h["size"][0].N*w,v+(1-p.y/(float)h["size"][1].N)*t)).ToArray();
        head.weights=new float[head.points.Length][];
        for(int i=0;i<head.points.Length;i++){
            var p=head.points[i];float start=(float)h["neck_start"].N,center=(float)h["neck_center"].N;
            float end=h["neck_end"].Has?(float)h["neck_end"].N:500,half=h["neck_width"].Has?(float)h["neck_width"].N:90;
            float amount=Mathf.Clamp01((p.y-start)/Mathf.Max(1,end-start))*Mathf.Clamp01((p.x-center+half)/28)*Mathf.Clamp01((center+half-p.x)/28);
            head.weights[i]=new[]{1-amount,amount*.25f,amount*.75f};
        }
        foreach(var p in body)BindPiece(p,bind);BindPiece(head,bind);
        geometry=look.Head*100+look.Width*10+look.Sex;boundLeft=pose.Left;
    }
    void Colours(Material m,bool isHead){m.SetColor("_Skin",look.SkinColor);m.SetColor("_Hair",look.HairColor);m.SetColor("_Primary",look.Primary);m.SetColor("_Secondary",look.Secondary);m.SetFloat("_Head",isHead?1:0);}
    void UpdatePiece(Piece p){
        var m=PartMatrix(p.part,pose);
        for(int i=0;i<p.points.Length;i++){
            Vector3 position=Map(m,p.points[i],p.part,pose.Left);
            if(p.bones!=null){
                position=Vector3.zero;
                for(int j=0;j<p.bones.Length;j++){
                    // A garment stays with its pelvis after a leg is severed;
                    // a detached head no longer blends back toward the collar.
                    int bindIndex=pose.Detached.Contains(p.bones[0])||pose.Detached.Contains(p.bones[j])?0:j;
                    position+=pose.Bones[p.bones[bindIndex]].Frame.MultiplyPoint3x4(p.locals[i][bindIndex])*p.weights[i][j];
                }
            }
            p.vertices[i]=position*outputScale;
        }
        p.mesh.vertices=p.vertices;p.mesh.RecalculateBounds();
    }
    public void Render(CharacterLook appearance,CharacterPose frame,float displayScale=1,bool fitAll=false,bool draw=true){
        look=appearance;pose=frame;outputScale=displayScale;
        if(geometry!=look.Head*100+look.Width*10+look.Sex||boundLeft!=pose.Left)Rebind();
        string outfit=look.Clothing==0?"cloud":"jade";
        foreach(var p in body){p.material.mainTexture=Source(Original?p.part["texture"].S:outfit);p.material.SetTexture("_ColorMask",Original?Texture2D.blackTexture:Source(outfit+"-mask"));Colours(p.material,false);UpdatePiece(p);}
        J h=HeadDefinition;head.material.mainTexture=Source(h["texture"].S);head.material.SetTexture("_ColorMask",Original?Texture2D.blackTexture:Source(h["texture"].S+"-mask"));Colours(head.material,true);UpdatePiece(head);
        var hm=PartMatrix(head.part,pose);string[] kinds={"eyes","brows","lips"};int[] choices={look.Eyes,look.Brows,look.Lips};
        for(int i=0;i<3;i++){
            features[i].renderer.enabled=!h["baked_features"].B;if(!features[i].renderer.enabled)continue;
            var r=h["face_rects"][kinds[i]];float x=(float)r[0].N,y=(float)r[1].N,w=(float)r[2].N,t=(float)r[3].N;
            Vector2[] corners={new Vector2(x,y),new Vector2(x+w,y),new Vector2(x,y+t),new Vector2(x+w,y+t)};
            for(int j=0;j<4;j++)features[i].vertices[j]=Map(hm,corners[j],head.part,pose.Left)*outputScale;
            var a=definition["features"][kinds[i]][choices[i]];float u=(float)a[0].N,v=(float)a[1].N,uw=(float)a[2].N,vh=(float)a[3].N;
            features[i].mesh.vertices=features[i].vertices;features[i].mesh.uv=new[]{new Vector2(u,v+vh),new Vector2(u+uw,v+vh),new Vector2(u,v),new Vector2(u+uw,v)};features[i].mesh.RecalculateBounds();
        }
        FrameRect=new Rect(-1.3f,-.2f,2.6f,2.6f);
        if(fitAll){
            Vector2 min=FrameRect.min,max=FrameRect.max;
            foreach(var p in body.Concat(new[]{head}).Concat(features))foreach(var v in p.vertices){min=Vector2.Min(min,(Vector2)v-Vector2.one*.03f);max=Vector2.Max(max,(Vector2)v+Vector2.one*.03f);}
            float side=Mathf.Max(max.x-min.x,max.y-min.y);Vector2 middle=(min+max)*.5f;FrameRect=new Rect(middle-Vector2.one*side*.5f,Vector2.one*side);
        }
        camera.orthographicSize=FrameRect.height*.5f;camera.transform.position=offset+new Vector3(FrameRect.center.x,FrameRect.center.y,-5);
        var eye=h["face_rects"]["eyes"];var center=Map(hm,new Vector2((float)eye[0].N+(float)eye[2].N*.5f,(float)eye[1].N),head.part,pose.Left)*outputScale;
        float half=.17f*outputScale,px=(center.x-FrameRect.xMin)/FrameRect.width-(pose.Left?-.04f:.04f)*outputScale,py=(center.y-FrameRect.yMin)/FrameRect.height+.045f*outputScale;
        PortraitUV=new Rect(Mathf.Clamp(px-half,0,1-half*2),Mathf.Clamp(py-half,0,1-half*2),half*2,half*2);
        if(draw)camera.Render();
    }
    // Render the selected composition straight from meshes. Cropping a small
    // full-body bitmap first would discard facial and carved-cloth detail.
    public void RenderCrop(RenderTexture target,Rect uv){
        var priorTarget=camera.targetTexture;var priorPosition=camera.transform.position;
        float priorSize=camera.orthographicSize,priorAspect=camera.aspect;
        Rect frame=new Rect(FrameRect.x+uv.x*FrameRect.width,FrameRect.y+uv.y*FrameRect.height,uv.width*FrameRect.width,uv.height*FrameRect.height);
        try{
            camera.targetTexture=target;camera.aspect=frame.width/frame.height;camera.orthographicSize=frame.height*.5f;
            camera.transform.position=offset+new Vector3(frame.center.x,frame.center.y,-5);camera.Render();
        }finally{camera.targetTexture=priorTarget;camera.transform.position=priorPosition;camera.orthographicSize=priorSize;camera.aspect=priorAspect;}
    }
    public J Probe()=>J.O("head",look?.Head??-1,"vertices",Vertices,"neck_layer",NeckLayer,"collar_layer",CollarLayer,"mesh_renderers",body.Count+1+features.Count(p=>p.renderer.enabled),"shared_uv_template",true,"art",look?.Art??"","finite",body.Concat(new[]{head}).All(p=>p.vertices.All(v=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsInfinity(v.x)&&!float.IsInfinity(v.y))));
    public void Dispose(){
        foreach(var p in body.Concat(new[]{head}).Concat(features)){if(p==null)continue;UnityEngine.Object.Destroy(p.mesh);UnityEngine.Object.Destroy(p.material);}
        if(Texture!=null){Texture.Release();UnityEngine.Object.Destroy(Texture);Texture=null;}UnityEngine.Object.Destroy(root);
    }
}
}
