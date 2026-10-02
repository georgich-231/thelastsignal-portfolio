using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;   // AddBlitPass extension
using UnityEngine.Rendering.Universal;

// Raymarched height fog with sun shafts. Driven entirely by the AlpineFog volume
// component, so density can be authored per-area instead of baked into the feature.
//
// The record/execute shape deliberately mirrors URP's own FullScreenPassRendererFeature:
// copy the active colour to a temp, then draw the fullscreen triangle back into the
// original attachment. That keeps MSAA and XR behaviour identical to the built-in path.
[DisallowMultipleRendererFeature("Alpine Volumetric Fog")]
public class VolumetricFogFeature : ScriptableRendererFeature
{
    [SerializeField] Shader fogShader;

    Material m_Material;
    FogPass m_Pass;

    public override void Create()
    {
        if (fogShader == null) fogShader = Shader.Find("Winter/Alpine Volumetric Fog");
        m_Pass = new FogPass
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection) return;
        // Item previews (orthographic, transparent background) must keep their alpha.
        if (renderingData.cameraData.camera.orthographic) return;

        var fog = VolumeManager.instance.stack.GetComponent<AlpineFog>();
        if (fog == null || !fog.IsActive()) return;

        if (m_Material == null)
        {
            if (fogShader == null || !fogShader.isSupported) return;
            m_Material = CoreUtils.CreateEngineMaterial(fogShader);
        }

        AlpineNoiseFrame.Publish(renderingData.cameraData.camera);
        m_Pass.Setup(m_Material, fog);
        m_Pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        renderer.EnqueuePass(m_Pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(m_Material);
        m_Material = null;
    }

    class FogPass : ScriptableRenderPass
    {
        static readonly int k_BlitTexture = Shader.PropertyToID("_BlitTexture");
        static readonly int k_BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");

        static readonly int k_Density = Shader.PropertyToID("_Density");
        static readonly int k_BaseHeight = Shader.PropertyToID("_BaseHeight");
        static readonly int k_HeightFalloff = Shader.PropertyToID("_HeightFalloff");
        static readonly int k_MaxDistance = Shader.PropertyToID("_MaxDistance");
        static readonly int k_Steps = Shader.PropertyToID("_Steps");
        static readonly int k_AmbientColor = Shader.PropertyToID("_AmbientColor");
        static readonly int k_SunScatterColor = Shader.PropertyToID("_SunScatterColor");
        static readonly int k_Anisotropy = Shader.PropertyToID("_Anisotropy");
        static readonly int k_ShaftIntensity = Shader.PropertyToID("_ShaftIntensity");
        static readonly int k_NoiseStrength = Shader.PropertyToID("_NoiseStrength");
        static readonly int k_NoiseScale = Shader.PropertyToID("_NoiseScale");
        static readonly int k_NoiseSpeed = Shader.PropertyToID("_NoiseSpeed");
        static readonly int k_MinTransmittance = Shader.PropertyToID("_MinTransmittance");
        static readonly int k_SkyFogScale = Shader.PropertyToID("_SkyFogScale");

        readonly MaterialPropertyBlock m_PropertyBlock = new MaterialPropertyBlock();
        Material m_Material;

        class PassData
        {
            internal Material material;
            internal MaterialPropertyBlock propertyBlock;
            internal TextureHandle source;
        }

        public void Setup(Material material, AlpineFog fog)
        {
            m_Material = material;

            // Without an intermediate target the active colour is the backbuffer,
            // which cannot be sampled and written in the same pass.
            requiresIntermediateTexture = true;

            material.SetFloat(k_Density, fog.density.value);
            material.SetFloat(k_BaseHeight, fog.baseHeight.value);
            material.SetFloat(k_HeightFalloff, fog.heightFalloff.value);
            material.SetFloat(k_MaxDistance, fog.maxDistance.value);
            material.SetFloat(k_Steps, fog.steps.value);
            material.SetColor(k_AmbientColor, fog.ambientColor.value * DaylightScale());
            material.SetColor(k_SunScatterColor, fog.sunScatterColor.value);
            material.SetFloat(k_Anisotropy, fog.anisotropy.value);
            material.SetFloat(k_ShaftIntensity, fog.shaftIntensity.value);
            material.SetFloat(k_NoiseStrength, fog.noiseStrength.value);
            material.SetFloat(k_NoiseScale, fog.noiseScale.value);
            material.SetFloat(k_NoiseSpeed, fog.noiseSpeed.value);
            material.SetFloat(k_MinTransmittance, fog.minTransmittance.value);
            material.SetFloat(k_SkyFogScale, fog.skyFogScale.value);
        }

        // The fog's ambient scatter is its own light. At night the story dims the sun
        // to ~14% but the fog kept glowing at full daytime strength, so every ridge
        // stood out as a pale silhouette against the black sky. It now follows the
        // sun's brightness (relative to the authored 3.05), falling off faster than
        // linear so night fog reads as darkness, not haze.
        const float DaySunIntensity = 3.05f;
        static Light s_Sun;
        static float DaylightScale()
        {
            if (s_Sun == null || !s_Sun.isActiveAndEnabled)
            {
                s_Sun = RenderSettings.sun;
                if (s_Sun == null)
                    foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                        if (light.type == LightType.Directional) { s_Sun = light; break; }
            }
            if (s_Sun == null) return 1f;
            float ratio = Mathf.Clamp01(s_Sun.intensity / DaySunIntensity);
            return Mathf.Max(0.02f, Mathf.Pow(ratio, 1.6f));
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resourceData = frameData.Get<UniversalResourceData>();
            if (!resourceData.cameraColor.IsValid()) return;

            var sourceDesc = renderGraph.GetTextureDesc(resourceData.cameraColor);
            sourceDesc.name = "_AlpineFogSource";
            sourceDesc.clearBuffer = false;

            var source = renderGraph.CreateTexture(sourceDesc);
            renderGraph.AddBlitPass(resourceData.cameraColor, source, Vector2.one, Vector2.zero,
                passName: "Alpine Fog Copy Colour");

            var destination = resourceData.activeColorTexture;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Alpine Volumetric Fog", out var passData))
            {
                passData.material = m_Material;
                passData.propertyBlock = m_PropertyBlock;
                passData.source = source;

                builder.UseTexture(source, AccessFlags.Read);

                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);

                // The raymarch reads the cascaded shadow map to carve sun shafts, and
                // that only reaches the shader as a render graph global.
                builder.UseAllGlobalTextures(true);

                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    Execute(context.cmd, data.propertyBlock, data.source, data.material));
            }
        }

        static void Execute(RasterCommandBuffer cmd, MaterialPropertyBlock propertyBlock, RTHandle source, Material material)
        {
            propertyBlock.Clear();
            if (source != null) propertyBlock.SetTexture(k_BlitTexture, source);

            // Required by shaders that use the core Blit.hlsl vertex stage.
            propertyBlock.SetVector(k_BlitScaleBias, new Vector4(1f, 1f, 0f, 0f));

            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, propertyBlock);
        }
    }
}
