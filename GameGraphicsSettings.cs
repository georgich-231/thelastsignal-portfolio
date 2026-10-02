using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Graphics quality presets, resolution and display mode, persisted in PlayerPrefs.
//
// HIGH is the authored look exactly as the render pipeline asset ships. The other
// presets scale from those captured values, so re-tuning the asset re-tunes every
// preset. The pipeline asset is CLONED before it is touched - editing the shared
// asset from Play mode would permanently rewrite project settings.
public sealed class GameGraphicsSettings : MonoBehaviour
{
    public enum Preset { Low, Medium, High, Ultra }
    public enum DisplayMode { Fullscreen, Borderless, Windowed }

    public static GameGraphicsSettings Instance { get; private set; }

    public static readonly string[] PresetNames = { "LOW", "MEDIUM", "HIGH", "ULTRA" };
    public static readonly string[] DisplayModeNames = { "FULLSCREEN", "BORDERLESS", "WINDOWED" };

    public Preset Quality { get; private set; } = Preset.High;
    public DisplayMode Mode { get; private set; } = DisplayMode.Borderless;
    public Vector2Int Resolution { get; private set; }
    public bool VSync { get; private set; } = true;
    public List<Vector2Int> Resolutions { get; } = new();

    public enum AntiAliasing { Temporal, Smaa }
    public static readonly string[] AntiAliasingNames = { "TAA", "SMAA" };
    public static readonly string[] SharpenNames = { "OFF", "LOW", "MEDIUM", "HIGH" };
    static readonly float[] SharpenAmounts = { 0f, 0.3f, 0.55f, 0.8f };

    public AntiAliasing AA { get; private set; } = AntiAliasing.Temporal;
    public int Sharpen { get; private set; } = 2;
    float nextCameraCheck;
    Camera lastCamera;

    UniversalRenderPipelineAsset authored, runtime;
    RenderPipelineAsset levelPipeline;
    float authoredShadowDistance, authoredLodBias;
    int authoredCascades, authoredShadowRes, authoredMsaa;
    Volume fogVolume;
    VolumeProfile fogProfile;
    readonly Dictionary<Terrain, (float pixelError, float treeBias, float billboard)> terrainBaseline = new();
    readonly List<(ScriptableRendererFeature feature, bool active)> featureBaseline = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() => Ensure();

    // Scene Awake runs before the bootstrap, so UI asks for the instance this way.
    public static GameGraphicsSettings Ensure()
    {
        if (Instance) return Instance;
        var go = new GameObject("Graphics Settings");
        DontDestroyOnLoad(go);
        return go.AddComponent<GameGraphicsSettings>();
    }

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        authored = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (authored)
        {
            runtime = Instantiate(authored);
            runtime.name = authored.name + " (Runtime)";
            authoredShadowDistance = authored.shadowDistance;
            authoredCascades = authored.shadowCascadeCount;
            authoredShadowRes = authored.mainLightShadowmapResolution;
            authoredMsaa = authored.msaaSampleCount;
            levelPipeline = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = runtime;
        }
        authoredLodBias = QualitySettings.lodBias;
        CaptureFeatures();

