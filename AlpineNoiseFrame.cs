using UnityEngine;
using UnityEngine.Rendering.Universal;

// The fog raymarch and the contact shadows hide their step count with a per-pixel
// dither. A FIXED dither pattern is invisible by day, but at night the fog is the
// brightest thing on screen and the pattern showed as speckle through every tree
// crown - and TAA cannot average away noise that is identical every frame.
//
// Under TAA the pattern now changes each frame (TAA integrates it into a smooth
// result). Under SMAA it stays fixed, because a moving pattern there would flicker.
public static class AlpineNoiseFrame
{
    static readonly int k_Frame = Shader.PropertyToID("_AlpineNoiseFrame");

    public static void Publish(Camera camera)
    {
        bool temporal = camera != null &&
                        camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) &&
                        data.antialiasing == AntialiasingMode.TemporalAntiAliasing;
        Shader.SetGlobalFloat(k_Frame, temporal ? Time.frameCount % 64 : 0f);
    }
}
