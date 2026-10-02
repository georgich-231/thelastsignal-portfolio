using UnityEngine;
using UnityEngine.Rendering;

// Volume-driven atmospheric scattering for the alpine map.
// Density is authored per-area: heavy in the valley floors, thin on the ridges.
[System.Serializable]
[VolumeComponentMenu("Winter/Alpine Volumetric Fog")]
public sealed class AlpineFog : VolumeComponent
{
    [Header("Density")]
    // Extinction is per-metre: 0.0035 halves a ~200m sightline, 0.02 is already a
    // whiteout. The slider is deliberately narrow so it stays authorable.
    [Tooltip("Extinction per metre at the base height. 0 disables the effect entirely.")]
    public ClampedFloatParameter density = new ClampedFloatParameter(0f, 0f, 0.03f);

    [Tooltip("World height at which the fog reaches full density.")]
    public FloatParameter baseHeight = new FloatParameter(0f);

    [Tooltip("How quickly density falls off above the base height. Larger = a thinner, flatter fog bank.")]
    public ClampedFloatParameter heightFalloff = new ClampedFloatParameter(0.08f, 0.002f, 1f);

    [Tooltip("Furthest distance the raymarch integrates over.")]
    public ClampedFloatParameter maxDistance = new ClampedFloatParameter(600f, 20f, 4000f);

    [Tooltip("Raymarch samples. 24-32 is plenty for a soft fog; raise only if you see banding.")]
    public ClampedIntParameter steps = new ClampedIntParameter(28, 8, 96);

    [Header("Colour")]
    [Tooltip("Ambient in-scattering: the colour of the fog where no sunlight reaches it.")]
    public ColorParameter ambientColor = new ColorParameter(new Color(0.34f, 0.40f, 0.52f), true, false, true);

    [Tooltip("Tint applied to sunlight scattered towards the camera.")]
    public ColorParameter sunScatterColor = new ColorParameter(new Color(1f, 0.82f, 0.62f), true, false, true);

    [Header("Scattering")]
    [Tooltip("Forward scattering. Positive values put a bright halo around the sun.")]
    public ClampedFloatParameter anisotropy = new ClampedFloatParameter(0.55f, -0.9f, 0.9f);

    [Tooltip("Strength of sunlight scattered through the fog. This is what produces god rays.")]
    public ClampedFloatParameter shaftIntensity = new ClampedFloatParameter(1.4f, 0f, 6f);

    [Header("Movement")]
    [Tooltip("Break-up noise applied to the density field. 0 gives a perfectly smooth bank.")]
    public ClampedFloatParameter noiseStrength = new ClampedFloatParameter(0.55f, 0f, 1f);

    public ClampedFloatParameter noiseScale = new ClampedFloatParameter(0.035f, 0.001f, 0.3f);

    public ClampedFloatParameter noiseSpeed = new ClampedFloatParameter(0.6f, 0f, 6f);

    [Header("Aerial perspective")]
    [Tooltip("Lowest transmittance the fog may reach. Distance should desaturate, not " +
             "erase: a floor here keeps far ridges tonally separated from the sky.")]
    public ClampedFloatParameter minTransmittance = new ClampedFloatParameter(0.12f, 0f, 0.6f);

    [Tooltip("Fraction of the march distance applied to skybox pixels. The sky gradient " +
             "already contains its own depth cue, so full-length marching washes it out.")]
    public ClampedFloatParameter skyFogScale = new ClampedFloatParameter(0.45f, 0f, 1f);

    public bool IsActive() => density.value > 0.0001f && maxDistance.value > 0f;
}
