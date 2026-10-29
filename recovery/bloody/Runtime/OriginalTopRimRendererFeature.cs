using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// TopRimRendererFeature 0x340cdc8..0x340ffbc. Retains the source renderer
// lists, RG8 mask, optional blur, R8 rim field and two composite draws.
public sealed class OriginalTopRimRendererFeature : ScriptableRendererFeature {
    [Serializable] public sealed class Settings {
        public RenderPassEvent maskEvent;
        public uint renderingLayerMask,vfxRenderingLayerMask;
        public int rimPixels,maskDownsample;
        public float rimIntensity,rimDarkFloor,maskBlackThreshold,rimSoftness,maskBlurSpread;
        public Color rimColor;
    }
    public Settings settings=new Settings();
    Material maskMaterial,contourMaterial,blurMaterial,fieldMaterial,rimMaterial;
    MaskPass maskPass;FieldPass fieldPass;CompositePass compositePass;
    public override void Create() {
        foreach(var material in new[]{maskMaterial,contourMaterial,blurMaterial,fieldMaterial,rimMaterial})CoreUtils.Destroy(material);
        Material CreateMaterial(string name)=>CoreUtils.CreateEngineMaterial(Shader.Find(name));
        maskMaterial=CreateMaterial("Hidden/TopRimMask");contourMaterial=CreateMaterial("Hidden/TopRimVFXContour");
        blurMaterial=CreateMaterial("Hidden/TopRimBlur");fieldMaterial=CreateMaterial("Hidden/TopRimBlur");rimMaterial=CreateMaterial("Hidden/TopRimComposite");
        maskPass=new MaskPass(settings,maskMaterial,contourMaterial);fieldPass=new FieldPass(settings,blurMaterial,fieldMaterial);compositePass=new CompositePass(settings,rimMaterial);
    }
    public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData) {
        if(maskMaterial==null||contourMaterial==null||blurMaterial==null||fieldMaterial==null||rimMaterial==null)return;
        renderer.EnqueuePass(maskPass);renderer.EnqueuePass(fieldPass);renderer.EnqueuePass(compositePass);
    }
    protected override void Dispose(bool disposing) {
        foreach(var material in new[]{maskMaterial,contourMaterial,blurMaterial,fieldMaterial,rimMaterial})CoreUtils.Destroy(material);
    }
    sealed class FrameTextures : ContextItem {
        public TextureHandle mask,field;public int width,height;
        public override void Reset(){mask=TextureHandle.nullHandle;field=TextureHandle.nullHandle;width=height=0;}
    }
    static TextureHandle Texture(RenderGraph graph,int width,int height,GraphicsFormat format,string name,bool clear=false) {
        return graph.CreateTexture(new TextureDesc(width,height){name=name,colorFormat=format,depthBufferBits=DepthBits.None,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,clearBuffer=clear,clearColor=Color.clear});
    }
    sealed class MaskPass : ScriptableRenderPass {
        readonly Settings settings;readonly Material mask,contour;
        sealed class Data {public RendererListHandle characters,vfx;public float threshold;}
        public MaskPass(Settings value,Material maskMaterial,Material contourMaterial){settings=value;mask=maskMaterial;contour=contourMaterial;renderPassEvent=settings.maskEvent;}
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData) {
            var camera=frameData.Get<UniversalCameraData>();var rendering=frameData.Get<UniversalRenderingData>();var lights=frameData.Get<UniversalLightData>();
            int downsample=Mathf.Max(1,settings.maskDownsample),width=Mathf.Max(1,camera.cameraTargetDescriptor.width/downsample),height=Mathf.Max(1,camera.cameraTargetDescriptor.height/downsample);
            var textures=frameData.GetOrCreate<FrameTextures>();textures.width=width;textures.height=height;textures.mask=Texture(graph,width,height,GraphicsFormat.R8G8_UNorm,"Top rim combined mask",true);
            var characters=RenderingUtils.CreateDrawingSettings(new ShaderTagId("Universal2D"),rendering,camera,lights,SortingCriteria.CommonTransparent);
            characters.overrideShader=mask.shader;characters.overrideShaderPassIndex=0;
            var characterFilter=new FilteringSettings(RenderQueueRange.all,-1,settings.renderingLayerMask);
            var vfx=RenderingUtils.CreateDrawingSettings(new List<ShaderTagId>{new ShaderTagId("SRPDefaultUnlit"),new ShaderTagId("UniversalForward"),new ShaderTagId("UniversalForwardOnly")},rendering,camera,lights,SortingCriteria.CommonTransparent);
            vfx.overrideMaterial=contour;vfx.overrideMaterialPassIndex=0;
            var vfxFilter=new FilteringSettings(RenderQueueRange.all,-1,settings.vfxRenderingLayerMask);
            using(var builder=graph.AddRasterRenderPass<Data>("Top rim combined mask",out var data)) {
                data.characters=graph.CreateRendererList(new RendererListParams(rendering.cullResults,characters,characterFilter));data.vfx=graph.CreateRendererList(new RendererListParams(rendering.cullResults,vfx,vfxFilter));data.threshold=settings.maskBlackThreshold;
                builder.UseRendererList(data.characters);builder.UseRendererList(data.vfx);builder.SetRenderAttachment(textures.mask,0,AccessFlags.Write);builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((Data d,RasterGraphContext context)=>{context.cmd.SetGlobalFloat("_TopRimMaskBlackThreshold",d.threshold);context.cmd.DrawRendererList(d.characters);context.cmd.DrawRendererList(d.vfx);});
            }
        }
    }
    sealed class FieldPass : ScriptableRenderPass {
        readonly Settings settings;readonly Material blur,field;
        sealed class Data {public TextureHandle source;public Material material;public int pass,width,height;public float spread,pixels,softness;}
        public FieldPass(Settings value,Material blurMaterial,Material fieldMaterial){settings=value;blur=blurMaterial;field=fieldMaterial;renderPassEvent=(RenderPassEvent)((int)value.maskEvent+1);}
        void Draw(RenderGraph graph,TextureHandle source,TextureHandle target,Material material,int pass,FrameTextures dimensions,float pixels) {
            using(var builder=graph.AddRasterRenderPass<Data>(pass==0?"Top rim blur":"Top rim field",out var data)) {
                data.source=source;data.material=material;data.pass=pass;data.width=dimensions.width;data.height=dimensions.height;data.spread=settings.maskBlurSpread;data.softness=settings.rimSoftness;data.pixels=pixels;
                builder.UseTexture(source,AccessFlags.Read);builder.SetRenderAttachment(target,0,AccessFlags.Write);
                builder.SetRenderFunc((Data d,RasterGraphContext context)=>{
                    d.material.SetTexture("_TopRimMaskSrc",d.source);d.material.SetVector("_MaskTexelSize",new Vector4(1f/d.width,1f/d.height,d.width,d.height));
                    if(d.pass==0)d.material.SetFloat("_BlurSpread",d.spread);else{d.material.SetFloat("_RimPixels",d.pixels);d.material.SetFloat("_RimSoftness",d.softness);}
                    context.cmd.DrawProcedural(Matrix4x4.identity,d.material,d.pass,MeshTopology.Triangles,3,1);
                });
            }
        }
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData) {
            var textures=frameData.GetOrCreate<FrameTextures>();if(!textures.mask.IsValid())return;
            var camera=frameData.Get<UniversalCameraData>();var source=textures.mask;
            if(settings.maskBlurSpread>0){source=Texture(graph,textures.width,textures.height,GraphicsFormat.R8G8_UNorm,"Top rim blurred mask");Draw(graph,textures.mask,source,blur,0,textures,0);}
            textures.field=Texture(graph,textures.width,textures.height,GraphicsFormat.R8_UNorm,"Top rim field");
            float pixels=(float)camera.cameraTargetDescriptor.height/Mathf.Max(1,Screen.height)*settings.rimPixels/Mathf.Max(1,settings.maskDownsample);
            Draw(graph,source,textures.field,field,1,textures,pixels);
        }
    }
    sealed class CompositePass : ScriptableRenderPass {
        readonly Settings settings;readonly Material material;
        sealed class Data {public TextureHandle field;public Material material;public Settings settings;}
        public CompositePass(Settings value,Material source){settings=value;material=source;renderPassEvent=(RenderPassEvent)((int)value.maskEvent+2);}
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData) {
            var textures=frameData.GetOrCreate<FrameTextures>();if(!textures.field.IsValid())return;
            using(var builder=graph.AddRasterRenderPass<Data>("Top rim composite",out var data)) {
                data.field=textures.field;data.material=material;data.settings=settings;builder.UseTexture(textures.field,AccessFlags.Read);builder.SetRenderAttachment(frameData.Get<UniversalResourceData>().activeColorTexture,0,AccessFlags.ReadWrite);
                builder.SetRenderFunc((Data d,RasterGraphContext context)=>{d.material.SetTexture("_TopRimField",d.field);d.material.SetFloat("_RimIntensity",d.settings.rimIntensity);d.material.SetFloat("_RimDarkFloor",d.settings.rimDarkFloor);d.material.SetColor("_RimColor",d.settings.rimColor);context.cmd.DrawProcedural(Matrix4x4.identity,d.material,0,MeshTopology.Triangles,3,1);context.cmd.DrawProcedural(Matrix4x4.identity,d.material,1,MeshTopology.Triangles,3,1);});
            }
        }
    }
}
