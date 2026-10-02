using UnityEngine;

// A height-field snow patch. Runtime edits use a private copy of TerrainData.
[RequireComponent(typeof(Terrain), typeof(TerrainCollider))]
public class DeformableSnow : MonoBehaviour
{
    [Tooltip("Depth above the solid ground. Keep the Terrain Collider disabled for soft snow.")]
    [SerializeField, Range(0.1f, 0.5f)] private float snowDepth = 0.25f;
    [SerializeField, Min(0f)] private float packedSnowThickness = 0.025f;
    [SerializeField, Range(0.02f, 0.25f)] private float dentDepth = 0.115f;
    [SerializeField, Range(0f, 0.08f)] private float rimHeight = 0.022f;
    [SerializeField, Range(0f, 1f)] private float compressedTint = 0.7f;
    [Tooltip("Earth showing through only at the bottom of a bootprint.")]
    [SerializeField, Range(0f, 1f)] private float dirtVisibility = 0.60f;
    [Tooltip("Scales footprint darkening without brightening the surrounding terrain. Lower values retain more fresh snow.")]
    [SerializeField, Range(0f, 1f)] private float footprintContrast = 0.18f;
    [Tooltip("Scales the existing dent depth to avoid deep, black self-shadowed holes.")]
    [SerializeField, Range(0.1f, 1f)] private float footprintDepthScale = 0.5f;
    [Tooltip("Small per-step changes while preserving the boot silhouette.")]
    [SerializeField, Range(0f, 0.15f)] private float shapeVariation = 0.07f;
    [SerializeField, Min(0.02f)] private float syncInterval = 0.12f;
    [Header("Footprint refilling")]
    [SerializeField] private bool refillFootprints = true;
    [Tooltip("Seconds before each footprint starts filling. New steps restart the timer where they overlap.")]
    [SerializeField, Min(0f)] private float refillDelay = 12f;
    [Tooltip("Seconds for a footprint to gently return to untouched snow after the delay.")]
    [SerializeField, Min(0.1f)] private float refillDuration = 18f;
    [Tooltip("Limits terrain patches restored per update to prevent frame spikes.")]
    [SerializeField, Range(1, 12)] private int refillUpdatesPerTick = 4;
    private sealed class Impression
    {
        public int id, x, z, ax, az;
        public float created;
        public float[,] shape;
        public float[,,] tint, cleanTint;
    }
    private readonly System.Collections.Generic.List<Impression> impressions = new();
    private int[,] heightOwner, tintOwner;
    private int nextImpression;
    private int refillCursor;
    private float nextRefill;
    private Terrain terrain;
    private TerrainCollider terrainCollider;
    private TerrainData original, runtime, resting;
    private LayeredSnow layers;
    private float[,] fresh, heights;
    private bool dirty;
    private float nextSync;
    public int StampCount { get; private set; }

    private void Awake()
    {
        terrain = GetComponent<Terrain>();
        layers = GetComponent<LayeredSnow>();
        terrainCollider = GetComponent<TerrainCollider>();
        original = terrain.terrainData;
        runtime = Instantiate(original);
        runtime.name = original.name + " (Runtime Snow)";
        terrain.terrainData = runtime;
        terrainCollider.terrainData = runtime;
        int resolution = runtime.heightmapResolution;
        fresh = original.GetHeights(0, 0, resolution, resolution);
        heights = (float[,])fresh.Clone();
        heightOwner = new int[resolution, resolution];
        tintOwner = new int[runtime.alphamapResolution, runtime.alphamapResolution];
        resting = Instantiate(runtime);
    }

    public void Rebase()
    {
        if(!runtime)return;
        int n=runtime.heightmapResolution;
        fresh=runtime.GetHeights(0,0,n,n);heights=(float[,])fresh.Clone();
        impressions.Clear();heightOwner=new int[n,n];
        tintOwner=new int[runtime.alphamapResolution,runtime.alphamapResolution];
        if(resting)Destroy(resting);resting=Instantiate(runtime);
    }

