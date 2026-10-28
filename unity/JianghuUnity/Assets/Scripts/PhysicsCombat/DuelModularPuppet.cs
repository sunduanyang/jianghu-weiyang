using System;
using System.Collections.Generic;
using System.Linq;
using Jianghu;
using UnityEngine;

public sealed partial class PhysicsDuel {
    public static bool ModularTrial=>GameClient.Arg("-modularPuppet").Length>0;
    public void BeginModularAcceptance(){
        if(!ModularTrial)throw new InvalidOperationException("The modular art sandbox is required");
        smoke=true;started=true;training=true;muted=true;player.testOverride=enemy.testOverride=true;
    }
    public void ModularInput(bool automatic){smoke=automatic||GameClient.Arg("-modularSmoke").Length>0;player.testOverride=enemy.testOverride=automatic;controls.ReleaseAll(true);player.ClearMouseCapture();}
    public void ModularHands(Vector2 main,Vector2 off){
        if(!controls.right.captured)controls.Begin(9221,new Vector2(1235,655));
        if(!controls.left.captured)controls.Begin(9222,new Vector2(365,655));
        controls.Move(9221,controls.right.center+new Vector2(main.x,-main.y)*DuelControls.Pad.Radius);
        controls.Move(9222,controls.left.center+new Vector2(off.x,-off.y)*DuelControls.Pad.Radius);
    }
    public sealed partial class Fighter {
        public int PuppetEyes,PuppetBrows,PuppetLips,PuppetOutfit;
        public ModularRig puppet;
        bool TryDrawModularPuppet(bool debug){if(!ModularTrial||!Reference)return false;DrawModularPuppet(debug,true);return true;}
        public void DrawModularPuppet(bool debug,bool equipment){
            if(puppet==null)puppet=new ModularRig(this);
            if(Event.current.type==EventType.Repaint)puppet.Render();
            if(equipment&&shield!=null&&HasShield)DrawWeapon(shield,true);
            var origin=ScreenPoint(new Vector2(Hip.x,0));
            GUI.DrawTexture(new Rect(origin.x-1.3f*Unit,Ground-2.4f*Unit,2.6f*Unit,2.6f*Unit),puppet.Texture,ScaleMode.StretchToFill,true);
            if(equipment&&weapon.id!="unarmed")DrawWeapon(sword,false);
            if(debug){
                foreach(var bone in referenceBones.Values){game.Line(ScreenPoint(bone.A),ScreenPoint(bone.B),.9f,new Color(.08f,.52f,.48f,.85f));game.Circle(ScreenPoint(bone.A),Vector2.one*2.7f,new Color(.75f,.3f,.15f));}
                puppet.Wireframe();
            }
        }
        Matrix4x4 PuppetPartMatrix(J part){
            string name=part["bone"].S;Vector2 a,b,heel=Vector2.zero;
            if(name=="spine"){a=pelvis.B;b=torso.B;}
            else if(name=="pelvis"){a=pelvis.B;b=pelvis.A;}
            else {var limb=limbs[name];a=limb.A;b=limb.B;if(name.StartsWith("foot")){a=Limb.BodyPoint(limb.rb,limb.joint.anchor);heel=limb.A;}}
            Vector2 pivot=Point(part["pivot"]),tip=Point(part["tip"]),size=Point(part["size"]),h=Point(part["heel_pivot"]);
            if(facing<0){pivot.x=size.x-pivot.x;tip.x=size.x-tip.x;h.x=size.x-h.x;}
            Vector2 source=tip-pivot,delta=ScreenPoint(b)-ScreenPoint(a),target=ScreenPoint(a);
            var m=Matrix4x4.identity;
            if(part["heel_pivot"].Has){
                h-=pivot;Vector2 t=ScreenPoint(heel)-target;float det=source.x*h.y-h.x*source.y;
                m.m00=(delta.x*h.y-t.x*source.y)/det;m.m01=(t.x*source.x-delta.x*h.x)/det;
                m.m10=(delta.y*h.y-t.y*source.y)/det;m.m11=(t.y*source.x-delta.y*h.x)/det;
            }else{
                Vector2 q=new Vector2(-source.y,source.x),tq=new Vector2(-delta.y,delta.x)*(float)part["breadth"].N;float sq=source.sqrMagnitude;
                m.m00=(delta.x*source.x+tq.x*q.x)/sq;m.m01=(delta.x*source.y+tq.x*q.y)/sq;
                m.m10=(delta.y*source.x+tq.y*q.x)/sq;m.m11=(delta.y*source.y+tq.y*q.y)/sq;
            }
            m.m03=target.x-m.m00*pivot.x-m.m01*pivot.y;m.m13=target.y-m.m10*pivot.x-m.m11*pivot.y;
            return m;
        }
        public sealed class ModularRig:IDisposable {
            const int Layer=30;
            static J catalog;
            static Texture2D[] outfits;
            static Texture2D features,mask;
            static int nextId;
            readonly Fighter owner;
            readonly GameObject root;
            readonly Camera camera;
            readonly List<Piece> pieces=new List<Piece>();
            readonly Piece[] face=new Piece[3];
            readonly Vector3 offset;
            int frame=-1,style=-1;
            float sway,swayVelocity;
            public RenderTexture Texture {get;private set;}
            public int MixedVertices {get;private set;}
            public float MaxWeightError {get;private set;}
            public float MaxHemDeformation {get;private set;}
            public int VertexCount=>pieces.Sum(p=>p.positions.Length);
            public bool Finite {get;private set;}=true;
            sealed class Piece {
                public J part,definition;
                public Mesh mesh;
                public MeshRenderer renderer;
                public Material material;
                public Vector3[] positions,bind;
                public Vector2[] samples;
                public Vector3[][] locals;
                public Transform[] bones;
                public float[][] weights;
            }
            public ModularRig(Fighter fighter){
                owner=fighter;
                if(catalog==null){
                    catalog=J.Parse(Resources.Load<TextAsset>("ModularPuppet/index").text);
                    outfits=new[]{Resources.Load<Texture2D>("ModularPuppet/cloud"),Resources.Load<Texture2D>("ModularPuppet/jade")};
                    features=Resources.Load<Texture2D>("ModularPuppet/features");mask=Resources.Load<Texture2D>("ModularPuppet/socket-mask");
                }
                offset=new Vector3(1000+20*nextId++,1000,0);
                root=new GameObject("Original puppet · fixed UV meshes");root.transform.SetParent(owner.root.transform);
                var lifetime=root.AddComponent<ModularPuppetLifetime>();lifetime.Release=Dispose;
                var go=new GameObject("Transparent portrait camera");go.transform.SetParent(root.transform);go.transform.position=offset+new Vector3(0,1.1f,-5);
                camera=go.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.3f;camera.aspect=1;
                camera.nearClipPlane=.1f;camera.farClipPlane=10;camera.cullingMask=1<<Layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
                camera.allowHDR=false;camera.allowMSAA=true;
                Texture=new RenderTexture(1024,1024,16,RenderTextureFormat.ARGB32){name="Puppet transparent mesh portrait",antiAliasing=4};Texture.Create();camera.targetTexture=Texture;
                foreach(J definition in catalog["meshes"].A){
                    var part=definition["part"];var p=Create(part["id"].S,(int)part["layer"].N);p.part=part;p.definition=definition;
                    p.samples=definition["vertices"].A.Select(Point).ToArray();p.positions=new Vector3[p.samples.Length];p.bind=new Vector3[p.samples.Length];
                    p.mesh.vertices=p.positions;p.mesh.uv=definition["uv"].A.Select(Point).ToArray();p.mesh.triangles=definition["triangles"].A.Select(v=>(int)v.N).ToArray();
                    p.weights=definition["weights"].A.Select(w=>w.A.Select(v=>(float)v.N).ToArray()).ToArray();
                    foreach(var w in p.weights){MaxWeightError=Mathf.Max(MaxWeightError,Mathf.Abs(w.Sum()-1));if(w.Count(v=>v>.00001f)>1)MixedVertices++;}
                    bool flexible=part["id"].S=="pelvis"||part["id"].S=="torso"||part["id"].S=="head-neck";
                    if(flexible){
                        p.bones=definition["bones"].A.Select(n=>owner.referenceBones[n.S].rb.transform).ToArray();
                        p.locals=new Vector3[p.samples.Length][];
                        var matrix=owner.PuppetPartMatrix(part);
                        for(int i=0;i<p.samples.Length;i++){p.bind[i]=World(matrix,p.samples[i],part);p.locals[i]=p.bones.Select(b=>b.InverseTransformPoint(p.bind[i])).ToArray();}
                    }
                    pieces.Add(p);
                }
                for(int i=0;i<3;i++){face[i]=Create(new[]{"eyes","brows","lips"}[i],65+i);face[i].positions=new Vector3[4];face[i].mesh.vertices=face[i].positions;face[i].mesh.triangles=new[]{0,1,2,1,3,2};face[i].material.SetTexture("_MaskTex",Texture2D.whiteTexture);face[i].material.mainTexture=features;}
            }
            Piece Create(string name,int order){
                var go=new GameObject(name);go.transform.SetParent(root.transform);go.layer=Layer;go.transform.position=offset;
                var mesh=new Mesh{name=name+" shared UV topology"};mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sortingOrder=order;
                var material=new Material(Resources.Load<Shader>("ModularPuppet/PuppetShader"));material.SetTexture("_MaskTex",mask);renderer.sharedMaterial=material;
                return new Piece{mesh=mesh,renderer=renderer,material=material};
            }
            Vector3 World(Matrix4x4 matrix,Vector2 sample,J part){
                if(owner.facing<0)sample.x=(float)part["size"][0].N-sample.x;
                var canvas=matrix.MultiplyPoint3x4(sample);
                return new Vector3((canvas.x-800)/Unit,(Ground-canvas.y)/Unit,0);
            }
            Vector3 ToRender(Vector3 world)=>new Vector3(world.x-owner.Hip.x,world.y,0);
            public Rect PortraitUV {
                get {
                    var head=pieces.First(p=>p.part["id"].S=="head-neck");
                    var position=World(owner.PuppetPartMatrix(head.part),new Vector2(235,150),head.part);
                    float u=(position.x-owner.Hip.x+1.3f)/2.6f-.04f*owner.facing;
                    float v=(position.y+.2f)/2.6f+.04f;
                    return new Rect(Mathf.Clamp(u-.17f,0,.66f),Mathf.Clamp(v-.17f,0,.66f),.34f,.34f);
                }
            }
            public void Render(){
                int current=owner.PuppetOutfit+owner.PuppetEyes*3+owner.PuppetBrows*9+owner.PuppetLips*27;
                if(frame==Time.frameCount&&style==current)return;frame=Time.frameCount;style=current;
                float target=Mathf.Clamp(-owner.pelvis.rb.linearVelocity.x*.045f+Mathf.Sin(owner.game.Clock*2.3f)*.012f,-.08f,.08f);
                if(!owner.game.paused)sway=Mathf.SmoothDamp(sway,target,ref swayVelocity,.2f,1,Mathf.Min(Time.deltaTime,.04f));
                foreach(var p in pieces){
                    var matrix=owner.PuppetPartMatrix(p.part);
                    p.material.mainTexture=p.part["id"].S=="head-neck"?outfits[1]:outfits[Mathf.Clamp(owner.PuppetOutfit,0,1)];
                    for(int i=0;i<p.positions.Length;i++){
                        Vector3 rigid=World(matrix,p.samples[i],p.part),world=rigid;
                        if(p.bones!=null){
                            world=Vector3.zero;
                            for(int k=0;k<p.bones.Length;k++)world+=p.bones[k].TransformPoint(p.locals[i][k])*p.weights[i][k];
                            if(p.part["id"].S=="pelvis"){
                                float t=1-p.weights[i][0];world.x+=sway*t*t;
                                MaxHemDeformation=Mathf.Max(MaxHemDeformation,Vector3.Distance(rigid,world));
                            }
                        }
                        p.positions[i]=ToRender(world);
                        if(float.IsNaN(world.x)||float.IsInfinity(world.y))Finite=false;
                    }
                    p.mesh.vertices=p.positions;p.mesh.RecalculateBounds();
                }
                var head=pieces.First(p=>p.part["id"].S=="head-neck");var hm=owner.PuppetPartMatrix(head.part);
                string[] kinds={"eyes","brows","lips"};int[] choices={owner.PuppetEyes,owner.PuppetBrows,owner.PuppetLips};
                for(int j=0;j<3;j++){
                    var rect=catalog["facial_rects"][kinds[j]];float x=(float)rect[0].N,y=(float)rect[1].N,w=(float)rect[2].N,h=(float)rect[3].N;
                    Vector2[] local={new Vector2(x,y),new Vector2(x+w,y),new Vector2(x,y+h),new Vector2(x+w,y+h)};
                    for(int i=0;i<4;i++)face[j].positions[i]=ToRender(World(hm,local[i],head.part));
                    var a=catalog["features"][kinds[j]][Mathf.Clamp(choices[j],0,2)];float u=(float)a[0].N,v=(float)a[1].N,uw=(float)a[2].N,vh=(float)a[3].N;
                    face[j].mesh.vertices=face[j].positions;face[j].mesh.uv=new[]{new Vector2(u,v+vh),new Vector2(u+uw,v+vh),new Vector2(u,v),new Vector2(u+uw,v)};face[j].mesh.RecalculateBounds();
                }
                camera.Render();
            }
            public void Wireframe(){
                foreach(var p in pieces.Where(p=>p.bones!=null)){
                    int[] tris=p.mesh.triangles;
                    for(int i=0;i<tris.Length;i+=3)for(int k=0;k<3;k++){
                        var a=p.positions[tris[i+k]];var b=p.positions[tris[i+(k+1)%3]];
                        owner.game.Line(ScreenPoint(new Vector2(a.x+owner.Hip.x,a.y)),ScreenPoint(new Vector2(b.x+owner.Hip.x,b.y)),.35f,new Color(.13f,.5f,.48f,.6f));
                    }
                }
            }
            public J Probe()=>J.O("vertices",VertexCount,"mixed_weight_vertices",MixedVertices,"weight_error",MaxWeightError,"max_hem_deformation_world",MaxHemDeformation,"finite",Finite,"mesh_renderers",pieces.Count+3,"shared_uv_template",true);
            public void Dispose(){
                foreach(var p in pieces.Concat(face.Where(p=>p!=null))){UnityEngine.Object.Destroy(p.mesh);UnityEngine.Object.Destroy(p.material);}
                if(Texture!=null){Texture.Release();UnityEngine.Object.Destroy(Texture);Texture=null;}
            }
        }
    }
}

public sealed class ModularPuppetLifetime:MonoBehaviour {
    public Action Release;
    void OnDestroy(){Release?.Invoke();Release=null;}
}
