using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// Screen-space contact shadows for the sun.
//
// A cascaded shadow map is a few centimetres per texel near the camera and metres per
// texel at range, so the small-scale shadowing that grounds things - a trunk entering
// its snow collar, a rock's underside, boot prints, the character's feet, the gaps
// inside a tree crown - is lost, and far forest reads as flat cutouts. This marches
// a short ray toward the sun through the depth buffer for every lit pixel.
//
// It only removes the SUN'S share of the light: the darkening is scaled by
// direct / (direct + sky) irradiance at that pixel, so contact shadows fall to the
// same cold, sky-filled value as the real shadows beside them rather than to black.
//
// Runs after opaques and the skybox, before the fog, so distance haze still sits on top.
[DisallowMultipleRendererFeature("Alpine Contact Shadows")]
public class AlpineContactShadowsFeature : ScriptableRendererFeature
{
    public static bool Enabled = true;
    public static int Steps = 20;
    public static float Strength = 0.9f;
    public static float Length = 0.9f;        // metres near the camera; grows with distance

    [SerializeField] Shader shader;
    Material m_Material;
    ContactPass m_Pass;
    static readonly Vector3[] s_Directions = { Vector3.up, Vector3.down };
    static readonly Color[] s_Irradiance = new Color[2];

    public override void Create()
    {
        if (shader == null) shader = Shader.Find("Winter/Alpine Contact Shadows");
        m_Pass = new ContactPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var type = renderingData.cameraData.cameraType;
        if (type == CameraType.Preview || type == CameraType.Reflection) return;
        if (renderingData.cameraData.camera.orthographic) return;   // item previews keep a transparent background
        if (!Enabled || Strength <= 0f) return;

        if (m_Material == null)
        {
            if (shader == null || !shader.isSupported) return;
            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        AlpineNoiseFrame.Publish(renderingData.cameraData.camera);
        m_Material.SetFloat("_ContactSteps", Mathf.Clamp(Steps, 4, 48));
        m_Material.SetFloat("_ContactStrength", Mathf.Clamp01(Strength));
        m_Material.SetFloat("_ContactLength", Mathf.Max(0.05f, Length));

        // Sky irradiance from the ambient probe, facing up and facing down.
        var probe = RenderSettings.ambientProbe;
        probe.Evaluate(s_Directions, s_Irradiance);
        // SetVector, not SetColor: these are linear and must not be gamma-converted.
        m_Material.SetVector("_ContactSkyColor", (Vector4)(s_Irradiance[0] * RenderSettings.ambientIntensity));
        m_Material.SetVector("_ContactGroundColor", (Vector4)(s_Irradiance[1] * RenderSettings.ambientIntensity));
        m_Pass.Setup(m_Material);
        m_Pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        renderer.EnqueuePass(m_Pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_Material);
        m_Material = null;
    }

    class ContactPass : ScriptableRenderPass
    {
        static readonly int k_BlitTexture = Shader.PropertyToID("_BlitTexture");
        static readonly int k_BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");

        readonly MaterialPropertyBlock m_Block = new MaterialPropertyBlock();
        Material m_Material;

        class PassData
        {
            internal Material material;
            internal MaterialPropertyBlock block;
            internal TextureHandle source;
        }

        public void Setup(Material material)
        {
            m_Material = material;
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (!resources.cameraColor.IsValid()) return;

            var desc = renderGraph.GetTextureDesc(resources.cameraColor);
            desc.name = "_AlpineContactSource";
            desc.clearBuffer = false;

            var source = renderGraph.CreateTexture(desc);
            renderGraph.AddBlitPass(resources.cameraColor, source, Vector2.one, Vector2.zero,
                passName: "Alpine Contact Shadows Copy");

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Alpine Contact Shadows", out var data))
            {
                data.material = m_Material;
                data.block = m_Block;
                data.source = source;

                builder.UseTexture(source, AccessFlags.Read);
                if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                if (resources.cameraNormalsTexture.IsValid()) builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                // The cascaded shadow map only reaches the shader as a render graph global.
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    d.block.Clear();
                    d.block.SetTexture(k_BlitTexture, d.source);
                    d.block.SetVector(k_BlitScaleBias, new Vector4(1f, 1f, 0f, 0f));
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, d.block);
                });
            }
        }
    }
}
