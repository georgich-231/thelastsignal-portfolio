using UnityEngine;

public class LowPolyFire : MonoBehaviour
{
    [SerializeField] private Light fireLight;
    [SerializeField] private Transform outerFlame;
    [SerializeField] private Transform innerFlame;
    [SerializeField] private Transform coreFlame;
    [SerializeField, Range(0f, 1f)] private float flicker = 0.22f;
    private Vector3 outerScale, innerScale, coreScale;
    private int outerAxis, innerAxis, coreAxis;
    private float seed;

    private void Awake()
    {
        if (fireLight == null) fireLight = GetComponentInChildren<Light>();
        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            if (child.name.Contains("Outer")) outerFlame = child;
            else if (child.name.Contains("Inner")) innerFlame = child;
            else if (child.name.Contains("Core")) coreFlame = child;
        }
        if (outerFlame != null) { outerScale = outerFlame.localScale; outerAxis = VerticalAxis(outerFlame); }
        if (innerFlame != null) { innerScale = innerFlame.localScale; innerAxis = VerticalAxis(innerFlame); }
        if (coreFlame != null) { coreScale = coreFlame.localScale; coreAxis = VerticalAxis(coreFlame); }
        seed = transform.position.x * 7.31f + transform.position.z * 3.17f;
    }

    private void Update()
    {
        float t = Time.time;
        float slow = Mathf.PerlinNoise(seed, t * 2.4f) * 2f - 1f;
        float fast = Mathf.PerlinNoise(seed + 19f, t * 7.5f) * 2f - 1f;
        float pulse = 1f + (slow * .7f + fast * .3f) * flicker;
        Animate(outerFlame, outerScale, outerAxis, pulse, t, 0f);
        Animate(innerFlame, innerScale, innerAxis, 2f - pulse, t, 1.7f);
        Animate(coreFlame, coreScale, coreAxis, 1f + fast * flicker * .45f, t, 3.2f);
        if (fireLight != null)
        {
            fireLight.intensity = 1.65f * Mathf.Lerp(.78f, 1.18f, Mathf.InverseLerp(-1f, 1f, slow));
            fireLight.range = 5.0f + fast * .45f;
        }
    }

    private static int VerticalAxis(Transform flame)
    {
        float x = Mathf.Abs(Vector3.Dot(flame.right, Vector3.up));
        float y = Mathf.Abs(Vector3.Dot(flame.up, Vector3.up));
        float z = Mathf.Abs(Vector3.Dot(flame.forward, Vector3.up));
        return x > y && x > z ? 0 : (y > z ? 1 : 2);
    }

    private static void Animate(Transform flame, Vector3 baseScale, int verticalAxis, float pulse, float time, float phase)
    {
        if (flame == null) return;
        float squeeze = 1f / Mathf.Sqrt(Mathf.Max(.1f, pulse));
        Vector3 scale = baseScale * squeeze;
        scale[verticalAxis] = baseScale[verticalAxis] * pulse;
        flame.localScale = scale;
        flame.localRotation = Quaternion.Euler(Mathf.Sin(time * 2.1f + phase) * 2.5f, Mathf.Sin(time * 1.3f + phase) * 5f, Mathf.Cos(time * 1.8f + phase) * 2.5f);
    }
}
