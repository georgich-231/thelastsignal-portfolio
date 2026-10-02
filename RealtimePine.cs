using UnityEngine;

// Data only: one forest manager performs the distance checks, not one Update per tree.
public sealed class RealtimePine : MonoBehaviour
{
    public Renderer[] detailed;
    public Renderer[] distant;
    public Bounds worldBounds;
    [System.NonSerialized] public int visibilityState=-1;
}
