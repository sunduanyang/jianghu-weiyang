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
    readonly bool original,singleHead,literalSourceParts;
    readonly Texture2D[] outfitTextures,outfitMasks;
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
    CharacterPose calibration;
    float outputScale;
    ulong profileFingerprint;
    bool explicitCalibration;
    int rebindCount,renderRevision,measuredRevision=-1,materialStyle=-1,materialOutfit=-1;
    Vector2 eyeCenter;
    Vector3 eyeCenterLocal;
    bool hasFeatures;
    static readonly string[] FeatureNames={"eyes","brows","lips"};
    static readonly string[] BoneNames={"pelvis","belly","torso","neck","head","upperNear","forearmNear","handNear","upperFar","forearmFar","handFar","thighNear","shinNear","footNear","thighFar","shinFar","footFar"};
    readonly Vector2[][][] featureUVs=new Vector2[3][][];
    static readonly int ClipId=Shader.PropertyToID("_ClipTex"),ColourMaskId=Shader.PropertyToID("_ColorMask"),SkinId=Shader.PropertyToID("_Skin"),HairId=Shader.PropertyToID("_Hair"),PrimaryId=Shader.PropertyToID("_Primary"),SecondaryId=Shader.PropertyToID("_Secondary"),HeadId=Shader.PropertyToID("_Head");
    public RenderTexture Texture {get;private set;}
    public Rect PortraitUV {get;private set;}
    public Rect FrameRect {get;private set;}=new Rect(-1.3f,-.2f,2.6f,2.6f);
    public int Vertices=>body.Sum(p=>p.points.Length)+head.points.Length+(features[0].renderer.enabled?12:0);
    public int NeckLayer=>head.renderer.sortingOrder;
    public int CollarLayer=>body.First(p=>p.part["id"].S=="torso").renderer.sortingOrder;
    sealed class Piece {
        public Mesh mesh;public MeshRenderer renderer;public Material material;public J part,definition;
        public Vector2[] points;public Vector3[] vertices;public float[][] weights;public string[] bones;public Vector3[][] locals;
        public Vector3[] bind,rigidLocals,rigid,candidate;
        public string primary;public Matrix4x4 bindMatrix;
        public bool primaryOverride;public Vector3 attachmentLocal;
        public Matrix4x4 bindPrimaryInverse,currentPrimary;
        public Matrix4x4[] frames;
        public int primaryIndex,featureChoice=-1;
        public Vector2[] featureCorners;
        public CharacterSkinStrain.Triangle[] strainTriangles;
        public float minimumStretch=1,maximumStretch=1;
        public int[] triangles;public bool[] rigidTriangles;
        public bool flexible;public float flexLimit,flexBlend=1,maxDisplacement,edgeError,areaError,minAreaRatio=1,attachmentError;
        public int inverted,rigidVertices,mixedVertices;public float weightError;
        public float bindScaleRatio=1,bindShear,footHeelError,footTipError;
        public string uvHash,topologyHash;
    }
    public static J Catalog {get{if(catalog==null)catalog=J.Parse(Resources.Load<TextAsset>("Characters/index").text);return catalog;}}
    Texture2D Source(string key){string path=resourcePath+"/"+key;if(!sources.TryGetValue(path,out var t)){t=Resources.Load<Texture2D>(path);if(t==null)throw new InvalidOperationException("Missing character module: "+path);sources[path]=t;}return t;}
    bool Original=>original;
    float HeadVisualScale=>Original?1:look.HeadScale;
    int GeometryKey=>literalSourceParts?0:look.Head*1000+look.Width*100+look.Sex*10+look.Stage;
    J HeadDefinition=>definition["heads"][singleHead?0:look.Head];
    public CharacterRenderer(int resolution=512,Transform parent=null,string asset=""){
        resourcePath=CharacterCastCatalog.ResourcePath(asset);
        var sourceDefinition=resourcePath=="Characters"?null:Resources.Load<TextAsset>(resourcePath+"/index");
        if(resourcePath!="Characters"&&sourceDefinition==null)throw new InvalidOperationException("Missing character definition: "+resourcePath+"/index");
        definition=resourcePath=="Characters"?Catalog:J.Parse(sourceDefinition.text);
        original=definition["preserve_source_colour"].B;singleHead=definition["single_head"].B;literalSourceParts=definition["literal_source_parts"].B;
        if(!Original){outfitTextures=new[]{Source("cloud"),Source("jade")};outfitMasks=new[]{Source("cloud-mask"),Source("jade-mask")};}
        root=new GameObject("Character modules · shared UV");if(parent!=null)root.transform.SetParent(parent);
        offset=new Vector3(4000+16*nextSlot++,4000,0);
        var c=new GameObject("Character alpha camera");c.transform.SetParent(root.transform);c.transform.position=offset+new Vector3(0,1.1f,-5);
        camera=c.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.3f;camera.aspect=1;camera.cullingMask=1<<29;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.nearClipPlane=.1f;camera.farClipPlane=10;camera.allowHDR=false;camera.allowMSAA=true;
        Texture=new RenderTexture(resolution,resolution,16,RenderTextureFormat.ARGB32){name="Character mesh surface",antiAliasing=4};Texture.Create();camera.targetTexture=Texture;
        foreach(J d in definition["body_meshes"].A){
            var p=Create(d["part"]["id"].S,d["part"]["layer"].I,offset);p.part=d["part"];p.definition=d;
            p.points=d["vertices"].A.Select(PhysicsDuel.Point).ToArray();p.vertices=new Vector3[p.points.Length];p.mesh.vertices=p.vertices;p.mesh.uv=d["uv"].A.Select(PhysicsDuel.Point).ToArray();p.mesh.triangles=d["triangles"].A.Select(v=>v.I).ToArray();
            p.weights=d["weights"].A.Select(w=>w.A.Select(v=>(float)v.N).ToArray()).ToArray();
            p.bones=d["bones"].A.Select(v=>v.S).ToArray();
            p.primaryOverride=p.part["primary_bone"].Has||d["primary_bone"].Has;
            p.primary=p.part["primary_bone"].Has?p.part["primary_bone"].S:d["primary_bone"].Has?d["primary_bone"].S:p.part["bone"].S=="spine"?"torso":p.part["bone"].S;
            p.flexible=p.bones.Length>1;
            p.material.SetTexture("_ClipTex",Original?Texture2D.whiteTexture:Source("socket-mask"));body.Add(p);
            if(Original){p.material.mainTexture=Source(p.part["texture"].S);if(!literalSourceParts)p.material.SetTexture("_PartMap",Source("part-map"));p.material.SetFloat("_SourcePart",literalSourceParts?0:p.part["source_number"].I);}
        }
        head=Create("head and neck behind collar",definition["head_layer"].I,offset);
        head.points=new Vector2[165];head.vertices=new Vector3[165];var triangles=new List<int>();
        for(int y=0;y<=14;y++)for(int x=0;x<=10;x++)head.points[y*11+x]=new Vector2(x*51.2f,y*512f/14);
        for(int y=0;y<14;y++)for(int x=0;x<10;x++){int i=y*11+x;triangles.AddRange(new[]{i,i+1,i+11,i+1,i+12,i+11});}
        head.mesh.vertices=head.vertices;head.mesh.triangles=triangles.ToArray();head.bones=literalSourceParts?new[]{"head"}:new[]{"head","neck","torso"};head.primary="head";head.flexible=!literalSourceParts;head.material.SetFloat("_Head",1);
        for(int i=0;i<3;i++){
            var p=Create(FeatureNames[i],65+i,offset);p.vertices=new Vector3[4];p.rigidLocals=new Vector3[4];p.mesh.vertices=p.vertices;p.mesh.triangles=new[]{0,1,2,1,3,2};p.material.mainTexture=Original?Texture2D.whiteTexture:Source("features");features[i]=p;
            var choices=definition["features"][FeatureNames[i]].A;featureUVs[i]=new Vector2[choices.Count][];
            for(int j=0;j<choices.Count;j++){
                var a=choices[j];float u=(float)a[0].N,v=(float)a[1].N,w=(float)a[2].N,h=(float)a[3].N;
                featureUVs[i][j]=new[]{new Vector2(u,v+h),new Vector2(u+w,v+h),new Vector2(u,v),new Vector2(u+w,v)};
            }
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
            // The physics foot can place its ankle on the heel-to-toe axis.
            // Solving three points would then flatten the entire painted shoe.
            // Fit its heel-to-toe direction and one uniform scale, with the
            // authored ankle anchored exactly to the physical ankle. The third
            // reference point is diagnostic, never a shear/height constraint.
            source=tip-h;delta=b-heel;
            Vector2 q=new Vector2(-source.y,source.x),tq=new Vector2(-delta.y,delta.x);float sq=source.sqrMagnitude;
            if(sq<.000001f)throw new InvalidOperationException("A shoe needs a nonzero authored heel-to-toe axis");
            m.m00=(delta.x*source.x+tq.x*q.x)/sq;m.m01=(delta.x*source.y+tq.x*q.y)/sq;
            m.m10=(delta.y*source.x+tq.y*q.x)/sq;m.m11=(delta.y*source.y+tq.y*q.y)/sq;
        }else{
            float breadth=part["breadth"].Has?(float)part["breadth"].N:1;if(bone=="spine"&&look.Sex==0&&!literalSourceParts)breadth*=1.06f;
            Vector2 q=new Vector2(-source.y,source.x),tq=new Vector2(-delta.y,delta.x)*breadth;float sq=source.sqrMagnitude;
            m.m00=(delta.x*source.x+tq.x*q.x)/sq;m.m01=(delta.x*source.y+tq.x*q.y)/sq;m.m10=(delta.y*source.x+tq.y*q.x)/sq;m.m11=(delta.y*source.y+tq.y*q.y)/sq;
        }
        m.m03=a.x-m.m00*pivot.x-m.m01*pivot.y;m.m13=a.y-m.m10*pivot.x-m.m11*pivot.y;return m;
    }
    static Vector3 Map(Matrix4x4 m,Vector2 point,J part,bool left){if(left)point.x=(float)part["size"][0].N-point.x;var v=m.MultiplyPoint3x4(point);return new Vector3(v.x,-v.y,0);}
    static float Ease(float start,float end,float value){float t=Mathf.Clamp01((value-start)/(end-start));return t*t*(3-2*t);}
    static string Hash(IEnumerable<int> values){uint hash=2166136261;unchecked{foreach(int value in values){hash^=(uint)value;hash*=16777619;}}return hash.ToString("x8");}
    Vector3 RigidPoint(Piece p,Vector2 point){
        var source=Map(p.bindMatrix,point,p.part,boundLeft);
        return p.currentPrimary.MultiplyPoint3x4(p.bindPrimaryInverse.MultiplyPoint3x4(source));
    }
    static ulong MixProfile(ulong hash,float value){unchecked{return(hash^(uint)value.GetHashCode())*1099511628211UL;}}
    static ulong ProfileFingerprint(CharacterPose bind,bool left){
        ulong hash=left?1469598103934665603UL:1469598103934665604UL;float sign=bind.Left==left?1:-1;
        // A fixed name order makes equivalent dictionaries the same profile.
        foreach(string name in BoneNames){
            var bone=bind.Bones[name];hash=MixProfile(hash,bone.A.x*sign);hash=MixProfile(hash,bone.A.y);
            hash=MixProfile(hash,bone.B.x*sign);hash=MixProfile(hash,bone.B.y);
        }
        hash=MixProfile(hash,bind.FootNear.x*sign);hash=MixProfile(hash,bind.FootNear.y);
        hash=MixProfile(hash,bind.FootFar.x*sign);return MixProfile(hash,bind.FootFar.y);
    }
    bool SelectCalibration(){
        var supplied=pose.BindPose;
        if(supplied!=null){
            ulong fingerprint=ProfileFingerprint(supplied,pose.Left);
            if(calibration==null||!explicitCalibration||fingerprint!=profileFingerprint){
                calibration=supplied.Snapshot(pose.Left);profileFingerprint=fingerprint;explicitCalibration=true;return true;
            }
        }else if(calibration==null){
            calibration=pose.Snapshot();profileFingerprint=ProfileFingerprint(calibration,pose.Left);return true;
        }else if(calibration.Left!=pose.Left){
            calibration=calibration.Snapshot(pose.Left);profileFingerprint=ProfileFingerprint(calibration,pose.Left);return true;
        }
        return false;
    }
    void LocalizeWeights(Piece p){
        if(!p.flexible||p==head)return;
        float width=(float)p.part["size"][0].N,height=(float)p.part["size"][1].N;
        for(int i=0;i<p.points.Length;i++){
            float x=p.points[i].x/width,y=p.points[i].y/height;
            if(p.part["bone"].S=="spine"){
                // The carved chest stays on the chest bone. The waist is the
                // only transition, rather than blending the entire coat.
                float seam=Ease(.62f,.94f,y),low=Ease(.82f,1,y);
                p.weights[i]=new[]{seam*low,seam*(1-low),1-seam};
            }else if(p.bones.Length==3&&p.bones[1].StartsWith("thigh")){
                // Most of the skirt is a rigid pelvis plate. Only its lower
                // edge borrows a small amount of the two leg rotations.
                float hem=Ease(.38f,.96f,y)*.32f;
                p.weights[i]=new[]{1-hem,hem*(1-x),hem*x};
            }
        }
    }
    void BindPiece(Piece p,CharacterPose bind){
        p.bindMatrix=PartMatrix(p.part,bind);
        if(p==head){
            // The adult head atlas was calibrated at the same bone span as
            // the child atlas. Reduce the whole painted head and its facial
            // layers uniformly around the collar; the neck still overlaps it.
            Vector2 collar=bind.Bones["neck"].A;
            var center=new Vector3(collar.x,-collar.y,0);
            p.bindMatrix=Matrix4x4.Translate(center)*Matrix4x4.Scale(new Vector3(HeadVisualScale,HeadVisualScale,1))*Matrix4x4.Translate(-center)*p.bindMatrix;
        }
        p.locals=new Vector3[p.points.Length][];
        p.bind=new Vector3[p.points.Length];p.rigidLocals=new Vector3[p.points.Length];p.rigid=new Vector3[p.points.Length];p.candidate=new Vector3[p.points.Length];
        p.triangles=p.mesh.triangles;p.rigidTriangles=new bool[p.triangles.Length/3];
        p.rigidVertices=p.mixedVertices=0;p.weightError=0;LocalizeWeights(p);
        int primary=p.primaryIndex=Array.IndexOf(p.bones,p.primary);
        if(primary<0)throw new InvalidOperationException("A painted piece needs its primary bone: "+p.part["id"].S);
        p.bindPrimaryInverse=bind.RigidFrame(p.primary).inverse;
        p.attachmentLocal=p.primaryOverride||p.primary=="neck"||p.primary=="torso"?p.bindPrimaryInverse.MultiplyPoint3x4(Map(p.bindMatrix,PhysicsDuel.Point(p.part[p.primary.StartsWith("hand")?"tip":"pivot"]),p.part,bind.Left)):Vector3.zero;
        if(p.frames==null||p.frames.Length!=p.bones.Length)p.frames=new Matrix4x4[p.bones.Length];
        for(int i=0;i<p.points.Length;i++){
            var point=Map(p.bindMatrix,p.points[i],p.part,bind.Left);p.bind[i]=point;
            p.rigidLocals[i]=p.bindPrimaryInverse.MultiplyPoint3x4(point);
            p.locals[i]=p.bones.Select(n=>bind.RigidFrame(n).inverse.MultiplyPoint3x4(point)).ToArray();
            float sum=p.weights[i].Take(p.bones.Length).Sum();p.weightError=Mathf.Max(p.weightError,Mathf.Abs(sum-1));
            if(p.weights[i].Take(p.bones.Length).Count(v=>v>.00001f)>1)p.mixedVertices++;
            if(!p.flexible||primary>=0&&p.weights[i][primary]>.99999f)p.rigidVertices++;
        }
        p.strainTriangles=CharacterSkinStrain.Bind(p.bind,p.triangles);
        for(int i=0;i<p.triangles.Length;i+=3)p.rigidTriangles[i/3]=!p.flexible||primary>=0&&Enumerable.Range(0,3).All(k=>p.weights[p.triangles[i+k]][primary]>.99999f);
        float rigScale=Vector2.Distance(bind.Bones["head"].A,bind.Bones["head"].B)/.3f;
        p.flexLimit=(p==head?.06f:p.part["bone"].S=="spine"?.08f:.10f)*rigScale;
        Vector2 columnX=new Vector2(p.bindMatrix.m00,p.bindMatrix.m10),columnY=new Vector2(p.bindMatrix.m01,p.bindMatrix.m11);
        p.bindScaleRatio=columnX.magnitude/Mathf.Max(1e-10f,columnY.magnitude);p.bindShear=Mathf.Abs(Vector2.Dot(columnX,columnY))/Mathf.Max(1e-10f,columnX.magnitude*columnY.magnitude);
        if(p.primary.StartsWith("foot")){
            p.footHeelError=Vector3.Distance(Map(p.bindMatrix,PhysicsDuel.Point(p.part["heel_pivot"]),p.part,bind.Left),bind.Bones[p.primary].A);
            p.footTipError=Vector3.Distance(Map(p.bindMatrix,PhysicsDuel.Point(p.part["tip"]),p.part,bind.Left),bind.Bones[p.primary].B);
        }
        p.uvHash=Hash(p.mesh.uv.SelectMany(v=>new[]{Mathf.RoundToInt(v.x*10000000),Mathf.RoundToInt(v.y*10000000)}));p.topologyHash=Hash(p.triangles);
    }
    void Rebind(){
        var bind=calibration;boundLeft=pose.Left;var h=HeadDefinition;
        head.part=J.O("id","head-neck","bone","head","pivot",h["pivot"],"tip",h["tip"],"size",h["size"],"breadth",Original?1:.94+look.Width*.03,"layer",35);
        if(Original){if(!literalSourceParts)head.material.SetTexture("_PartMap",Source("part-map"));head.material.SetFloat("_SourcePart",literalSourceParts?0:h["source_number"].I);}
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
            head.weights[i]=literalSourceParts?new[]{1f}:new[]{1-amount,amount*.25f,amount*.75f};
        }
        foreach(var p in body)BindPiece(p,bind);BindPiece(head,bind);
        head.material.mainTexture=Source(h["texture"].S);
        head.material.SetTexture(ColourMaskId,Original?Texture2D.blackTexture:Source(h["texture"].S+"-mask"));
        hasFeatures=!literalSourceParts&&!h["baked_features"].B;
        for(int i=0;i<features.Length;i++){
            var feature=features[i];feature.renderer.enabled=hasFeatures;feature.featureChoice=-1;
            if(!hasFeatures)continue;
            var r=h["face_rects"][FeatureNames[i]];float x=(float)r[0].N,y=(float)r[1].N,faceWidth=(float)r[2].N,faceHeight=(float)r[3].N;
            if(feature.featureCorners==null)feature.featureCorners=new Vector2[4];
            feature.featureCorners[0]=new Vector2(x,y);feature.featureCorners[1]=new Vector2(x+faceWidth,y);
            feature.featureCorners[2]=new Vector2(x,y+faceHeight);feature.featureCorners[3]=new Vector2(x+faceWidth,y+faceHeight);
            for(int j=0;j<4;j++)feature.rigidLocals[j]=head.bindPrimaryInverse.MultiplyPoint3x4(Map(head.bindMatrix,feature.featureCorners[j],head.part,bind.Left));
        }
        var eye=h["face_rects"]["eyes"];eyeCenter=new Vector2((float)eye[0].N+(float)eye[2].N*.5f,(float)eye[1].N);
        eyeCenterLocal=head.bindPrimaryInverse.MultiplyPoint3x4(Map(head.bindMatrix,eyeCenter,head.part,bind.Left));
        geometry=GeometryKey;rebindCount++;materialStyle=materialOutfit=-1;measuredRevision=-1;
    }
    void Colours(Material m,bool isHead){m.SetColor(SkinId,look.SkinColor);m.SetColor(HairId,look.HairColor);m.SetColor(PrimaryId,look.Primary);m.SetColor(SecondaryId,look.Secondary);m.SetFloat(HeadId,isHead?1:0);}
    void UpdateMaterials(){
        if(literalSourceParts)return;
        int clothing=look.Clothing;
        if(!Original&&materialOutfit!=clothing){
            foreach(var p in body){p.material.mainTexture=outfitTextures[clothing];p.material.SetTexture(ColourMaskId,outfitMasks[clothing]);}
            materialOutfit=clothing;
        }
        int style=look.Skin|(look.Hair<<4)|(look.Palette<<8)|(look.Grey<<12)|(look.Stage<<16);
        if(materialStyle==style)return;
        foreach(var p in body)Colours(p.material,false);Colours(head.material,true);materialStyle=style;
    }
    static float Cross(Vector3 a,Vector3 b)=>a.x*b.y-a.y*b.x;
    const float MinimumTriangleRatio=.20f;
    const float MinimumPrincipalStretch=.65f,MaximumPrincipalStretch=1.35f;
    float SafeSkinBlend(Piece p){
        float blend=1,max=0;
        for(int i=0;i<p.points.Length;i++)max=Mathf.Max(max,Vector3.Distance(p.rigid[i],p.candidate[i]));
        if(max>p.flexLimit)blend=p.flexLimit/max;
        double maximum=0;
        for(int i=0;i<p.strainTriangles.Length;i++){
            if(p.rigidTriangles[i])continue;
            double amount=CharacterSkinStrain.DifferentialNorm(p.strainTriangles[i],p.rigid,p.candidate);
            if(double.IsNaN(amount)||double.IsInfinity(amount))throw new InvalidOperationException("Non-finite painted-piece deformation: "+p.part["id"].S);
            maximum=Math.Max(maximum,amount);
        }
        // F(t)=R+t*dF, with R a rigid rotation. The spectral norm bound
        // guarantees all principal stretches stay in range along the entire
        // 0..t interval. This continuous certificate avoids binary-search
        // branch jumps and keeps every vertex on the same piece coefficient.
        double budget=Math.Min(1-MinimumPrincipalStretch,MaximumPrincipalStretch-1)*.995;
        if(maximum>budget)blend=Mathf.Min(blend,(float)(budget/maximum));
        return Mathf.Clamp01(blend);
    }
    void MeasurePiece(Piece p){
        p.edgeError=p.areaError=p.maxDisplacement=p.attachmentError=0;p.inverted=0;p.minAreaRatio=p.minimumStretch=p.maximumStretch=1;
        for(int i=0;i<p.points.Length;i++)p.maxDisplacement=Mathf.Max(p.maxDisplacement,Vector3.Distance(p.vertices[i]/outputScale,p.rigid[i]));
        for(int i=0;i<p.triangles.Length;i+=3){
            int a=p.triangles[i],b=p.triangles[i+1],c=p.triangles[i+2];
            float before=Cross(p.bind[b]-p.bind[a],p.bind[c]-p.bind[a]);if(Mathf.Abs(before)<1e-10f)continue;
            float after=Cross(p.vertices[b]-p.vertices[a],p.vertices[c]-p.vertices[a])/(outputScale*outputScale),ratio=after/before;
            if(ratio<=0)p.inverted++;p.minAreaRatio=Mathf.Min(p.minAreaRatio,ratio);
            CharacterSkinStrain.Stretch(p.strainTriangles[i/3],p.vertices,1/outputScale,out float minimum,out float maximum);
            p.minimumStretch=Mathf.Min(p.minimumStretch,minimum);p.maximumStretch=Mathf.Max(p.maximumStretch,maximum);
            if(!p.rigidTriangles[i/3])continue;
            p.areaError=Mathf.Max(p.areaError,Mathf.Abs(ratio-1));
            for(int k=0;k<3;k++){
                int ia=p.triangles[i+k],ib=p.triangles[i+(k+1)%3];float length=Vector3.Distance(p.bind[ia],p.bind[ib]);
                if(length>1e-6f)p.edgeError=Mathf.Max(p.edgeError,Mathf.Abs(Vector3.Distance(p.vertices[ia],p.vertices[ib])/Mathf.Abs(outputScale)/length-1));
            }
        }
        if(!p.flexible){
            Vector2 point=PhysicsDuel.Point(p.part[p.primary.StartsWith("hand")?"tip":"pivot"]);
            var expected=p.currentPrimary.MultiplyPoint3x4(p.attachmentLocal);
            p.attachmentError=Vector3.Distance(RigidPoint(p,point),expected);
        }
    }
    void UpdatePiece(Piece p){
        var main=p.currentPrimary=pose.RigidFrame(p.primary);
        var frames=p.frames;for(int i=0;i<p.bones.Length;i++)frames[i]=pose.RigidFrame(p.bones[i]);
        int primary=p.primaryIndex;
        bool detached=pose.Detached.Contains(p.primary)
            ||p.primary=="head"&&(pose.Detached.Contains("neck")||pose.Detached.Contains("torso")||pose.Detached.Contains("belly")||pose.Detached.Contains("pelvis"))
            ||p.primary=="torso"&&(pose.Detached.Contains("belly")||pose.Detached.Contains("pelvis"));
        for(int i=0;i<p.points.Length;i++){
            Vector3 position=main.MultiplyPoint3x4(p.rigidLocals[i]);p.rigid[i]=position;
            if(p.flexible&&!detached){
                position=Vector3.zero;
                for(int j=0;j<p.bones.Length;j++){
                    // A garment stays with its pelvis after a leg is severed;
                    // a cut chest or neck must not blend back to an old parent.
                    int bindIndex=pose.Detached.Contains(p.bones[j])?primary:j;
                    position+=frames[bindIndex].MultiplyPoint3x4(p.locals[i][bindIndex])*p.weights[i][j];
                }
            }
            p.candidate[i]=position;
        }
        // One coefficient applies to the whole connected painted piece. This
        // preserves continuous seams; independent per-vertex clamps can crease
        // or invert triangles even when every displacement is individually small.
        p.flexBlend=p.flexible?SafeSkinBlend(p):1;
        for(int i=0;i<p.points.Length;i++)p.vertices[i]=Vector3.LerpUnclamped(p.rigid[i],p.candidate[i],p.flexBlend)*outputScale;
        p.mesh.vertices=p.vertices;p.mesh.RecalculateBounds();
    }
    static void ExtendBounds(Piece p,ref Vector2 min,ref Vector2 max){
        if(!p.renderer.enabled)return;
        for(int i=0;i<p.vertices.Length;i++){var point=(Vector2)p.vertices[i];min=Vector2.Min(min,point-Vector2.one*.03f);max=Vector2.Max(max,point+Vector2.one*.03f);}
    }
    public void Render(CharacterLook appearance,CharacterPose frame,float displayScale=1,bool fitAll=false,bool draw=true){
        if(appearance==null||frame==null)throw new ArgumentNullException(appearance==null?nameof(appearance):nameof(frame));
        if(displayScale<=0||float.IsNaN(displayScale)||float.IsInfinity(displayScale))throw new ArgumentOutOfRangeException(nameof(displayScale));
        look=appearance;pose=frame;outputScale=displayScale;
        bool changed=SelectCalibration();if(changed||geometry!=GeometryKey||boundLeft!=pose.Left)Rebind();
        UpdateMaterials();foreach(var p in body)UpdatePiece(p);UpdatePiece(head);
        if(hasFeatures)for(int i=0;i<features.Length;i++){
            var feature=features[i];
            for(int j=0;j<4;j++)feature.vertices[j]=head.currentPrimary.MultiplyPoint3x4(feature.rigidLocals[j])*outputScale;
            int choice=Mathf.Clamp(i==0?look.Eyes:i==1?look.Brows:look.Lips,0,featureUVs[i].Length-1);
            if(feature.featureChoice!=choice){feature.mesh.uv=featureUVs[i][choice];feature.featureChoice=choice;}
            feature.mesh.vertices=feature.vertices;feature.mesh.RecalculateBounds();
        }
        FrameRect=new Rect(-1.3f,-.2f,2.6f,2.6f);
        if(fitAll){
            Vector2 min=FrameRect.min,max=FrameRect.max;
            foreach(var p in body)ExtendBounds(p,ref min,ref max);ExtendBounds(head,ref min,ref max);
            for(int i=0;i<features.Length;i++)ExtendBounds(features[i],ref min,ref max);
            float side=Mathf.Max(max.x-min.x,max.y-min.y);Vector2 middle=(min+max)*.5f;FrameRect=new Rect(middle-Vector2.one*side*.5f,Vector2.one*side);
        }
        camera.orthographicSize=FrameRect.height*.5f;camera.transform.position=offset+new Vector3(FrameRect.center.x,FrameRect.center.y,-5);
        var center=head.currentPrimary.MultiplyPoint3x4(eyeCenterLocal)*outputScale;
        float headScale=HeadVisualScale;
        float half=.17f*outputScale*headScale,px=(center.x-FrameRect.xMin)/FrameRect.width-(pose.Left?-.04f:.04f)*outputScale*headScale,py=(center.y-FrameRect.yMin)/FrameRect.height+.045f*outputScale*headScale;
        PortraitUV=new Rect(Mathf.Clamp(px-half,0,1-half*2),Mathf.Clamp(py-half,0,1-half*2),half*2,half*2);
        renderRevision++;
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
    float JointGap(bool change){
        if(pose==null||calibration==null)return 0;float max=0;
        void Compare(string parent,string child){float gap=Vector2.Distance(pose.Bones[parent].B,pose.Bones[child].A);if(change)gap=Mathf.Max(0,gap-Vector2.Distance(calibration.Bones[parent].B,calibration.Bones[child].A));max=Mathf.Max(max,gap);}
        Compare("pelvis","belly");Compare("belly","torso");Compare("torso","neck");Compare("neck","head");
        foreach(string side in new[]{"Near","Far"}){
            Compare("upper"+side,"forearm"+side);Compare("forearm"+side,"hand"+side);Compare("thigh"+side,"shin"+side);
            Vector2 ankle=side=="Near"?pose.FootNear:pose.FootFar,rest=side=="Near"?calibration.FootNear:calibration.FootFar;
            float gap=Vector2.Distance(pose.Bones["shin"+side].B,ankle);if(change)gap=Mathf.Max(0,gap-Vector2.Distance(calibration.Bones["shin"+side].B,rest));max=Mathf.Max(max,gap);
        }return max;
    }
    public J Probe(){
        if(calibration==null||renderRevision==0)return J.O("initialized",false,"calibrated_from_static_rig",false,"rebind_count",rebindCount);
        if(measuredRevision!=renderRevision){foreach(var piece in body)MeasurePiece(piece);MeasurePiece(head);measuredRevision=renderRevision;}
        var parts=body.Concat(new[]{head}).ToArray();
        return J.O("head",look?.Head??-1,"vertices",Vertices,"neck_layer",NeckLayer,"collar_layer",CollarLayer,"mesh_renderers",body.Count+1+features.Count(p=>p.renderer.enabled),"shared_uv_template",true,"art",look?.Art??"",
            "age_stage",look?.Stage??-1,"head_visual_scale",look==null?1:HeadVisualScale,"head_scale_pivot","collar","resource_path",resourcePath,"literal_source_parts",literalSourceParts,
            "rebind_count",rebindCount,"profile_fingerprint",profileFingerprint.ToString("x16"),"calibration_source",explicitCalibration?"explicit_bind_pose":"first_render_frame",
            "minimum_principal_stretch",parts.Min(p=>p.minimumStretch),"maximum_principal_stretch",parts.Max(p=>p.maximumStretch),"principal_stretch_minimum_limit",MinimumPrincipalStretch,"principal_stretch_maximum_limit",MaximumPrincipalStretch,
            "binding_strategy","frozen_rigid_plates_local_seams","calibrated_from_static_rig",calibration!=null,"rigid_vertices",parts.Sum(p=>p.rigidVertices),"mixed_weight_vertices",parts.Sum(p=>p.mixedVertices),"weight_error",parts.Max(p=>p.weightError),
            "rigid_edge_relative_error",parts.Max(p=>p.edgeError),"rigid_area_relative_error",parts.Max(p=>p.areaError),"attachment_error_world",parts.Max(p=>p.attachmentError),"seam_anchor_error_world",parts.Max(p=>p.attachmentError),
            "joint_gap_world",JointGap(false),"joint_gap_change_world",JointGap(true),"inverted_triangles",parts.Sum(p=>p.inverted),"minimum_triangle_area_ratio",parts.Min(p=>p.minAreaRatio),"minimum_triangle_area_limit",MinimumTriangleRatio,
            "flexible_max_displacement_world",parts.Max(p=>p.maxDisplacement),"piece_metrics",new J(parts.Select(p=>J.O("id",p.part["id"].S,"primary_bone",p.primary,"flexible",p.flexible,"rigid_vertices",p.rigidVertices,"mixed_weight_vertices",p.mixedVertices,
                "rigid_edge_relative_error",p.edgeError,"rigid_area_relative_error",p.areaError,"inverted_triangles",p.inverted,"minimum_triangle_area_ratio",p.minAreaRatio,"minimum_principal_stretch",p.minimumStretch,"maximum_principal_stretch",p.maximumStretch,"flexible_displacement_world",p.maxDisplacement,"flexible_limit_world",p.flexLimit,"flexible_blend",p.flexBlend,"attachment_error_world",p.attachmentError,"bind_source_scale_ratio",p.bindScaleRatio,"bind_source_shear",p.bindShear,"foot_reference_heel_error_world",p.footHeelError,"foot_reference_tip_error_world",p.footTipError,"uv_hash",p.uvHash,"topology_hash",p.topologyHash)).ToList()),
            "finite",parts.All(p=>p.vertices.All(v=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsInfinity(v.x)&&!float.IsInfinity(v.y))));
    }
    public void Dispose(){
        foreach(var p in body.Concat(new[]{head}).Concat(features)){if(p==null)continue;UnityEngine.Object.Destroy(p.mesh);UnityEngine.Object.Destroy(p.material);}
        if(Texture!=null){Texture.Release();UnityEngine.Object.Destroy(Texture);Texture=null;}UnityEngine.Object.Destroy(root);
    }
}
}
