using UnityEngine;

[DisallowMultipleComponent]
public sealed class BonfireFXController : MonoBehaviour
{
    [SerializeField] private Light keyLight;
    [SerializeField] private Light emberLight;
    // A real campfire lights the whole clearing: ~14m of warm light, soft shadows.
    [SerializeField, Min(0f)] private float keyIntensity = 9f;
    [SerializeField, Min(0f)] private float emberIntensity = 2.4f;
    [SerializeField, Min(0f)] private float keyRange = 14f;
    [SerializeField, Range(0f, 1f)] private float flickerAmount = 0.18f;
    [SerializeField, Min(0.1f)] private float flickerSpeed = 8.2f;

    private float seed;
    private float smoothedPulse = 1f;

    private void Awake()
    {
        seed = transform.position.x * 3.731f + transform.position.z * 5.179f;
        // Flickering soft shadows from the trees, logs and the player sell the fire
        // as the light source of the scene.
        if (keyLight != null)
        {
            keyLight.shadows = LightShadows.Soft;
            keyLight.shadowStrength = 0.85f;
            keyLight.shadowNearPlane = 0.2f;
        }
    }

    private void Update()
    {
        float t = Time.time * flickerSpeed;
        float broad = Mathf.PerlinNoise(seed, t * 0.22f) * 2f - 1f;
        float detail = Mathf.PerlinNoise(seed + 17.3f, t * 0.71f) * 2f - 1f;
        float fast = Mathf.PerlinNoise(seed + 43f, t * 1.7f) * 2f - 1f;
        float flutter = broad * .48f + detail * .37f + fast * .15f;
        float target = 1f + flutter * .65f;
        smoothedPulse = Mathf.Lerp(smoothedPulse, target, 1f - Mathf.Exp(-14f * Time.deltaTime));
        float pulse = smoothedPulse;

        if (keyLight != null)
        {
            keyLight.intensity = keyIntensity * pulse;
            keyLight.range = keyRange;
            keyLight.color = Color.Lerp(
                new Color(1f, 0.39f, 0.10f),
                new Color(1f, 0.52f, 0.18f),
                Mathf.InverseLerp(-1f, 1f, broad));
        }

        if (emberLight != null)
        {
            emberLight.intensity = emberIntensity * (1f + detail * flickerAmount * 0.7f);
            emberLight.range = keyRange * 0.4f;
        }
    }

    public void Configure(Light mainLight, Light fillLight)
    {
        keyLight = mainLight;
        emberLight = fillLight;
    }
}
