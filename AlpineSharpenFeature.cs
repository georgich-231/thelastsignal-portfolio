using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// Contrast-adaptive sharpening (after AMD FidelityFX CAS), run on the final graded
// image. Temporal AA, render scaling and the fog all soften fine detail, and far
// ridgelines and treelines lose their edge first. CAS restores edge contrast in
// proportion to how much local contrast there is, so flat sky and snow are left
// alone and it does not ring or crawl the way an unsharp mask does.
//
// Strength is set at runtime by GameGraphicsSettings (0 disables the pass).
[DisallowMultipleRendererFeature("Alpine Sharpen")]
public class AlpineSharpenFeature : ScriptableRendererFeature
{
    public static float Strength = 0.55f;

    [SerializeField] Shader shader;
    Material m_Material;
    SharpenPass m_Pass;

    public override void Create()
    {
        if (shader == null) shader = Shader.Find("Winter/Alpine Sharpen");
        m_Pass = new SharpenPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var type = renderingData.cameraData.cameraType;
        if (type == CameraType.Preview || type == CameraType.Reflection) return;
        if (renderingData.cameraData.camera.orthographic) return;   // item previews keep a transparent background
        if (Strength <= 0.001f) return;

        if (m_Material == null)
        {
            if (shader == null || !shader.isSupported) return;
            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        m_Material.SetFloat("_Sharpness", Mathf.Clamp01(Strength));
        m_Pass.Setup(m_Material);
        renderer.EnqueuePass(m_Pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_Material);
        m_Material = null;
    }

    class SharpenPass : ScriptableRenderPass
    {
        static readonly int k_BlitTexture = Shader.PropertyToID("_BlitTexture");
        static readonly int k_BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
        static readonly int k_Texel = Shader.PropertyToID("_SourceTexel");

        readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();
        Material m_Material;

        class PassData
        {
            internal Material material;
            internal MaterialPropertyBlock block;
            internal TextureHandle source;
            internal Vector4 texel;
        }

        public void Setup(Material material)
        {
            m_Material = material;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (!resources.activeColorTexture.IsValid()) return;

            var desc = renderGraph.GetTextureDesc(resources.activeColorTexture);
            desc.name = "_AlpineSharpenSource";
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;

            var source = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(resources.activeColorTexture, source, Vector2.one, Vector2.zero,
                passName: "Alpine Sharpen Copy");

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Alpine Sharpen", out var data))
            {
                data.material = m_Material;
                data.block = m_Block;
                data.source = source;
                data.texel = new Vector4(1f / desc.width, 1f / desc.height, desc.width, desc.height);
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    d.block.Clear();
                    d.block.SetTexture(k_BlitTexture, d.source);
                    d.block.SetVector(k_BlitScaleBias, new Vector4(1f, 1f, 0f, 0f));
                    d.block.SetVector(k_Texel, d.texel);
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, d.block);
                });
            }
        }
    }
}