    public bool IsUnderFoot(Vector3 foot)
    {
        if (runtime == null || fresh == null) return false;   // Awake not finished on this tile
        if (layers && layers.CoverageAt(foot) < .2f) return false;
        Vector3 local = foot - transform.position;
        if (local.x < 0 || local.z < 0 || local.x > runtime.size.x || local.z > runtime.size.z) return false;
        float y = terrain.SampleHeight(foot) + transform.position.y;
        // Reject footprints while standing on crates or airborne above the snow.
        return foot.y <= y + 0.10f && foot.y >= y - snowDepth - 0.10f;
    }

    public bool Stamp(Vector3 foot, Vector3 forward, float width, float length)
    {
        return Press(foot, forward, width, length);
    }

    private bool Press(Vector3 foot, Vector3 forward, float width, float length)
    {
        if (!IsUnderFoot(foot)) return false;
        forward.y = 0;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 center = foot - transform.position;
        float seed = Mathf.Repeat(Mathf.Sin(center.x * 12.9898f + center.z * 78.233f + StampCount * 37.719f) * 43758.5453f, 1f);
        float second = Mathf.Repeat(seed * 17.371f + 0.318f, 1f);
        float halfWidth = Mathf.Max(0.05f, width * 0.5f * Mathf.Lerp(1f - shapeVariation, 1f + shapeVariation, seed));
        float halfLength = Mathf.Max(0.05f, length * 0.5f * Mathf.Lerp(1f - shapeVariation * 0.6f, 1f + shapeVariation * 0.6f, second));
        float shear = (Mathf.Repeat(second * 13.17f, 1f) * 2f - 1f) * shapeVariation * 0.32f;
        float corner = 0.70f + (Mathf.Repeat(seed * 23.91f, 1f) * 2f - 1f) * shapeVariation * 0.30f;
        float radius = Mathf.Max(halfWidth, halfLength) * 1.55f;
        int n = runtime.heightmapResolution;
        float dx = runtime.size.x / (n - 1), dz = runtime.size.z / (n - 1);
        int x0 = Mathf.Clamp(Mathf.FloorToInt((center.x - radius) / dx), 0, n - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((center.z - radius) / dz), 0, n - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((center.x + radius) / dx), 0, n - 1);
        int z1 = Mathf.Clamp(Mathf.CeilToInt((center.z + radius) / dz), 0, n - 1);
        float[,] patch = new float[z1 - z0 + 1, x1 - x0 + 1];
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            Vector3 delta = new Vector3(x * dx - center.x, 0, z * dz - center.z);
            float r = BootRadius(delta, right, forward, halfWidth, halfLength, shear, corner);
            float rest = fresh[z, x];
            float coverage = layers ? layers.CoverageAt(transform.position + new Vector3(x * dx,0,z * dz)) : 1;
            if(coverage < .2f){patch[z-z0,x-x0]=heights[z,x];continue;}
            if (r < 1f)
            {
                float bowl = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, r));
                // Press toward a fixed depth, so repeated steps never drill holes.
                // Ground follows the drifts; retain the same packed layer at every elevation.
                float packedFloor = rest - (snowDepth - packedSnowThickness) / runtime.size.y;
                float pressed = Mathf.Max(packedFloor, rest - dentDepth * footprintDepthScale * coverage * bowl / runtime.size.y);
                heights[z, x] = Mathf.Min(heights[z, x], pressed);
            }
            else if (r < 1.5f && heights[z, x] >= rest - 0.001f)
            {
                float rim = Mathf.Sin((r - 1f) * Mathf.PI / 0.5f);
                heights[z, x] = Mathf.Max(heights[z, x], rest + rimHeight * coverage * rim / runtime.size.y);
            }
            patch[z - z0, x - x0] = Mathf.Clamp01(heights[z, x]);
        }
        runtime.SetHeightsDelayLOD(x0, z0, patch);
        // Tint only the compressed sole, leaving fresh snow between steps.
        PaintCompression(center, right, forward, halfWidth, halfLength, radius, shear, corner);
        RememberImpression(x0, z0, patch, center, radius);
        dirty = true;
        StampCount++;
        return true;
    }

    private static float BootRadius(Vector3 delta, Vector3 right, Vector3 forward, float halfWidth, float halfLength, float shear, float corner)
    {
        float along = Vector3.Dot(delta, forward) / halfLength;
        // Broad rounded toe, inset arch and a distinct, squarer heel.
        float arch=1-.26f*Mathf.Exp(-Mathf.Pow((along+.22f)/.28f,2));
        float taper=Mathf.Lerp(.72f,1,Mathf.InverseLerp(-.7f,.25f,along))*arch;
        float across=Vector3.Dot(delta,right)/(halfWidth*taper)+along*shear;
        float ax=Mathf.Abs(across);
        if(along>.55f)return Mathf.Sqrt(ax*ax+Mathf.Pow((along-.55f)/.45f,2));
        return Mathf.Max(ax,Mathf.Max(-along,(ax+Mathf.Max(0,-along-.65f))*.88f));
    }

    private void PaintCompression(Vector3 center, Vector3 right, Vector3 forward, float w, float l, float radius, float shear, float corner)
    {
        if (runtime.alphamapLayers < 2) return;
        int n = runtime.alphamapResolution;
        float dx = runtime.size.x / n, dz = runtime.size.z / n;
        int x0 = Mathf.Clamp(Mathf.FloorToInt((center.x - radius) / dx), 0, n - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((center.z - radius) / dz), 0, n - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((center.x + radius) / dx), 0, n - 1);
        int z1 = Mathf.Clamp(Mathf.CeilToInt((center.z + radius) / dz), 0, n - 1);
        var paint = runtime.GetAlphamaps(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            Vector3 delta = new Vector3((x + 0.5f) * dx - center.x, 0, (z + 0.5f) * dz - center.z);
            float r = BootRadius(delta, right, forward, w, l, shear, corner);
            if(layers && layers.CoverageAt(transform.position+new Vector3((x+.5f)*dx,0,(z+.5f)*dz))<.2f)continue;
            float amount = compressedTint * footprintContrast * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1.02f, r)));
            amount = Mathf.Max(paint[z - z0, x - x0, 1], amount);
            float dirt = 0f;
            if (runtime.alphamapLayers >= 3)
            {
                float bottom = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.85f, r));
                float flecks = Mathf.Lerp(0.65f, 1f, Mathf.PerlinNoise((x + 0.5f) * dx * 16f, (z + 0.5f) * dz * 16f));
                dirt = Mathf.Max(paint[z - z0, x - x0, 2], dirtVisibility * footprintContrast * bottom * flecks);
                paint[z - z0, x - x0, 2] = dirt;
            }
            amount = Mathf.Min(amount, 1f - dirt);
            paint[z - z0, x - x0, 0] = 1f - amount - dirt;
            paint[z - z0, x - x0, 1] = amount;
        }
        runtime.SetAlphamaps(x0, z0, paint);
    }

    private void RememberImpression(int x, int z, float[,] patch, Vector3 center, float radius)
    {
        int n = runtime.alphamapResolution;
        float dx = runtime.size.x / n, dz = runtime.size.z / n;
        int ax = Mathf.Clamp(Mathf.FloorToInt((center.x - radius) / dx), 0, n - 1);
        int az = Mathf.Clamp(Mathf.FloorToInt((center.z - radius) / dz), 0, n - 1);
        int bx = Mathf.Clamp(Mathf.CeilToInt((center.x + radius) / dx), 0, n - 1);
        int bz = Mathf.Clamp(Mathf.CeilToInt((center.z + radius) / dz), 0, n - 1);
        var mark = new Impression { id = ++nextImpression, x = x, z = z, ax = ax, az = az,
            created = Time.time, shape = patch,
            tint = runtime.GetAlphamaps(ax, az, bx - ax + 1, bz - az + 1),
            cleanTint = resting.GetAlphamaps(ax, az, bx - ax + 1, bz - az + 1) };
        for (int j = 0; j < patch.GetLength(0); j++)
        for (int i = 0; i < patch.GetLength(1); i++) heightOwner[z + j, x + i] = mark.id;
        for (int j = az; j <= bz; j++)
        for (int i = ax; i <= bx; i++) tintOwner[j, i] = mark.id;
        impressions.Add(mark);
    }

    // Only touched patches are updated. Ownership prevents old steps erasing newer overlaps.
    private void Refill(float now)
    {
        if (!refillFootprints || runtime == null || impressions.Count == 0) return;
        int visited = 0, restored = 0, scanLimit = impressions.Count;
        while (visited++ < scanLimit && restored < refillUpdatesPerTick && impressions.Count > 0)
        {
            if (refillCursor >= impressions.Count) refillCursor = 0;
            int k = refillCursor;
            var mark = impressions[k];
            float age = now - mark.created - refillDelay;
            if (age <= 0) { refillCursor++; continue; }
            restored++;
            float t = Mathf.Clamp01(age / Mathf.Max(.1f, refillDuration));
            float blend = t * t * (3f - 2f * t);
            int h = mark.shape.GetLength(0), w = mark.shape.GetLength(1);
            var patch = new float[h, w];
            bool owned = false;
            for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                int z = mark.z + j, x = mark.x + i;
                if (heightOwner[z, x] == mark.id)
                {
                    owned = true;
                    heights[z, x] = Mathf.Lerp(mark.shape[j, i], fresh[z, x], blend);
                    if (t >= 1) heightOwner[z, x] = 0;
                }
                patch[j, i] = heights[z, x];
            }
            int ah = mark.tint.GetLength(0), aw = mark.tint.GetLength(1);
            var paint = runtime.GetAlphamaps(mark.ax, mark.az, aw, ah);
            bool painted = false;
            for (int j = 0; j < ah; j++)
            for (int i = 0; i < aw; i++)
            {
                if (tintOwner[mark.az + j, mark.ax + i] != mark.id) continue;
                painted = true;
                for (int layer = 0; layer < paint.GetLength(2); layer++)
                    paint[j, i, layer] = Mathf.Lerp(mark.tint[j, i, layer], mark.cleanTint[j, i, layer], blend);
                if (t >= 1) tintOwner[mark.az + j, mark.ax + i] = 0;
            }
            // Update only this boot-sized patch, never the large rectangle joining
            // distant footprints. Retain ownership checks for overlapping soles.
            if (owned) { runtime.SetHeightsDelayLOD(mark.x, mark.z, patch); dirty = true; }
            if (painted) runtime.SetAlphamaps(mark.ax, mark.az, paint);
            if (t >= 1 || (!owned && !painted)) impressions.RemoveAt(k);
            else refillCursor++;
        }
    }
    private void LateUpdate()
    {
        if (Time.time >= nextRefill)
        {
            Refill(Time.time);
            nextRefill = Time.time + Mathf.Max(.06f, syncInterval);
        }
        if (dirty && Time.unscaledTime >= nextSync)
        {
            Flush();
            nextSync = Time.unscaledTime + syncInterval;
        }
    }

    public void Flush()
    {
        if (!dirty || runtime == null) return;
        runtime.SyncHeightmap();
        dirty = false;
    }

    private void OnDestroy()
    {
        if (terrain != null) terrain.terrainData = original;
        if (terrainCollider != null) terrainCollider.terrainData = original;
        if (runtime != null) Destroy(runtime);
        if (resting != null) Destroy(resting);
    }
}


