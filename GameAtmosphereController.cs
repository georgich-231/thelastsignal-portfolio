using System;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class GameAtmosphereController : MonoBehaviour
{
    public enum FogState { Clear, WinterMist, HeavyFog, Whiteout }

    [Serializable]
    public struct FogPreset
    {
        public Color color;
        [Range(0f, 0.08f)] public float distanceDensity;
        [Range(0f, 0.2f)] public float localDensity;
        [Range(0f, 2f)] public float noiseStrength;
        [Range(0f, 1f)] public float windSpeed;

        public FogPreset(Color color, float distanceDensity, float localDensity, float noiseStrength, float windSpeed)
        {
            this.color = color;
            this.distanceDensity = distanceDensity;
            this.localDensity = localDensity;
            this.noiseStrength = noiseStrength;
            this.windSpeed = windSpeed;
        }
    }

    [Header("Game state")]
    [SerializeField] private FogState initialState = FogState.WinterMist;
    [SerializeField, Min(0.05f)] private float transitionDuration = 3.5f;
    [Header("Fog presets")]
    [SerializeField] private FogPreset clear = new FogPreset(new Color(0.60f, 0.67f, 0.75f), 0.0015f, 0.004f, 0.25f, 0.08f);
    [SerializeField] private FogPreset winterMist = new FogPreset(new Color(0.47f, 0.55f, 0.65f), 0.0070f, 0.045f, 0.75f, 0.18f);
    [SerializeField] private FogPreset heavyFog = new FogPreset(new Color(0.43f, 0.50f, 0.59f), 0.0150f, 0.095f, 1.10f, 0.30f);
    [SerializeField] private FogPreset whiteout = new FogPreset(new Color(0.70f, 0.74f, 0.79f), 0.0290f, 0.180f, 1.45f, 0.52f);
    [Header("Raymarched local fog")]
    [SerializeField] private Transform followTarget;
    [SerializeField] private Material volumeMaterial;
    [SerializeField] private Vector3 volumeSize = new Vector3(38f, 6f, 38f);
    [SerializeField] private float volumeHeight = 2.2f;

    private Material runtimeMaterial;
    private FogPreset current;
    private FogPreset desired;
    private float blend;
    private FogState state;
    private bool originalFog;
    private Color originalColor;
    private FogMode originalMode;
    private float originalDensity;
    private Renderer fogVolume;
    private ShedNightSequence nightSequence;

    public FogState State => state;

    private void Awake()
    {
        nightSequence=FindFirstObjectByType<ShedNightSequence>();
        originalFog = RenderSettings.fog;
        originalColor = RenderSettings.fogColor;
        originalMode = RenderSettings.fogMode;
        originalDensity = RenderSettings.fogDensity;
        if (!followTarget && Camera.main) followTarget = Camera.main.transform;
        BuildFogVolume();
        current = desired = Preset(initialState);
        state = initialState;
        Apply(current);
    }

    private void Update()
    {
        if (followTarget)
        {
            Vector3 p = followTarget.position;
            transform.position = new Vector3(p.x, CursedForestSequence.Ground(p)+volumeHeight, p.z);
        }
        if (blend >= 1f) return;
        blend = Mathf.MoveTowards(blend, 1f, Time.unscaledDeltaTime / transitionDuration);
        float eased = blend * blend * (3f - 2f * blend);
        Apply(Lerp(current, desired, eased));
        if (blend >= 1f) current = desired;
    }
    private void LateUpdate()
    {
        bool night=nightSequence && nightSequence.IsNight && !nightSequence.IsDawn;
        if(fogVolume)fogVolume.enabled=night;
        if(!night)RenderSettings.fog=false;
    }

    public void SetState(FogState newState)
    {
        current = CurrentValues();
        desired = Preset(newState);
        state = newState;
        blend = 0f;
    }

    public void SetFogAmount(float amount)
    {
        amount = Mathf.Clamp01(amount);
        current = CurrentValues();
        desired = amount < 0.5f ? Lerp(clear, winterMist, amount * 2f) : Lerp(winterMist, whiteout, (amount - 0.5f) * 2f);
        blend = 0f;
    }

    private FogPreset Preset(FogState value)
    {
        switch (value)
        {
            case FogState.Clear: return clear;
            case FogState.HeavyFog: return heavyFog;
            case FogState.Whiteout: return whiteout;
            default: return winterMist;
        }
    }

    private FogPreset CurrentValues()
    {
        FogPreset value = current;
        value.color = RenderSettings.fogColor;
        value.distanceDensity = RenderSettings.fogDensity;
        if (runtimeMaterial)
        {
            value.localDensity = runtimeMaterial.GetFloat("_Density");
            value.noiseStrength = runtimeMaterial.GetFloat("_NoiseStrength");
            value.windSpeed = runtimeMaterial.GetFloat("_WindSpeed");
        }
        return value;
    }

    private static FogPreset Lerp(FogPreset a, FogPreset b, float t)
    {
        return new FogPreset(Color.Lerp(a.color, b.color, t), Mathf.Lerp(a.distanceDensity, b.distanceDensity, t),
            Mathf.Lerp(a.localDensity, b.localDensity, t), Mathf.Lerp(a.noiseStrength, b.noiseStrength, t), Mathf.Lerp(a.windSpeed, b.windSpeed, t));
    }

    private void Apply(FogPreset value)
    {
        RenderSettings.fog = value.distanceDensity > 0.0001f;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = value.color;
        RenderSettings.fogDensity = value.distanceDensity;
        if (!runtimeMaterial) return;
        runtimeMaterial.SetColor("_FogColor", value.color);
        runtimeMaterial.SetFloat("_Density", value.localDensity);
        runtimeMaterial.SetFloat("_NoiseStrength", value.noiseStrength);
        runtimeMaterial.SetFloat("_WindSpeed", value.windSpeed);
    }

    private void BuildFogVolume()
    {
        if (!volumeMaterial) return;
        runtimeMaterial = new Material(volumeMaterial) { name = volumeMaterial.name + " (Runtime)" };
        var volume = GameObject.CreatePrimitive(PrimitiveType.Cube);
        volume.name = "Raymarched ground fog volume";
        volume.transform.SetParent(transform, false);
        volume.transform.localScale = volumeSize;
        var collider = volume.GetComponent<Collider>();
        if (collider) Destroy(collider);
        var renderer = volume.GetComponent<MeshRenderer>();
        fogVolume=renderer;
        renderer.sharedMaterial = runtimeMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        if (Camera.main) Camera.main.depthTextureMode |= DepthTextureMode.Depth;
    }

    private void OnDisable()
    {
        RenderSettings.fog = originalFog;
        RenderSettings.fogColor = originalColor;
        RenderSettings.fogMode = originalMode;
        RenderSettings.fogDensity = originalDensity;
    }

    private void OnDestroy()
    {
        if (runtimeMaterial) Destroy(runtimeMaterial);
    }
}
