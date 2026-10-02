using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class CameraOcclusionFade : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField, Min(0f)] private float targetHeight = 0.9f;

    [Header("Occlusion Detection")]
    [SerializeField, Min(0.05f)] private float castRadius = 0.55f;
    [SerializeField, Min(0f)] private float endpointPadding = 0.2f;
    [SerializeField] private LayerMask occluderLayers = ~0;


    [Header("Fade")]
    [SerializeField, Range(0.08f, 0.9f)] private float minimumOpacity = 0.3f;
    [SerializeField, Range(0.03f, 0.5f)] private float largeOccluderOpacity = 0.08f;
    [SerializeField, Min(0.1f)] private float adaptiveFadeStartWidth = 3.5f;
    [SerializeField, Min(0.1f)] private float adaptiveFadeFullWidth = 5f;
    [SerializeField, Min(0.1f)] private float fadeOutSpeed = 6f;
    [SerializeField, Min(0.1f)] private float fadeInSpeed = 4f;
    [Header("Shoulder camera foliage")]
    [SerializeField, Min(.1f)] private float foliageCameraRadius = .75f;
    private static readonly Vector3[] proximityDirections = {
        Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up, Vector3.down,
        new Vector3(1,1,1).normalized, new Vector3(-1,1,1).normalized,
        new Vector3(1,-1,1).normalized, new Vector3(-1,-1,1).normalized
    };

    private const int HitCapacity = 64;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int BlendId = Shader.PropertyToID("_Blend");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int SrcBlendAlphaId = Shader.PropertyToID("_SrcBlendAlpha");
    private static readonly int DstBlendAlphaId = Shader.PropertyToID("_DstBlendAlpha");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");

    private readonly RaycastHit[] hits = new RaycastHit[HitCapacity];
    private readonly Collider[] overlaps = new Collider[HitCapacity];
    private readonly HashSet<Renderer> occludedThisFrame = new HashSet<Renderer>();
    private readonly Dictionary<Renderer, FadeState> fadeStates = new Dictionary<Renderer, FadeState>();
    private readonly List<Renderer> cleanup = new List<Renderer>();
    private readonly List<Renderer[]> forestCanopies = new List<Renderer[]>();
    // World bounds of each canopy (all LODs), cached once: trees never move, and
    // rejecting far trees with one test avoids touching thousands of renderers a frame.
    private readonly List<Bounds> canopyBounds = new List<Bounds>();
    private readonly Dictionary<Mesh, CanopyMeshRaycast> canopyMeshes = new Dictionary<Mesh, CanopyMeshRaycast>();
    private readonly Dictionary<Renderer, CanopyMeshRaycast> canopyGeometry = new Dictionary<Renderer, CanopyMeshRaycast>();
    private readonly HashSet<Transform> treeRoots = new HashSet<Transform>();

    private static Bounds Encapsulate(List<Renderer> renderers)
    {
        var b=renderers[0].bounds;
        for(int i=1;i<renderers.Count;i++)b.Encapsulate(renderers[i].bounds);
        return b;
    }

    private void Start()
    {
        if(!target){var player=FindFirstObjectByType<PlayerMovement>();if(player)target=player.transform;}
        foreach(var group in FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
        {
            if(!group.transform.parent || !group.transform.parent.Find("Trunk collision"))continue;
            var renderers=new List<Renderer>();
            foreach(var lod in group.GetLODs())foreach(var renderer in lod.renderers)
                if(renderer && !renderers.Contains(renderer))renderers.Add(renderer);
            if(renderers.Count>0)
            {
                forestCanopies.Add(renderers.ToArray()); canopyBounds.Add(Encapsulate(renderers)); treeRoots.Add(group.transform.parent);
                foreach(var renderer in renderers)
                {
                    var filter=renderer.GetComponent<MeshFilter>(); var mesh=filter ? filter.sharedMesh : null;
                    if(!mesh || !mesh.isReadable)continue;
                    if(!canopyMeshes.TryGetValue(mesh,out var geometry))
                    { geometry=new CanopyMeshRaycast(mesh);canopyMeshes.Add(mesh,geometry); }
                    canopyGeometry[renderer]=geometry;
                }
            }
        }
    }

    public Transform Target
    {
        get => target;
        set => target = value;
    }

    public int ActiveOccluderCount { get; private set; }

    private sealed class FadeState
    {
        public Renderer Renderer;
        public Material[] OriginalMaterials;
        public Material[] FadeMaterials;
        public Color[] OriginalColors;
        public float MinimumOpacity;
        public float Opacity = 1f;
        public bool UsingFadeMaterials;
    }

    private void Reset()
    {
        Camera cameraComponent = GetComponent<Camera>();
        if (cameraComponent == null)
            Debug.LogWarning("CameraOcclusionFade should be placed on the top-down Camera.", this);
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        var shoulderCamera = GetComponent<DawnThirdPersonCamera>();
        if (shoulderCamera && shoulderCamera.IsActive)
        {
            FindNearbyFoliage();
            AnimateFades(Time.unscaledDeltaTime);
            return;
        }

        FindOccluders();
        AnimateFades(Time.unscaledDeltaTime);
    }

    // Only foliage touching the camera fades. Distant trees and building roofs stay solid.
    private void FindNearbyFoliage()
    {
        occludedThisFrame.Clear();
        Vector3 cameraPosition=transform.position;
        for(int c=0;c<forestCanopies.Count;c++)
        {
            var canopy=forestCanopies[c];
            if(canopyBounds[c].SqrDistance(cameraPosition)>foliageCameraRadius*foliageCameraRadius)continue;   // cheap reject: exact, the cached box contains every LOD
            if(canopy.Length==0 || !canopy[0] || !canopy[0].gameObject.activeInHierarchy)continue;
            Renderer visual=canopy[0];
            foreach(var candidate in canopy)if(candidate && candidate.isVisible){visual=candidate;break;}
            if(visual.bounds.SqrDistance(cameraPosition)>foliageCameraRadius*foliageCameraRadius)continue;
            if(!canopyGeometry.TryGetValue(visual,out var geometry))continue;
            bool touching=false;
            foreach(var direction in proximityDirections)
            {
                if(!geometry.Intersects(visual.transform,new Ray(cameraPosition,transform.TransformDirection(direction)),foliageCameraRadius))continue;
                touching=true;break;
            }
            if(touching)foreach(var renderer in canopy)AddOccluderRenderer(renderer,.08f);
        }
        ActiveOccluderCount=occludedThisFrame.Count;
    }

    private void FindOccluders()
    {
        occludedThisFrame.Clear();
        var cameraComponent = GetComponent<Camera>();
        if (!cameraComponent) return;
        var controller = target.GetComponent<CharacterController>();
        Bounds body = controller ? controller.bounds : new Bounds(target.position + Vector3.up * targetHeight, new Vector3(.6f, targetHeight * 2, .6f));
        // Bounds only reject distant candidates. Actual triangle contact is required
        // before fading, so empty air between branches never triggers it.
        for(int c=0;c<forestCanopies.Count;c++)
        {
            var canopy=forestCanopies[c];
            if(canopyBounds[c].SqrDistance(body.center)>900)continue;   // conservative: the visual's centre lies inside this box
            if(!canopy[0] || !canopy[0].gameObject.activeInHierarchy)continue;
            Renderer visual=canopy[0];
            foreach(var candidate in canopy)if(candidate && candidate.isVisible){visual=candidate;break;}
            if(!canopyGeometry.TryGetValue(visual,out var geometry))continue;
            var bounds=visual.bounds;
            if((bounds.center-body.center).sqrMagnitude>900)continue;
            bool covers=false;
            for(int row=0;row<3 && !covers;row++)
            {
                Vector3 point=body.center+Vector3.up*((row-1)*body.extents.y*.65f);
                Ray ray=cameraComponent.ScreenPointToRay(cameraComponent.WorldToScreenPoint(point));
                float distance=Vector3.Dot(point-ray.origin,ray.direction);
                covers=bounds.IntersectRay(ray,out float entry) && entry<distance-.1f
                    && geometry.Intersects(visual.transform,ray,distance-.1f);
            }
            if(covers)foreach(var renderer in canopy)AddOccluderRenderer(renderer,.09f);
        }
        // Sample the visible body, rather than a large sphere around the player.
        // ScreenPointToRay also keeps these rays parallel for an orthographic camera.
        for (int row = 0; row < 5; row++)
        for (int column = -1; column <= 1; column++)
        {
            Vector3 point = new Vector3(body.center.x, Mathf.Lerp(body.min.y, body.max.y, .12f + row * .19f), body.center.z);
            point += transform.right * (column * Mathf.Min(body.extents.x, body.extents.z) * .65f);
            Vector3 screen = cameraComponent.WorldToScreenPoint(point);
            if (screen.z <= 0) continue;
            Ray ray = cameraComponent.ScreenPointToRay(screen);
            float distance = Vector3.Dot(point - ray.origin, ray.direction) - .025f;
            if (distance <= 0) continue;
            int count = Physics.RaycastNonAlloc(ray, hits, distance, occluderLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) AddOccluder(hits[i].collider);
        }

        // Buildings use their own interior volume. Their walls never trigger the
        // generic camera rays, and their roof fades only after the player's center
        // has crossed the actual interior boundary.
        for (int i = BuildingInterior.Active.Count - 1; i >= 0; i--)
        {
            BuildingInterior building = BuildingInterior.Active[i];
            if (building != null && building.Contains(target.position))
            {
                AddOccluderRenderer(building.roofVisual, building.roofOpacity);
                foreach (var roof in building.additionalRoofVisuals)
                    if (roof) AddOccluderRenderer(roof, building.roofOpacity);
            }
        }

        ActiveOccluderCount = occludedThisFrame.Count;
    }

    private void AddOccluder(Collider hitCollider)
    {
        if (hitCollider == null || hitCollider is TerrainCollider)
            return;

        Transform hitTransform = hitCollider.transform;
        // Trunk capsules and old canopy proxies must not bypass precise mesh tests.
        for(var ancestor=hitTransform;ancestor;ancestor=ancestor.parent)
            if(treeRoots.Contains(ancestor))return;
        if (hitTransform == target || hitTransform.IsChildOf(target))
            return;

        // A BuildingInterior owns the visibility of its roof. Ignoring every
        // collider below it prevents walls and roof overhangs from fading while
        // the player is still outside or merely standing beside a wall.
        if (hitCollider.GetComponentInParent<BuildingInterior>() != null)
            return;

        var canopy = hitCollider.GetComponent<CanopyOccluder>();
        Renderer[] renderers = canopy && canopy.visual ? new Renderer[]{canopy.visual} : FindFadeRenderers(hitTransform);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            if (renderer == null || renderer.transform == target || renderer.transform.IsChildOf(target))
                continue;
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                continue;
            AddOccluderRenderer(renderer, null);
        }
    }

    private void AddOccluderRenderer(Renderer renderer, float? opacityOverride)
    {
        if (renderer == null || renderer.transform == target || renderer.transform.IsChildOf(target))
            return;
        if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
            return;

        occludedThisFrame.Add(renderer);
        if (!fadeStates.TryGetValue(renderer, out FadeState state))
        {
            state = CreateFadeState(renderer);
            fadeStates.Add(renderer, state);
        }
        if (opacityOverride.HasValue)
            state.MinimumOpacity = Mathf.Clamp(opacityOverride.Value, 0.01f, 0.95f);
    }

    private static Renderer[] FindFadeRenderers(Transform hitTransform)
    {
        Transform root = hitTransform;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

        for (int level = 0; renderers.Length == 0 && level < 3 && root.parent != null; level++)
        {
            root = root.parent;
            renderers = root.GetComponentsInChildren<Renderer>(true);
        }

        // Avoid accidentally fading a whole organizational scene group.
        return renderers.Length <= 24 ? renderers : Array.Empty<Renderer>();
    }

    private FadeState CreateFadeState(Renderer renderer)
    {
        Material[] originals = renderer.sharedMaterials;
        Material[] fades = new Material[originals.Length];
        Color[] colors = new Color[originals.Length];

        for (int i = 0; i < originals.Length; i++)
        {
            Material original = originals[i];
            if (original == null)
                continue;

            colors[i] = ReadColor(original);
            Material fade = new Material(original)
            {
                name = original.name + " (Camera Fade)",
                hideFlags = HideFlags.DontSave
            };
            ConfigureTransparentSurface(fade);
            fades[i] = fade;
        }

        float visualWidth = Mathf.Max(renderer.bounds.size.x, renderer.bounds.size.z);
        float widthRange = Mathf.Max(0.01f, adaptiveFadeFullWidth - adaptiveFadeStartWidth);
        float largeObjectFactor = Mathf.Clamp01((visualWidth - adaptiveFadeStartWidth) / widthRange);

        return new FadeState
        {
            Renderer = renderer,
            OriginalMaterials = originals,
            FadeMaterials = fades,
            OriginalColors = colors,
            MinimumOpacity = Mathf.Lerp(minimumOpacity, largeOccluderOpacity, largeObjectFactor)
        };
    }

    private void AnimateFades(float deltaTime)
    {
        cleanup.Clear();

        foreach (KeyValuePair<Renderer, FadeState> pair in fadeStates)
        {
            Renderer renderer = pair.Key;
            FadeState state = pair.Value;
            if (renderer == null)
            {
                DestroyFadeMaterials(state);
                cleanup.Add(renderer);
                continue;
            }

            bool shouldFade = occludedThisFrame.Contains(renderer);
            float targetOpacity = shouldFade ? state.MinimumOpacity : 1f;
            float speed = shouldFade ? fadeOutSpeed : Mathf.Min(fadeInSpeed,1.6f);
            float blend = 1f - Mathf.Exp(-speed * Mathf.Max(0f, deltaTime));
            state.Opacity = Mathf.Lerp(state.Opacity, targetOpacity, blend);

            if (shouldFade && !state.UsingFadeMaterials)
            {
                renderer.sharedMaterials = state.FadeMaterials;
                state.UsingFadeMaterials = true;
            }

            if (state.UsingFadeMaterials)
                ApplyOpacity(state);

            if (!shouldFade && state.Opacity >= 0.995f && state.UsingFadeMaterials)
            {
                renderer.sharedMaterials = state.OriginalMaterials;
                state.UsingFadeMaterials = false;
                state.Opacity = 1f;
            }
        }

        for (int i = 0; i < cleanup.Count; i++)
            fadeStates.Remove(cleanup[i]);
    }

    private static Color ReadColor(Material material)
    {
        if (material.HasProperty(BaseColorId))
            return material.GetColor(BaseColorId);
        if (material.HasProperty(ColorId))
            return material.GetColor(ColorId);
        return Color.white;
    }

    private static void ConfigureTransparentSurface(Material material)
    {
        if (material.HasProperty(SurfaceId))
            material.SetFloat(SurfaceId, 1f);
        if (material.HasProperty(BlendId))
            material.SetFloat(BlendId, 0f);
        if (material.HasProperty(SrcBlendId))
            material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
        if (material.HasProperty(DstBlendId))
            material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty(SrcBlendAlphaId))
            material.SetFloat(SrcBlendAlphaId, (float)BlendMode.One);
        if (material.HasProperty(DstBlendAlphaId))
            material.SetFloat(DstBlendAlphaId, (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty(ZWriteId))
            material.SetFloat(ZWriteId, 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        // Preserve any leaf cutout mask while fading the existing material.
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private static void ApplyOpacity(FadeState state)
    {
        for (int i = 0; i < state.FadeMaterials.Length; i++)
        {
            Material material = state.FadeMaterials[i];
            if (material == null)
                continue;

            Color color = state.OriginalColors[i];
            color.a *= state.Opacity;
            if (material.HasProperty(BaseColorId))
                material.SetColor(BaseColorId, color);
            if (material.HasProperty(ColorId))
                material.SetColor(ColorId, color);
        }
    }

    private void OnDisable()
    {
        RestoreAll();
    }

    private void OnDestroy()
    {
        RestoreAll();
        foreach (FadeState state in fadeStates.Values)
            DestroyFadeMaterials(state);
        fadeStates.Clear();
    }

    private void RestoreAll()
    {
        foreach (FadeState state in fadeStates.Values)
        {
            if (state.Renderer != null && state.UsingFadeMaterials)
                state.Renderer.sharedMaterials = state.OriginalMaterials;
            state.UsingFadeMaterials = false;
            state.Opacity = 1f;
        }
        ActiveOccluderCount = 0;
    }

    private static void DestroyFadeMaterials(FadeState state)
    {
        if (state == null || state.FadeMaterials == null)
            return;

        for (int i = 0; i < state.FadeMaterials.Length; i++)
        {
            Material material = state.FadeMaterials[i];
            if (material == null)
                continue;
            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (target == null)
            return;

        Vector3 targetPoint = target.position + Vector3.up * targetHeight;
        Gizmos.color = new Color(0.25f, 0.8f, 1f, 0.5f);
        Gizmos.DrawLine(transform.position, targetPoint);
        Gizmos.DrawWireSphere(targetPoint, castRadius);

    }
#endif
}
