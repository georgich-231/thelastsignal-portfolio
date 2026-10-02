using UnityEngine;

// The procedural sky needs to know where the sun is. Rather than rely on the
// skybox pass seeing URP's light constants, we push the key light into the
// material directly and keep ambient in sync when it moves.
[ExecuteAlways]
[DisallowMultipleComponent]
public class AlpineSkyBinder : MonoBehaviour
{
    static readonly int k_SunDirection = Shader.PropertyToID("_SunDirection");
    static readonly int k_SunColor = Shader.PropertyToID("_SunColor");
    static readonly int k_SunGlowColor = Shader.PropertyToID("_SunGlowColor");

    [Tooltip("Key light. Left empty, the brightest directional light in the scene is used.")]
    public Light sun;

    [Tooltip("Sky material to drive. Left empty, the scene's skybox material is used.")]
    public Material skyMaterial;

    [Tooltip("Multiplier on the sun disc brightness relative to the light's own colour.")]
    public float discIntensity = 4f;

    [Tooltip("Warm glow around the sun and along the horizon.")]
    [ColorUsage(false, true)] public Color glowColor = new Color(1f, 0.62f, 0.34f);

    [Tooltip("Refresh the ambient probe when the sun moves. Turn off if you bake lighting.")]
    public bool updateAmbient = true;

    Vector3 m_LastDirection;
    Color m_LastColor;

    void OnEnable() => Apply();
    void OnValidate() => Apply();
    void Update() => Apply();

    public void Apply()
    {
        var material = skyMaterial != null ? skyMaterial : RenderSettings.skybox;
        if (material == null) return;

        var light = sun != null ? sun : FindKeyLight();
        if (light == null) return;

        // A directional light points along its forward axis, so the direction
        // towards the sun is the negated forward.
        Vector3 toSun = -light.transform.forward;
        Color lightColor = light.color * Mathf.Max(light.intensity, 0f);

        if (material.HasProperty(k_SunDirection))
            material.SetVector(k_SunDirection, new Vector4(toSun.x, toSun.y, toSun.z, 0f));
        if (material.HasProperty(k_SunColor))
            material.SetColor(k_SunColor, lightColor * discIntensity);
        if (material.HasProperty(k_SunGlowColor))
            material.SetColor(k_SunGlowColor, glowColor);

        if (!updateAmbient) return;

        bool moved = (toSun - m_LastDirection).sqrMagnitude > 1e-6f;
        bool recoloured = lightColor != m_LastColor;
        if (!moved && !recoloured) return;

        m_LastDirection = toSun;
        m_LastColor = lightColor;
        DynamicGI.UpdateEnvironment();
    }

    static Light FindKeyLight()
    {
        Light best = null;
        float bestIntensity = float.NegativeInfinity;

        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional || !light.isActiveAndEnabled) continue;
            if (light.intensity <= bestIntensity) continue;
            bestIntensity = light.intensity;
            best = light;
        }

        return best;
    }
}
