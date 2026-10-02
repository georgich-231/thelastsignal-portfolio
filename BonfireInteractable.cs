using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BonfireInteractable : MonoBehaviour
{
    public static readonly List<BonfireInteractable> Active = new List<BonfireInteractable>();
    public GameObject effectsRoot;
    public Light[] fireLights;
    public BonfireFXController flicker;
    public SnowHeatSource heat;
    [Min(.5f)] public float interactionRadius = 2.8f;
    public bool startsLit;
    public Vector3 InteractionCenter => GetComponentInChildren<MeshRenderer>() is MeshRenderer renderer ? renderer.bounds.center : transform.position;
    public Vector3 PromptPoint
    {
        get
        {
            var model=GetComponentInChildren<MeshRenderer>();
            if(model==null) return transform.position+Vector3.up*.9f;
            var b=model.bounds; return new Vector3(b.center.x,b.max.y+.1f,b.center.z);
        }
    }
    public bool IsLit { get; private set; }
    void Awake() { EnsureSolidCollision(); SetLit(startsLit); }
    void EnsureSolidCollision()
    {
        var model=GetComponentInChildren<MeshRenderer>();if(!model)return;
        // An upright solid volume remains reliable even when the imported root is tilted.
        var bounds=model.bounds;
        var blocker=new GameObject("Bonfire solid collision");blocker.layer=gameObject.layer;
        blocker.transform.position=bounds.center;blocker.transform.rotation=Quaternion.identity;
        blocker.transform.SetParent(transform,true);
        var capsule=blocker.AddComponent<CapsuleCollider>();capsule.direction=1;capsule.isTrigger=false;
        capsule.radius=Mathf.Max(bounds.extents.x,bounds.extents.z)*.88f;
        capsule.height=Mathf.Max(bounds.size.y, capsule.radius*2);capsule.center=Vector3.zero;
    }
    void OnEnable() { if(!Active.Contains(this)) Active.Add(this); }
    void OnDisable() { Active.Remove(this); }
    public void SetLit(bool lit)
    {
        IsLit = lit;
        if (effectsRoot != null) effectsRoot.SetActive(lit);
        if (fireLights != null) foreach (var light in fireLights) if(light != null) light.enabled = lit;
        if (flicker != null) flicker.enabled = lit;
        if (heat != null) heat.enabled = lit;
    }
    public bool TryLight(LighterItem lighter)
    {
        if (IsLit || lighter == null || !lighter.IsStored || !lighter.TryIgnite()) return false;
        SetLit(true); return true;
    }
}