        foreach (var r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (size.x >= 1024 && !Resolutions.Contains(size)) Resolutions.Add(size);
        }
        if (Resolutions.Count == 0) Resolutions.Add(new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height));
        Resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        var native = new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
        Quality = (Preset)Mathf.Clamp(PlayerPrefs.GetInt("Graphics.Quality", (int)Preset.High), 0, 3);
        Mode = (DisplayMode)Mathf.Clamp(PlayerPrefs.GetInt("Graphics.DisplayMode", (int)DisplayMode.Borderless), 0, 2);
        Resolution = new Vector2Int(PlayerPrefs.GetInt("Graphics.Width", native.x), PlayerPrefs.GetInt("Graphics.Height", native.y));
        VSync = PlayerPrefs.GetInt("Graphics.VSync", 1) == 1;
        AA = (AntiAliasing)Mathf.Clamp(PlayerPrefs.GetInt("Graphics.AA", 0), 0, 1);
        Sharpen = Mathf.Clamp(PlayerPrefs.GetInt("Graphics.Sharpen", 2), 0, 3);

        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyQuality();
        ApplyDisplay();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyQuality();

    // ------------------------------------------------------------------ public

    public void SetQuality(Preset preset)
    {
        Quality = preset;
        PlayerPrefs.SetInt("Graphics.Quality", (int)preset);
        ApplyQuality();
    }

    public void SetDisplayMode(DisplayMode mode)
    {
        Mode = mode;
        PlayerPrefs.SetInt("Graphics.DisplayMode", (int)mode);
        ApplyDisplay();
    }

    public void SetResolution(Vector2Int size)
    {
        Resolution = size;
        PlayerPrefs.SetInt("Graphics.Width", size.x);
        PlayerPrefs.SetInt("Graphics.Height", size.y);
        ApplyDisplay();
    }

    public void SetAntiAliasing(AntiAliasing aa)
    {
        AA = aa;
        PlayerPrefs.SetInt("Graphics.AA", (int)aa);
        ApplyCamera(Camera.main);
    }

    public void SetSharpen(int level)
    {
        Sharpen = Mathf.Clamp(level, 0, 3);
        PlayerPrefs.SetInt("Graphics.Sharpen", Sharpen);
        AlpineSharpenFeature.Strength = SharpenAmounts[Sharpen];
    }

    // Story sequences can swap the active camera, so keep checking which one it is.
    void Update()
    {
        if (Time.unscaledTime < nextCameraCheck) return;
        nextCameraCheck = Time.unscaledTime + 1f;
        var cam = Camera.main;
        if (cam != lastCamera) ApplyCamera(cam);
    }

    // TAA is what removes the shimmer from distant needles, snow sparkle and thin
    // branches; the sharpening pass restores the edge crispness it costs. Its own CAS
    // is off because AlpineSharpenFeature does that job for both AA modes.
    void ApplyCamera(Camera cam)
    {
        lastCamera = cam;
        if (!cam) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (!data) return;

        if (AA == AntiAliasing.Temporal)
        {
            data.antialiasing = AntialiasingMode.TemporalAntiAliasing;
            ref var taa = ref data.taaSettings;
            taa.quality = Quality >= Preset.High ? TemporalAAQuality.VeryHigh : TemporalAAQuality.High;
            taa.baseBlendFactor = 0.88f;
            taa.varianceClampScale = 0.9f;
            taa.mipBias = Quality >= Preset.High ? -0.5f : 0f;
            taa.contrastAdaptiveSharpening = 0f;
        }
        else
        {
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
        }
    }

    public void SetVSync(bool on)
    {
        VSync = on;
        PlayerPrefs.SetInt("Graphics.VSync", on ? 1 : 0);
        QualitySettings.vSyncCount = on ? 1 : 0;
    }

    public int ResolutionIndex()
    {
        int best = 0;
        long bestError = long.MaxValue;
        for (int i = 0; i < Resolutions.Count; i++)
        {
            long e = Mathf.Abs(Resolutions[i].x - Resolution.x) + Mathf.Abs(Resolutions[i].y - Resolution.y);
            if (e < bestError) { bestError = e; best = i; }
        }
        return best;
    }

    public void Save() => PlayerPrefs.Save();

    // ----------------------------------------------------------------- display

    void ApplyDisplay()
    {
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        if (Application.isEditor) return;   // the Game view owns its own size

        var mode = Mode switch
        {
            DisplayMode.Fullscreen => FullScreenMode.ExclusiveFullScreen,
            DisplayMode.Borderless => FullScreenMode.FullScreenWindow,
            _ => FullScreenMode.Windowed,
        };

        // Borderless always covers the monitor; a lower resolution there is rendered
        // smaller and scaled up, which is the usual way to trade sharpness for speed.
        Screen.SetResolution(Resolution.x, Resolution.y, mode);
    }

    // ----------------------------------------------------------------- quality

    void ApplyQuality()
    {
        int q = (int)Quality;

        if (runtime)
        {
            runtime.renderScale = new[] { 0.75f, 0.9f, 1f, 1f }[q];
            runtime.shadowDistance = authoredShadowDistance * new[] { 0.35f, 0.6f, 1f, 1.5f }[q];
            runtime.shadowCascadeCount = Mathf.Min(authoredCascades, new[] { 2, 3, 4, 4 }[q]);
            runtime.mainLightShadowmapResolution = new[] { 1024, 2048, authoredShadowRes, Mathf.Max(authoredShadowRes, 4096) }[q];
            // Anti-aliasing is TAA or SMAA; MSAA would also block TAA and the
            // screen-space passes that read the colour buffer.
            runtime.msaaSampleCount = 1;
        }

        QualitySettings.lodBias = authoredLodBias * new[] { 0.6f, 0.8f, 1f, 1.4f }[q];
        QualitySettings.globalTextureMipmapLimit = q == 0 ? 1 : 0;
        QualitySettings.anisotropicFiltering = q == 0 ? AnisotropicFiltering.Enable : AnisotropicFiltering.ForceEnable;
        // 16x on everything above LOW: snow and rock seen at a grazing angle stay
        // sharp instead of smearing into mush a few metres out.
        Texture.SetGlobalAnisotropicFilteringLimits(q == 0 ? 4 : 16, 16);

        AlpineContactShadowsFeature.Enabled = q > 0;
        AlpineContactShadowsFeature.Steps = new[] { 8, 12, 20, 28 }[q];
        AlpineSharpenFeature.Strength = SharpenAmounts[Sharpen];
        ApplyCamera(Camera.main);

        // Ambient occlusion is the most expensive screen effect after the fog.
        foreach (var (feature, active) in featureBaseline)
        {
            if (feature && feature is ScreenSpaceAmbientOcclusion)
                feature.SetActive(active && Quality != Preset.Low);
        }

        ApplyTerrain(q);
        ApplyFog(q);
    }

    void ApplyTerrain(int q)
    {
        float errorScale = new[] { 2.6f, 1.6f, 1f, 0.7f }[q];
        float treeScale = new[] { 0.6f, 0.8f, 1f, 1.3f }[q];
        float billboardScale = new[] { 0.5f, 0.75f, 1f, 1.3f }[q];

        foreach (var terrain in Terrain.activeTerrains)
        {
            if (!terrainBaseline.TryGetValue(terrain, out var b))
            {
                b = (terrain.heightmapPixelError, terrain.treeLODBiasMultiplier, terrain.treeBillboardDistance);
                terrainBaseline[terrain] = b;
            }
            terrain.heightmapPixelError = Mathf.Clamp(b.pixelError * errorScale, 1f, 200f);
            terrain.treeLODBiasMultiplier = b.treeBias * treeScale;
            terrain.treeBillboardDistance = b.billboard * billboardScale;
        }
    }

    // A top-priority volume that overrides only the fog's raymarch step count.
    void ApplyFog(int q)
    {
        if (!fogVolume)
        {
            fogProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            fogProfile.name = "Graphics preset fog";
            var go = new GameObject("Graphics preset volume");
            go.transform.SetParent(transform, false);
            fogVolume = go.AddComponent<Volume>();
            fogVolume.isGlobal = true;
            fogVolume.priority = 10000f;
            fogVolume.sharedProfile = fogProfile;
            fogProfile.Add<AlpineFog>(true);
        }

        if (fogProfile.TryGet<AlpineFog>(out var fog))
        {
            // HIGH keeps whatever the authored atmosphere volume asks for.
            fog.steps.overrideState = Quality != Preset.High;
            fog.steps.value = new[] { 12, 18, 24, 36 }[q];
        }
    }

    // The renderer data is a shared asset, so its feature toggles are captured and
    // restored on exit.
    void CaptureFeatures()
    {
        if (!authored) return;
        var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field?.GetValue(runtime ? runtime : authored) is not ScriptableRendererData[] list) return;
        foreach (var data in list)
        {
            if (!data) continue;
            foreach (var feature in data.rendererFeatures)
                if (feature) featureBaseline.Add((feature, feature.isActive));
        }
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        foreach (var (feature, active) in featureBaseline) if (feature) feature.SetActive(active);
        foreach (var kv in terrainBaseline)
        {
            if (!kv.Key) continue;
            kv.Key.heightmapPixelError = kv.Value.pixelError;
            kv.Key.treeLODBiasMultiplier = kv.Value.treeBias;
            kv.Key.treeBillboardDistance = kv.Value.billboard;
        }
        if (runtime && QualitySettings.renderPipeline == runtime) QualitySettings.renderPipeline = levelPipeline;
        if (runtime) Destroy(runtime);
        if (fogProfile) Destroy(fogProfile);
        QualitySettings.lodBias = authoredLodBias;
        QualitySettings.globalTextureMipmapLimit = 0;
        Save();
        Instance = null;
    }
}
