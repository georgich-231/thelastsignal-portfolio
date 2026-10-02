using UnityEngine;

[RequireComponent(typeof(Terrain))]
public sealed class LayeredSnow : MonoBehaviour
{
    public TerrainData untouchedSnow;
    public Texture2D coverage;
    [Range(.02f,.5f)] public float layerDepth = .20f;
    [HideInInspector] public bool painting;
    [HideInInspector] public float brushRadius = 1.2f;
    Texture2D savedCoverage;
    void Awake() { savedCoverage=coverage; if(coverage) coverage=Instantiate(coverage); }
    public float CoverageAt(Vector3 world)
    {
        if(!coverage) return 1;
        var d=GetComponent<Terrain>().terrainData; var p=world-transform.position;
        if(p.x<0||p.z<0||p.x>d.size.x||p.z>d.size.z)return 0;
        return coverage.GetPixelBilinear(p.x/d.size.x,p.z/d.size.z).r;
    }
    // Both the scene brush and heat sources use this persistent coverage mask.
    public void Paint(Vector3 world,float radius,float snowAmount)
    {
        if(!coverage||!untouchedSnow)return;
        var terrain=GetComponent<Terrain>();var data=terrain.terrainData;
        var center=world-transform.position; int n=coverage.width;
        int x0=Mathf.Clamp(Mathf.FloorToInt((center.x-radius)/data.size.x*(n-1)),0,n-1);
        int z0=Mathf.Clamp(Mathf.FloorToInt((center.z-radius)/data.size.z*(n-1)),0,n-1);
        int x1=Mathf.Clamp(Mathf.CeilToInt((center.x+radius)/data.size.x*(n-1)),0,n-1);
        int z1=Mathf.Clamp(Mathf.CeilToInt((center.z+radius)/data.size.z*(n-1)),0,n-1);
        for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
        {
            float wx=x*data.size.x/(n-1),wz=z*data.size.z/(n-1);
            float edge=Vector2.Distance(new Vector2(wx,wz),new Vector2(center.x,center.z))/radius;
            edge += (Mathf.PerlinNoise(wx*3.1f+17,wz*3.1f+23)-.5f)*.12f;
            float blend=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.65f,1,edge));
            float old=coverage.GetPixel(x,z).r;
            float value=snowAmount<.5f?Mathf.Min(old,1-blend):Mathf.Max(old,blend);
            coverage.SetPixel(x,z,new Color(value,value,value,1));
        }
        coverage.Apply(false);
        ApplyRegion(center,radius);
        if(Application.isPlaying)GetComponent<DeformableSnow>()?.Rebase();
        GetComponent<ForestFloorDetails>()?.Invalidate();
    }
    public void RefreshSurface()
    {
        var size=GetComponent<Terrain>().terrainData.size;
        ApplyRegion(size*.5f,Mathf.Max(size.x,size.z));
        GetComponent<ForestFloorDetails>()?.Invalidate();
    }
    void ApplyRegion(Vector3 center,float radius)
    {
        var data=GetComponent<Terrain>().terrainData;
        int n=data.heightmapResolution;
        int x0=Mathf.Clamp(Mathf.FloorToInt((center.x-radius)/data.size.x*(n-1)),0,n-1),z0=Mathf.Clamp(Mathf.FloorToInt((center.z-radius)/data.size.z*(n-1)),0,n-1);
        int x1=Mathf.Clamp(Mathf.CeilToInt((center.x+radius)/data.size.x*(n-1)),0,n-1),z1=Mathf.Clamp(Mathf.CeilToInt((center.z+radius)/data.size.z*(n-1)),0,n-1);
        var h=untouchedSnow.GetHeights(x0,z0,x1-x0+1,z1-z0+1);
        var forest=GetComponent<ForestFloorDetails>();
        for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
        {
            float exposed=1-coverage.GetPixelBilinear(x/(float)(n-1),z/(float)(n-1)).r;
            float relief=forest && exposed>.001f?forest.GroundOffset(transform.position.x+x*data.size.x/(n-1),transform.position.z+z*data.size.z/(n-1)):0;
            h[z-z0,x-x0]+=exposed*(relief-layerDepth)/data.size.y;
        }
        data.SetHeightsDelayLOD(x0,z0,h);data.SyncHeightmap();
        n=data.alphamapResolution;
        x0=Mathf.Clamp(Mathf.FloorToInt((center.x-radius)/data.size.x*n),0,n-1);z0=Mathf.Clamp(Mathf.FloorToInt((center.z-radius)/data.size.z*n),0,n-1);
        x1=Mathf.Clamp(Mathf.CeilToInt((center.x+radius)/data.size.x*n),0,n-1);z1=Mathf.Clamp(Mathf.CeilToInt((center.z+radius)/data.size.z*n),0,n-1);
        var a=untouchedSnow.GetAlphamaps(x0,z0,x1-x0+1,z1-z0+1);
        for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
        {
            float c=coverage.GetPixelBilinear((x+.5f)/n,(z+.5f)/n).r;
            // Keep the sloping snow bank opaque; expose soil only near its bottom.
            c=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.015f,.32f,c));
            for(int l=0;l<a.GetLength(2);l++)a[z-z0,x-x0,l]*=c;
            a[z-z0,x-x0,2]+=1-c;
        }
        data.SetAlphamaps(x0,z0,a);
    }
    void OnDestroy(){if(savedCoverage){Destroy(coverage);coverage=savedCoverage;}}
}
