using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// FlipViewRendererFeature 0x340c6b8..0x340cdc8. The original local first
// player view starts disabled; the host can enable the original P2 mirror.
public sealed class OriginalFlipViewRendererFeature : ScriptableRendererFeature {
    public Shader flipShader;
    public RenderPassEvent injectionPoint;
    public static bool FlipEnabled {get;set;}
    Material material;
    FlipPass pass;
    public override void Create() {
        CoreUtils.Destroy(material);if(flipShader==null)return;
        material=CoreUtils.CreateEngineMaterial(flipShader);pass=new FlipPass(material){renderPassEvent=injectionPoint};
    }
    public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData) {
        if(FlipEnabled&&material!=null&&renderingData.cameraData.cameraType==CameraType.Game&&renderingData.cameraData.renderType==CameraRenderType.Base)renderer.EnqueuePass(pass);
    }
    protected override void Dispose(bool disposing){CoreUtils.Destroy(material);}
    sealed class FlipPass : ScriptableRenderPass {
        readonly Material material;
        public FlipPass(Material source){material=source;profilingSampler=new ProfilingSampler("FlipView");}
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData) {
            var source=frameData.Get<UniversalResourceData>().activeColorTexture;
            var description=graph.GetTextureDesc(source);description.name="FlipViewTemp";description.clearBuffer=false;description.depthBufferBits=DepthBits.None;
            var target=graph.CreateTexture(description);
            graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source,target,material,0),passName:"FlipView_Flip");
            graph.AddCopyPass(target,source,passName:"FlipView_CopyBack");
        }
    }
}
