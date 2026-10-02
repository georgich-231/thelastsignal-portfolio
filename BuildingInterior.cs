using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BuildingInterior : MonoBehaviour
{
    public static readonly List<BuildingInterior> Active = new List<BuildingInterior>();

    [Header("Building references")]
    public BoxCollider interiorVolume;
    public Renderer roofVisual;
    public Renderer[] additionalRoofVisuals = new Renderer[0];
    public BoxCollider[] additionalInteriorVolumes = new BoxCollider[0];

    [Header("Fallback local volume")]
    [Tooltip("Used when a prefab instance has no scene reference to its volume collider.")]
    public Vector3 interiorCenter = new Vector3(0f, 0f, -0.00272f);
    public Vector3 interiorSize = new Vector3(0.015f, 0.015f, 0.00575f);

    [Header("Interior presentation")]
    [Range(0.01f, 0.35f)] public float roofOpacity = 0.035f;
    [Min(1f)] public float interiorViewSize = 3.35f;

    public bool Contains(Vector3 worldPoint)
    {
        foreach(var volume in additionalInteriorVolumes)
        {
            if(!volume || !volume.enabled || !volume.gameObject.activeInHierarchy)continue;
            var d=volume.transform.InverseTransformPoint(worldPoint)-volume.center;
            var halfSize=volume.size*.5f;
            if(Mathf.Abs(d.x)<=halfSize.x && Mathf.Abs(d.y)<=halfSize.y && Mathf.Abs(d.z)<=halfSize.z)return true;
        }
        Transform volumeTransform = interiorVolume != null ? interiorVolume.transform : transform;
        Vector3 center = interiorVolume != null ? interiorVolume.center : interiorCenter;
        Vector3 size = interiorVolume != null ? interiorVolume.size : interiorSize;
        if (interiorVolume != null && (!interiorVolume.enabled || !interiorVolume.gameObject.activeInHierarchy))
            return false;

        Vector3 localPoint = volumeTransform.InverseTransformPoint(worldPoint);
        Vector3 delta = localPoint - center;
        Vector3 half = size * 0.5f;
        return Mathf.Abs(delta.x) <= half.x &&
               Mathf.Abs(delta.y) <= half.y &&
               Mathf.Abs(delta.z) <= half.z;
    }

    public static BuildingInterior FindContaining(Transform target)
    {
        if (target == null) return null;
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            BuildingInterior building = Active[i];
            if (building != null && building.isActiveAndEnabled && building.Contains(target.position))
                return building;
        }
        return null;
    }

    void OnEnable()
    {
        if (!Active.Contains(this)) Active.Add(this);
    }

    void OnDisable()
    {
        Active.Remove(this);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Transform volumeTransform = interiorVolume != null ? interiorVolume.transform : transform;
        Vector3 center = interiorVolume != null ? interiorVolume.center : interiorCenter;
        Vector3 size = interiorVolume != null ? interiorVolume.size : interiorSize;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = volumeTransform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.22f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.8f);
        Gizmos.DrawWireCube(center, size);
        Gizmos.matrix = previous;
    }
#endif
}
