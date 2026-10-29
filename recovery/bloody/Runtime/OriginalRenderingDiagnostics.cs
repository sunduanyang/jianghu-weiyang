using System;
using System.IO;
using System.Linq;
using Jianghu;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class OriginalRenderingDiagnostics {
    static J Vector(Vector3 value)=>J.O("x",value.x,"y",value.y,"z",value.z);
    static J BoundsData(Bounds value)=>J.O("center",Vector(value.center),"size",Vector(value.size),"min",Vector(value.min),"max",Vector(value.max));
    static string HierarchyPath(Transform value,Transform root=null) {
        if(value==null)return "missing";
        string path=value.name;
        while(value.parent!=null&&value!=root){value=value.parent;path=value.name+"/"+path;}
        return path;
    }
    static J SkinnedRenderers(Camera camera) {
        var rows=new System.Collections.Generic.List<J>();
        var planes=GeometryUtility.CalculateFrustumPlanes(camera);
        foreach(var renderer in UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include,FindObjectsSortMode.None)) {
            var rig=renderer.GetComponentInParent<OriginalRig>();if(rig==null)continue;
            var mesh=renderer.sharedMesh;
            var row=J.O("name",renderer.name,"path",HierarchyPath(renderer.transform,rig.transform),"rig",rig.name,"active",renderer.gameObject.activeInHierarchy,"active_self",renderer.gameObject.activeSelf,"enabled",renderer.enabled,"force_rendering_off",renderer.forceRenderingOff,"layer",renderer.gameObject.layer,"camera_layer_visible",(camera.cullingMask&(1<<renderer.gameObject.layer))!=0,"world_bounds",BoundsData(renderer.bounds),"local_bounds",BoundsData(renderer.localBounds),"frustum_bounds_visible",GeometryUtility.TestPlanesAABB(planes,renderer.bounds),"root_bone",HierarchyPath(renderer.rootBone,rig.transform),"local_to_world",renderer.localToWorldMatrix.ToString(),"determinant",renderer.localToWorldMatrix.determinant,"update_when_offscreen",renderer.updateWhenOffscreen,"quality",renderer.quality.ToString(),"sorting_layer",renderer.sortingLayerName,"sorting_order",renderer.sortingOrder,"mesh",mesh==null?"missing":mesh.name);
            row.D["bones"]=new J(renderer.bones.Select(b=>b==null?J.O("missing",true):J.O("path",HierarchyPath(b,rig.transform),"local_position",Vector(b.localPosition),"position",Vector(b.position),"rotation",Vector(b.eulerAngles),"scale",Vector(b.lossyScale),"screen",Vector(camera.WorldToScreenPoint(b.position)))).ToList());
            var materials=new System.Collections.Generic.List<J>();
            for(int i=0;i<renderer.sharedMaterials.Length;i++) {
                var material=renderer.sharedMaterials[i];if(material==null){materials.Add(J.O("index",i,"missing",true));continue;}
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,i);
                var globalBlock=new MaterialPropertyBlock();renderer.GetPropertyBlock(globalBlock);
                var entry=J.O("index",i,"name",material.name,"shader",material.shader.name,"supported",material.shader.isSupported,"queue",material.renderQueue,"keywords",new J(material.shaderKeywords.Select(k=>new J(k)).ToList()),"pass_names",new J(Enumerable.Range(0,material.passCount).Select(p=>new J(material.GetPassName(p))).ToList()));
                var properties=new System.Collections.Generic.List<J>();
                foreach(string key in new[]{"_MainTex","White","Color_4F6CF892","_Cull","_ZWrite","_ZTest"}) {
                    if(!material.HasProperty(key))continue;
                    var prop=J.O("name",key,"block",block.HasProperty(Shader.PropertyToID(key)),"global_block",globalBlock.HasProperty(Shader.PropertyToID(key)));
                    if(key=="_MainTex") {var texture=material.GetTexture(key);prop.D["material"]=new J(texture==null?"missing":texture.name);var t=block.GetTexture(key);prop.D["block_value"]=new J(t==null?"missing":t.name);t=globalBlock.GetTexture(key);prop.D["global_block_value"]=new J(t==null?"missing":t.name);}
                    else if(key=="White"||key=="Color_4F6CF892"){prop.D["material"]=new J(material.GetVector(key).ToString());prop.D["block_value"]=new J(block.GetVector(key).ToString());prop.D["global_block_value"]=new J(globalBlock.GetVector(key).ToString());}
                    else prop.D["material"]=new J(material.GetFloat(key));
                    properties.Add(prop);
                }
                entry.D["properties"]=new J(properties);materials.Add(entry);
            }
            row.D["materials"]=new J(materials);
            if(mesh!=null) {
                row.D["vertices"]=new J(mesh.vertexCount);row.D["vertex_colors"]=new J(mesh.colors.Length);row.D["bindposes"]=new J(mesh.bindposes.Length);row.D["blendshapes"]=new J(Enumerable.Range(0,mesh.blendShapeCount).Select(i=>J.O("index",i,"name",mesh.GetBlendShapeName(i),"weight",renderer.GetBlendShapeWeight(i))).ToList());
                Mesh baked=new Mesh();
                try {
                    var localScale=renderer.transform.localScale;var lossy=renderer.transform.lossyScale;
                    try{renderer.transform.localScale=Vector3.one;renderer.BakeMesh(baked,false);}
                    finally{renderer.transform.localScale=localScale;}
                    // BakeMesh already carries the scale from its bone matrices.
                    // Match native CwMeshModel.jba, rather than applying that
                    // inherited scale a second time during world projection.
                    var worldMatrix=renderer.localToWorldMatrix*Matrix4x4.Scale(new Vector3(lossy.x==0?0:1/lossy.x,lossy.y==0?0:1/lossy.y,lossy.z==0?0:1/lossy.z));
                    var local=baked.vertices;var world=local.Select(worldMatrix.MultiplyPoint3x4).ToArray();var screen=world.Select(camera.WorldToScreenPoint).ToArray();
                    row.D["bake_world_method"]=new J("native CwMeshModel: bake at localScale=1, localToWorld times inverse lossyScale");
                    if(world.Length>0) {
                        Bounds actual=new Bounds(world[0],Vector3.zero),projected=new Bounds(screen[0],Vector3.zero);for(int i=1;i<world.Length;i++){actual.Encapsulate(world[i]);projected.Encapsulate(screen[i]);}
                        row.D["baked_world_bounds"]=BoundsData(actual);row.D["projected_bounds"]=BoundsData(projected);
                    }
                    int clockwise=0,counterClockwise=0,edgeOn=0;var triangles=baked.triangles;
                    for(int i=0;i+2<triangles.Length;i+=3){Vector3 a=screen[triangles[i]],b=screen[triangles[i+1]],c=screen[triangles[i+2]];float signed=(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);if(Mathf.Abs(signed)<.00001f)edgeOn++;else if(signed<0)clockwise++;else counterClockwise++;}
                    row.D["projected_triangle_winding"]=J.O("clockwise",clockwise,"counter_clockwise",counterClockwise,"edge_on",edgeOn);
                    if(new[]{"Head","Hair","Eyes","Nose","Mouth","Beard","Hat"}.Contains(renderer.name,StringComparer.OrdinalIgnoreCase)) {
                        row.D["baked_world_vertices"]=new J(world.Select(Vector).ToList());row.D["screen_vertices"]=new J(screen.Select(Vector).ToList());
                        row.D["source_local_vertices"]=new J(mesh.vertices.Select(Vector).ToList());
                        row.D["source_bindposes"]=new J(mesh.bindposes.Select(v=>new J(v.ToString())).ToList());
                        row.D["source_bone_weights"]=new J(mesh.boneWeights.Select(v=>J.O("index0",v.boneIndex0,"weight0",v.weight0,"index1",v.boneIndex1,"weight1",v.weight1,"index2",v.boneIndex2,"weight2",v.weight2,"index3",v.boneIndex3,"weight3",v.weight3)).ToList());
                        row.D["source_uv"]=new J(mesh.uv.Select(v=>J.O("x",v.x,"y",v.y)).ToList());
                    }
                }
                catch(Exception error){row.D["bake_error"]=new J(error.ToString());}
                finally{UnityEngine.Object.Destroy(baked);}
            }
            rows.Add(row);
        }
        return new J(rows);
    }
    static J Target(RenderTexture value)=>value==null?J.O("target","screen"):J.O("name",value.name,"width",value.width,"height",value.height,"format",value.format.ToString(),"graphics_format",value.graphicsFormat.ToString(),"depth",value.depth,"msaa",value.antiAliasing,"sRGB",value.sRGB,"created",value.IsCreated());
    static J CameraData(Camera value) {
        var additional=value.GetComponent<UniversalAdditionalCameraData>();
        return J.O("name",value.name,"enabled",value.enabled,"active",value.gameObject.activeInHierarchy,"type",value.cameraType.ToString(),"position",value.transform.position.ToString(),"rotation",value.transform.rotation.eulerAngles.ToString(),"clear",value.clearFlags.ToString(),"background",value.backgroundColor.ToString(),"culling_mask",((uint)value.cullingMask).ToString(),"target",Target(value.targetTexture),"pixel_rect",value.pixelRect.ToString(),"rect",value.rect.ToString(),"depth",value.depth,"near",value.nearClipPlane,"far",value.farClipPlane,"fov",value.fieldOfView,"orthographic",value.orthographic,"projection",value.projectionMatrix.ToString(),"target_display",value.targetDisplay,"hdr",value.allowHDR,"msaa",value.allowMSAA,"urp_render_type",additional==null?"missing":additional.renderType.ToString(),"urp_renderer",additional==null||additional.scriptableRenderer==null?"missing":additional.scriptableRenderer.GetType().FullName,"post_processing",additional!=null&&additional.renderPostProcessing,"clear_depth",additional!=null&&additional.clearDepth);
    }
    static J DumpPaintTextures(string outputDirectory) {
        var rows=new System.Collections.Generic.List<J>();string directory=Path.Combine(outputDirectory,"paint-textures");Directory.CreateDirectory(directory);
        foreach(var paint in UnityEngine.Object.FindObjectsByType<OriginalPaintTexture>(FindObjectsInactive.Include,FindObjectsSortMode.None)) {
            if(paint.current==null)continue;var prior=RenderTexture.active;Texture2D readable=null;
            try {
                var target=paint.current;readable=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);RenderTexture.active=target;readable.ReadPixels(new Rect(0,0,target.width,target.height),0,0,false);readable.Apply(false,false);
                int transparent=0,opaque=0,minAlpha=255,maxAlpha=0;foreach(var p in readable.GetPixels32()){minAlpha=Mathf.Min(minAlpha,p.a);maxAlpha=Mathf.Max(maxAlpha,p.a);if(p.a==0)transparent++;if(p.a==255)opaque++;}
                string name=paint.name+"-"+paint.GetInstanceID()+".png";File.WriteAllBytes(Path.Combine(directory,name),readable.EncodeToPNG());
                Mesh mesh=paint.target is SkinnedMeshRenderer skinned?skinned.sharedMesh:paint.GetComponent<MeshFilter>()?.sharedMesh;
                var uv=mesh==null?Array.Empty<Vector2>():mesh.uv;
                rows.Add(J.O("name",paint.name,"file",name,"current",Target(target),"source",paint.sourceTexture==null?"null":paint.sourceTexture.name,"source_size",paint.sourceTexture==null?"null":paint.sourceTexture.width+"x"+paint.sourceTexture.height,"tint",paint.sourceColor.ToString(),"alpha_min",minAlpha,"alpha_max",maxAlpha,"transparent",transparent,"opaque",opaque,"uv_count",uv.Length,"uv_first",uv.Length==0?"missing":uv[0].ToString(),"uv_min",uv.Length==0?"missing":new Vector2(uv.Min(v=>v.x),uv.Min(v=>v.y)).ToString(),"uv_max",uv.Length==0?"missing":new Vector2(uv.Max(v=>v.x),uv.Max(v=>v.y)).ToString(),"materials",new J(paint.target.sharedMaterials.Select(m=>J.O("name",m.name,"shader",m.shader.name,"main",m.mainTexture==null?"null":m.mainTexture.name,"main_is_current",m.mainTexture==paint.current)).ToList())));
            }
            finally{RenderTexture.active=prior;if(readable!=null)UnityEngine.Object.Destroy(readable);}
        }
        return new J(rows);
    }
    static J AudioBindings() {
        var catalog=Resources.Load<OriginalVisualCatalog>("OriginalVisualCatalog");
        var objects=catalog.Data["scene"].A.Where(r=>r["script"].S=="AudioObject").ToDictionary(r=>r["source"]["file"].S+":"+r["source"]["id"].S);
        var kinds=new System.Collections.Generic.List<J>();
        foreach(string kind in new[]{"ImpactSFX","FootstepsSFX"}) {
            var references=catalog.Data["rigs"].D.Values.SelectMany(r=>r.A).Where(r=>r["script"].S==kind).SelectMany(r=>r["assets"].D.Values.Select(v=>v.S)).Where(objects.ContainsKey).Distinct().ToArray();
            var clips=references.SelectMany(r=>objects[r]["assets"].D.Where(p=>p.Key.StartsWith("audioClip[",StringComparison.Ordinal)).Select(p=>p.Value.S)).Distinct().ToArray();
            kinds.Add(J.O("kind",kind,"audio_objects",references.Length,"expected_clips",clips.Length,"clips",new J(clips.Select(key=>{var clip=catalog.Get<AudioClip>(key);return clip==null?J.O("identity",key,"missing",true):J.O("identity",key,"name",clip.name,"samples",clip.samples,"frequency",clip.frequency,"channels",clip.channels,"length",clip.length,"state",clip.loadState.ToString(),"missing",false);}).ToList())));
        }
        return new J(kinds);
    }
    struct CanvasState {public Canvas canvas;public RenderMode mode;public Camera camera;public float distance;}
    static J CaptureHeads(Camera camera,RenderTexture destination,Texture2D readable,string outputDirectory) {
        var renderers=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        var previous=renderers.Select(r=>r.forceRenderingOff).ToArray();
        int visible=0;
        try {
            for(int i=0;i<renderers.Length;i++) {
                var renderer=renderers[i];bool face=renderer.GetComponentInParent<OriginalRig>()!=null&&new[]{"Head","Hair","Eyes","Nose","Mouth","Beard","Hat"}.Contains(renderer.name,StringComparer.OrdinalIgnoreCase);
                renderer.forceRenderingOff=previous[i]||!face;if(face&&renderer.enabled&&renderer.gameObject.activeInHierarchy&&!previous[i])visible++;
            }
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=destination});
            RenderTexture.active=destination;readable.ReadPixels(new Rect(0,0,destination.width,destination.height),0,0,false);readable.Apply(false,false);File.WriteAllBytes(Path.Combine(outputDirectory,"original-heads-only.png"),readable.EncodeToPNG());
            return J.O("face_renderers",visible,"restored",true);
        }
        finally {for(int i=0;i<renderers.Length;i++)if(renderers[i]!=null)renderers[i].forceRenderingOff=previous[i];}
    }
    static J CaptureHud(Camera camera,RenderTexture destination,Texture2D readable,string outputDirectory) {
        var states=UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).Where(c=>c.isRootCanvas&&c.renderMode==RenderMode.ScreenSpaceOverlay).Select(c=>new CanvasState{canvas=c,mode=c.renderMode,camera=c.worldCamera,distance=c.planeDistance}).ToArray();
        int originalMask=camera.cullingMask;
        try {
            foreach(var state in states){foreach(var node in state.canvas.GetComponentsInChildren<Transform>(true))camera.cullingMask|=1<<node.gameObject.layer;state.canvas.renderMode=RenderMode.ScreenSpaceCamera;state.canvas.worldCamera=camera;state.canvas.planeDistance=Mathf.Clamp(state.distance,camera.nearClipPlane+.1f,camera.farClipPlane-.1f);}
            Canvas.ForceUpdateCanvases();RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=destination});
            RenderTexture.active=destination;readable.ReadPixels(new Rect(0,0,destination.width,destination.height),0,0,false);readable.Apply(false,false);File.WriteAllBytes(Path.Combine(outputDirectory,"original-camera-with-hud.png"),readable.EncodeToPNG());
            return J.O("overlay_roots",states.Length,"names",new J(states.Select(s=>new J(s.canvas.name)).ToList()),"restored",true);
        }
        finally {
            foreach(var state in states){state.canvas.renderMode=state.mode;state.canvas.worldCamera=state.camera;state.canvas.planeDistance=state.distance;}
            camera.cullingMask=originalMask;
            Canvas.ForceUpdateCanvases();
        }
    }
    public static void Capture(Camera camera,string outputDirectory) {
        Directory.CreateDirectory(outputDirectory);
        var active=RenderTexture.active;RenderTexture destination=null;Texture2D readable=null;
        var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var report=J.O("frame",Time.frameCount,"graphics_device",SystemInfo.graphicsDeviceType.ToString(),"screen_width",Screen.width,"screen_height",Screen.height,"focused",Application.isFocused,"active_render_texture",Target(active),"pipeline_asset",pipeline==null?"missing":pipeline.name,"pipeline_type",RenderPipelineManager.currentPipeline==null?"missing":RenderPipelineManager.currentPipeline.GetType().FullName,"color_space",QualitySettings.activeColorSpace.ToString(),"urp_render_scale",pipeline==null?0:pipeline.renderScale,"urp_msaa",pipeline==null?0:pipeline.msaaSampleCount,"camera",camera==null?J.O("missing",true):CameraData(camera),"all_cameras",new J(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(CameraData).ToList()));
        try {
            if(camera==null)throw new InvalidOperationException("Camera.main is missing");
            destination=new RenderTexture(Mathf.Max(1,Screen.width),Mathf.Max(1,Screen.height),24,RenderTextureFormat.ARGB32){name="Original recovery explicit camera capture",antiAliasing=1};destination.Create();
            var request=new UniversalRenderPipeline.SingleCameraRequest{destination=destination};
            bool supported=RenderPipeline.SupportsRenderRequest(camera,request);report.D["request_supported"]=new J(supported);report.D["destination"]=Target(destination);
            if(!supported)throw new InvalidOperationException("Current pipeline does not support SingleCameraRequest");
            RenderPipeline.SubmitRenderRequest(camera,request);
            readable=new Texture2D(destination.width,destination.height,TextureFormat.RGBA32,false);
            RenderTexture.active=destination;readable.ReadPixels(new Rect(0,0,destination.width,destination.height),0,0,false);readable.Apply(false,false);
            var pixels=readable.GetPixels32();int nonBlack=0,min=255,max=0;long total=0;
            foreach(var pixel in pixels){int high=Mathf.Max(pixel.r,Mathf.Max(pixel.g,pixel.b)),low=Mathf.Min(pixel.r,Mathf.Min(pixel.g,pixel.b));if(high!=0)nonBlack++;max=Mathf.Max(max,high);min=Mathf.Min(min,low);total+=pixel.r+pixel.g+pixel.b;}
            report.D["pixel_statistics"]=J.O("non_black",nonBlack,"pixels",pixels.Length,"min_channel",min,"max_channel",max,"mean_rgb",(double)total/(pixels.Length*3));
            File.WriteAllBytes(Path.Combine(outputDirectory,"original-camera-render.png"),readable.EncodeToPNG());
            report.D["paint_textures"]=DumpPaintTextures(outputDirectory);
            report.D["skinned_renderers"]=SkinnedRenderers(camera);
            report.D["audio_bindings"]=AudioBindings();
            report.D["heads_only_capture"]=CaptureHeads(camera,destination,readable,outputDirectory);
            var hud=UnityEngine.Object.FindAnyObjectByType<OriginalBattleHud>();
            report.D["hud_before_capture"]=OriginalHudDiagnostics.Capture(hud);
            report.D["hud_capture"]=CaptureHud(camera,destination,readable,outputDirectory);
            report.D["hud_after_capture"]=OriginalHudDiagnostics.Capture(hud);
        }
        catch(Exception error){report.D["error"]=new J(error.ToString());Debug.LogException(error);}
        finally {
            RenderTexture.active=active;
            if(readable!=null)UnityEngine.Object.Destroy(readable);
            if(destination!=null){destination.Release();UnityEngine.Object.Destroy(destination);}
            File.WriteAllText(Path.Combine(outputDirectory,"rendering-diagnostics.json"),report.ToString());
        }
    }
}
