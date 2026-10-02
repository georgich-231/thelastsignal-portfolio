using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class WorldUprightFX : MonoBehaviour
{
    private void OnEnable() => KeepUpright();
    private void LateUpdate() => KeepUpright();

    private void KeepUpright()
    {
        if (Quaternion.Angle(transform.rotation, Quaternion.identity) > 0.01f)
            transform.rotation = Quaternion.identity;
    }
}
