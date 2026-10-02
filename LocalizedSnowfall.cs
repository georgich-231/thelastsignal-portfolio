using System.Collections.Generic;
using UnityEngine;

// Falling snow that follows the player.
//
// Flakes are soft billboards at real snowflake sizes (a few millimetres to
// small clumps), not large crystal meshes. A second, much finer layer drifts close
// to the camera for depth. Flakes sink out of sight into the ground, and anything
// that reaches a building's interior volume is
// removed, so it never snows indoors, while snow still falls past the windows.
[RequireComponent(typeof(ParticleSystem))]
public sealed class LocalizedSnowfall : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] float height = 9f;
    [SerializeField] float area = 36f;

    ParticleSystem snow, dust;

    static readonly Color FlakeTint = new Color(.92f, .95f, 1f, .9f);
    static Texture2D s_Flake;

    public ParticleSystem Dust => dust;

    void Awake()
    {
        if (target == null)
        {
            var player = GameObject.Find("Player");
            if (player != null) target = player.transform;
        }
        snow = GetComponent<ParticleSystem>();
        // Drawn after the volumetric fog (see AlpineWeatherLayerSetup), which would
        // otherwise fog each flake by the distance of whatever is behind it.
        int weather = LayerMask.NameToLayer("Weather");
        if (weather >= 0) gameObject.layer = weather;
        ConfigureFlakes(snow, area, .035f, .075f, 4200);
        dust = BuildDust();
    }

    void Start()
    {
        // Building volumes register in OnEnable, so gather them once everything is live.
        foreach (var system in new[] { snow, dust }) ExcludeInteriors(system);
    }

    void LateUpdate()
    {
        if (target == null) return;
        Vector3 p = target.position;
        transform.position = new Vector3(p.x, p.y + height, p.z);
        if (!dust) return;
        dust.transform.position = new Vector3(p.x, p.y + 4.5f, p.z);
        // The fine layer rides the same wind the weather sets on the main snowfall.
        var from = snow.velocityOverLifetime; var to = dust.velocityOverLifetime;
        to.enabled = true; to.space = ParticleSystemSimulationSpace.World;
        to.x = from.x.constant * .8f; to.y = -.55f; to.z = from.z.constant * .8f;
        var rate = dust.emission; rate.rateOverTime = snow.emission.rateOverTime.constant * 1.6f;
    }

    void ConfigureFlakes(ParticleSystem ps, float width, float minSize, float maxSize, int max)
    {
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 15f);
        main.startRotation3D = false;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(.82f, .87f, .95f, .75f), new Color(1f, 1f, 1f, 1f));
        main.maxParticles = max;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        var shape = ps.shape;
        shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(width, .5f, width); shape.rotation = Vector3.zero; shape.position = Vector3.zero;

        // Flakes tumble slowly around their centre while they fall.
        var spin = ps.rotationOverLifetime; spin.enabled = true; spin.separateAxes = false;
        spin.z = new ParticleSystem.MinMaxCurve(-.9f, .9f);
        var flutter = ps.noise; flutter.enabled = true; flutter.strength = .28f; flutter.frequency = .45f;
        flutter.scrollSpeed = .2f; flutter.octaveCount = 2; flutter.quality = ParticleSystemNoiseQuality.Medium;

        var fade = ps.colorOverLifetime; fade.enabled = true; var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .08f), new GradientAlphaKey(1, .9f), new GradientAlphaKey(0, 1) });
        fade.color = g;

        // No world collision: invisible blockers and canopy volumes killed most flakes
        // before they came near the camera. Flakes that sink below the ground are hidden
        // by it, and the interior triggers (ExcludeInteriors) keep snow out of buildings.
        var hit = ps.collision; hit.enabled = false;

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.alignment = ParticleSystemRenderSpace.View;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.minParticleSize = 0f; r.maxParticleSize = .5f;
        // Lit flakes (Particles/Simple Lit): normals bent outward like tiny spheres, so
        // the side facing the fire, a lantern or the lighter catches it even when the
        // light is behind them. With nothing lighting them they stay dark at night.
        r.normalDirection = .25f;
        if (r.sharedMaterial)
        {
            // The scene material's shader and variant (lit, transparent), with a
            // soft flake instead of a flat white crystal.
            var m = new Material(r.sharedMaterial) { name = "Snowflake (runtime)" };
            m.SetTexture("_BaseMap", Flake());
            m.SetColor("_BaseColor", FlakeTint);
            r.sharedMaterial = m;
        }
    }

    ParticleSystem BuildDust()
    {
        var go = new GameObject("Fine drifting snow");
        go.transform.SetParent(transform.parent, false);
        go.layer = gameObject.layer;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = GetComponent<ParticleSystemRenderer>().sharedMaterial;
        ConfigureFlakes(ps, 14f, .014f, .028f, 3000);
        var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f); main.startSpeed = 0f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(.85f, .9f, 1f, .45f), new Color(1f, 1f, 1f, .8f));
        var flutter = ps.noise; flutter.strength = .45f; flutter.frequency = .8f;
        ps.Play();
        return ps;
    }

    static void ExcludeInteriors(ParticleSystem ps)
    {
        if (!ps) return;
        var volumes = new List<Collider>();
        foreach (var building in FindObjectsByType<BuildingInterior>(FindObjectsSortMode.None))
        {
            if (building.interiorVolume) volumes.Add(building.interiorVolume);
            else volumes.Add(FallbackVolume(building));
            foreach (var extra in building.additionalInteriorVolumes) if (extra) volumes.Add(extra);
        }
        var trigger = ps.trigger; trigger.enabled = volumes.Count > 0;
        trigger.inside = ParticleSystemOverlapAction.Kill;
        trigger.enter = ParticleSystemOverlapAction.Kill;
        trigger.outside = ParticleSystemOverlapAction.Ignore;
        trigger.exit = ParticleSystemOverlapAction.Ignore;
        trigger.radiusScale = 1f;
        foreach (var volume in volumes) trigger.AddCollider(volume);
    }

    // Buildings without a volume collider (the forest house) describe their interior
    // as a local box; the particle trigger needs a real collider, so give it one.
    // A trigger on Ignore Raycast: invisible to the camera sweep and physics queries.
    static Collider FallbackVolume(BuildingInterior building)
    {
        const string name = "Snowfall interior volume";
        var existing = building.transform.Find(name);
        if (existing && existing.TryGetComponent<BoxCollider>(out var had)) return had;
        var go = new GameObject(name);
        go.transform.SetParent(building.transform, false);
        int layer = LayerMask.NameToLayer("Ignore Raycast");
        if (layer >= 0) go.layer = layer;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = building.interiorCenter;
        box.size = building.interiorSize;
        return box;
    }

    // A soft snowflake: a fuzzy, slightly irregular clump with a feathered edge.
    static Texture2D Flake()
    {
        if (s_Flake) return s_Flake;
        const int n = 64;
        s_Flake = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Soft snowflake", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float u = (x + .5f) / n * 2f - 1f, v = (y + .5f) / n * 2f - 1f;
            float r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
            // A soft, slightly lumpy clump rather than a drawn crystal: falling snow reads
            // as fuzzy points, and a six-armed star looks like a cartoon at any distance.
            float lump = 1f + .10f * Mathf.Sin(a * 3f + 1.3f) + .07f * Mathf.Sin(a * 5f + .4f);
            float rr = r / lump;
            float alpha = Mathf.Clamp01(Mathf.Exp(-rr * rr * 3.4f) * 1.15f - .03f);
            px[y * n + x] = new Color(1f, 1f, 1f, alpha);
        }
        s_Flake.SetPixels(px); s_Flake.Apply(true, true);
        return s_Flake;
    }
}
