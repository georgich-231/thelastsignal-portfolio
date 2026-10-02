using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class InspectablePickup : MonoBehaviour
{
    public static readonly List<InspectablePickup> Active = new List<InspectablePickup>();

    [Header("Item presentation")]
    public string displayName = "FIELD LIGHTER";
    [TextArea(2, 4)] public string description = "A reusable fuel lighter. Used for lighting fires or torches and keeping warm.";
    public Transform visualRoot;
    public Mesh inspectionMesh;
    public float interactionDistance = 2.25f;
    public float holdDuration = 1f;

    [Header("World highlight")]
    public Color highlightColor = new Color(1f, 0.48f, 0.14f, 1f);
    public float hoverHeight = 0.055f;
    public float hoverSpeed = 1.7f;

    Renderer[] renderers;
    Material[][] runtimeMaterials;
    Color[][] baseEmission;
    Vector3 baseLocalPosition;
    Quaternion baseLocalRotation;
    float focus;
    bool focused;
    bool presentationHidden;

    public Vector3 FocusPoint
    {
        get
        {
            if (renderers != null && renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                return bounds.center;
            }
            return transform.position;
        }
    }

    public Transform PresentationRoot => visualRoot != null ? visualRoot : transform;

    void Awake()
    {
        InitializePresentation();
    }

    void InitializePresentation()
    {
        if (visualRoot == null) visualRoot = transform;
        baseLocalPosition = visualRoot.localPosition;
        baseLocalRotation = visualRoot.localRotation;
        renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        runtimeMaterials = new Material[renderers.Length][];
        baseEmission = new Color[renderers.Length][];
        for (int r = 0; r < renderers.Length; r++)
        {
            Material[] originals = renderers[r].sharedMaterials;
            runtimeMaterials[r] = new Material[originals.Length];
            baseEmission[r] = new Color[originals.Length];
            for (int m = 0; m < originals.Length; m++)
            {
                if (originals[m] == null) continue;
                Material instance = new Material(originals[m]);
                instance.name = originals[m].name + " (Pickup Instance)";
                if (instance.HasProperty("_EmissionColor"))
                {
                    baseEmission[r][m] = instance.GetColor("_EmissionColor");
                    instance.EnableKeyword("_EMISSION");
                }
                runtimeMaterials[r][m] = instance;
            }
            renderers[r].materials = runtimeMaterials[r];
        }
    }

    void OnEnable()
    {
        if (!Active.Contains(this)) Active.Add(this);
    }

    void OnDisable()
    {
        Active.Remove(this);
    }

    void OnDestroy()
    {
        for (int r = 0; runtimeMaterials != null && r < runtimeMaterials.Length; r++)
            for (int m = 0; runtimeMaterials[r] != null && m < runtimeMaterials[r].Length; m++)
                if (runtimeMaterials[r][m] != null) Destroy(runtimeMaterials[r][m]);
    }

    public void SetFocused(bool value)
    {
        focused = value && !presentationHidden;
    }

    public void HideForPresentation()
    {
        presentationHidden = true;
        focused = false;
        foreach (Renderer itemRenderer in renderers) itemRenderer.enabled = false;
        foreach (Collider itemCollider in GetComponentsInChildren<Collider>(true)) itemCollider.enabled = false;
    }

    public void RestoreWorldPresentation()
    {
        presentationHidden = false; focus = 0; focused = false;
        visualRoot.localPosition = baseLocalPosition;
        visualRoot.localRotation = baseLocalRotation;
        foreach (var r in renderers) r.enabled = true;
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = true;
    }
    void Update()
    {
        // With "Reload Scene" disabled Unity can clear these non-serialized
        // caches during a script reload without invoking Awake again.
        if (visualRoot == null || renderers == null || runtimeMaterials == null ||
            baseEmission == null || runtimeMaterials.Length != renderers.Length)
            InitializePresentation();

        float target = focused ? 1f : 0f;
        focus = Mathf.MoveTowards(focus, target, Time.unscaledDeltaTime * (focused ? 5.5f : 7.5f));
        if (presentationHidden) return;

        float wave = Mathf.Sin(Time.unscaledTime * hoverSpeed * Mathf.PI * 2f);
        visualRoot.localPosition = baseLocalPosition + Vector3.up * ((wave * 0.5f + 0.5f) * hoverHeight * focus);
        visualRoot.localRotation = baseLocalRotation * Quaternion.Euler(0f, wave * 2.2f * focus, 0f);

        float pulse = Mathf.Sin(Time.unscaledTime * 2.8f) * 0.5f + 0.5f;
        pulse = pulse * pulse * (3f - 2f * pulse);
        Color emission = highlightColor * (pulse * 0.68f) * focus;
        for (int r = 0; r < renderers.Length; r++)
        {
            if (runtimeMaterials[r] == null || baseEmission[r] == null) continue;
            for (int m = 0; m < runtimeMaterials[r].Length; m++)
                if (runtimeMaterials[r][m] != null && runtimeMaterials[r][m].HasProperty("_EmissionColor"))
                    runtimeMaterials[r][m].SetColor("_EmissionColor", baseEmission[r][m] + emission);
        }
    }
}

