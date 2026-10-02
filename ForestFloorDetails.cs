using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways, RequireComponent(typeof(LayeredSnow))]
public sealed class ForestFloorDetails : MonoBehaviour
{
    public Material wood, litter, water;
    [Range(0,.08f)] public float unevenness=.045f;
    [Range(0,1)] public float puddleDensity=.45f;
    [Range(.2f,2)] public float litterSpacing=.55f;
    public int seed=137;
    GameObject generated;
    readonly List<Mesh> meshes=new();
    bool dirty=true;
    double nextBuild;
    static float Hash(int x,int z,int salt){unchecked{uint h=(uint)(x*374761393+z*668265263+salt*144269);h=(h^(h>>13))*1274126177;return (h^(h>>16))/(float)uint.MaxValue;}}
    public void Invalidate(){dirty=true;}
    void OnEnable(){dirty=true;}
    System.Collections.IEnumerator Start(){yield return null;yield return null;dirty=true;nextBuild=0;}
    void OnValidate(){dirty=true;}
    void Update(){if(dirty && Time.realtimeSinceStartupAsDouble>=nextBuild){Rebuild();nextBuild=Time.realtimeSinceStartupAsDouble+.5;}}
    public Vector3 Basin(int x,int z)
    {
        return new Vector3((x+.2f+Hash(x,z,seed)*.6f)*2.2f,Mathf.Lerp(.32f,.55f,Hash(x,z,seed+1)),(z+.2f+Hash(x,z,seed+2)*.6f)*2.2f);
    }
    public float GroundOffset(float x,float z)
    {
        float h=(Mathf.PerlinNoise(x*2.3f+17,z*2.3f+31)-.5f)*unevenness*2;
        h+=(Mathf.PerlinNoise(x*7.7f,z*7.7f)-.5f)*unevenness*.4f;
        int ix=Mathf.FloorToInt(x/2.2f),iz=Mathf.FloorToInt(z/2.2f);
        for(int j=-1;j<=1;j++)for(int i=-1;i<=1;i++)
        {
            if(Hash(ix+i,iz+j,seed+3)>puddleDensity)continue;
            var b=Basin(ix+i,iz+j);float d=Vector2.Distance(new Vector2(x,z),new Vector2(b.x,b.z))/b.y;
            h-=.09f*(1-Mathf.SmoothStep(0,1,d));
        }
        return h;
    }
    public void Rebuild()
    {
        dirty=false;Clear();if(!wood||!water||!litter)return;
        var snow=GetComponent<LayeredSnow>();var terrain=GetComponent<Terrain>();var size=terrain.terrainData.size;
        Vector3 origin=transform.position;
        var fire=GameObject.Find("Winter Hearth Bonfire");
        Vector2 firePosition=fire?new Vector2(fire.transform.position.x,fire.transform.position.z):Vector2.positiveInfinity;
        // Sample a single snapshot rather than doing GetComponent/native texture reads
        // for every twig candidate. Out-of-tile positions are not exposed soil.
        var mask=snow.coverage;var pixels=mask?mask.GetPixels32():null;
        int mw=mask?mask.width:0,mh=mask?mask.height:0;
        float SnowAt(float x,float z)
        {
            if(pixels==null)return 1;
            float fx=Mathf.Clamp01((x-origin.x)/size.x)*(mw-1),fz=Mathf.Clamp01((z-origin.z)/size.z)*(mh-1);
            int ix=Mathf.Min(mw-2,(int)fx),iz=Mathf.Min(mh-2,(int)fz);
            return Mathf.Lerp(Mathf.Lerp(pixels[iz*mw+ix].r,pixels[iz*mw+ix+1].r,fx-ix),Mathf.Lerp(pixels[(iz+1)*mw+ix].r,pixels[(iz+1)*mw+ix+1].r,fx-ix),fz-iz)/255f;
        }
        generated=new GameObject("Procedural forest floor (automatic)");generated.hideFlags=HideFlags.HideAndDontSave;
        var twigs=new List<Vector3>();var twigTriangles=new List<int>();var needles=new List<Vector3>();var needleTriangles=new List<int>();var puddles=new List<Vector3>();var puddleTriangles=new List<int>();
        float Y(float x,float z)=>terrain.SampleHeight(new Vector3(x,0,z))+transform.position.y;
        bool ClearSnow(float x,float z,float radius)
        {
            if(x-radius<origin.x||z-radius<origin.z||x+radius>origin.x+size.x||z+radius>origin.z+size.z)return false;
            if(SnowAt(x,z)>.10f)return false;
            for(int i=0;i<8;i++){float a=i*Mathf.PI/4;if(SnowAt(x+Mathf.Cos(a)*radius,z+Mathf.Sin(a)*radius)>.16f)return false;}
            if(Vector2.Distance(new Vector2(x,z),firePosition)<1.05f)return false;
            return true;
        }
        // World-space stratified sampling: tiles no longer repeat the same local
        // pattern, and each candidate has independent X/Z jitter and occupancy.
        for(int z=Mathf.FloorToInt(origin.z/litterSpacing);z<Mathf.CeilToInt((origin.z+size.z)/litterSpacing);z++)
        for(int x=Mathf.FloorToInt(origin.x/litterSpacing);x<Mathf.CeilToInt((origin.x+size.x)/litterSpacing);x++)
        {
            if(Hash(x,z,seed+40)>.82f)continue;
            float wx=(x+Hash(x,z,seed+4))*litterSpacing,wz=(z+Hash(x,z,seed+5))*litterSpacing;
            if(!ClearSnow(wx,wz,.29f))continue;
            float angle=Hash(x,z,seed+6)*Mathf.PI*2;var dir=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            float length=Mathf.Lerp(.14f,.42f,Hash(x,z,seed+7));var a=new Vector3(wx,0,wz)-dir*length*.5f;var b=a+dir*length;a.y=Y(a.x,a.z)+.018f;b.y=Y(b.x,b.z)+.02f;
            Branch(twigs,twigTriangles,a,b,.008f);
            var tip=Vector3.Lerp(a,b,.7f)+new Vector3(-dir.z,0,dir.x)*length*.3f;tip.y=Y(tip.x,tip.z)+.018f;Branch(twigs,twigTriangles,Vector3.Lerp(a,b,.4f),tip,.004f);
            for(int k=0;k<5;k++)
            {
                var c=new Vector3(wx+(Hash(x+k,z,seed+8)-.5f)*.4f,0,wz+(Hash(x,z+k,seed+9)-.5f)*.4f);c.y=Y(c.x,c.z)+.006f;
                float an=Hash(x+k,z+k,seed+10)*6.28f;var d=new Vector3(Mathf.Cos(an),0,Mathf.Sin(an))*.045f;var w=Vector3.Cross(d,Vector3.up).normalized*.0025f;
                int v=needles.Count;needles.Add(c-d);needles.Add(c+w);needles.Add(c+d);needles.Add(c-w);needleTriangles.AddRange(new[]{v,v+1,v+2,v,v+2,v+3});
            }
        }
        for(int z=Mathf.FloorToInt(transform.position.z/2.2f);z<(transform.position.z+size.z)/2.2f;z++)for(int x=Mathf.FloorToInt(transform.position.x/2.2f);x<(transform.position.x+size.x)/2.2f;x++)
        {
            if(Hash(x,z,seed+3)>puddleDensity)continue;var b=Basin(x,z);float radius=b.y*.64f;if(!ClearSnow(b.x,b.z,radius+.08f))continue;
            // A level water surface belongs in a shallow hollow, not on a hill face.
            if(Mathf.Abs(Y(b.x+radius,b.z)-Y(b.x-radius,b.z))>.14f||Mathf.Abs(Y(b.x,b.z+radius)-Y(b.x,b.z-radius))>.14f)continue;
            float y=Y(b.x,b.z)+.027f;int first=puddles.Count;puddles.Add(new Vector3(b.x,y,b.z));
            for(int i=0;i<20;i++){float angle=i*Mathf.PI*.1f;float r=radius*Mathf.Lerp(.83f,1,Hash(x+i,z,seed+20));puddles.Add(new Vector3(b.x+Mathf.Cos(angle)*r,y,b.z+Mathf.Sin(angle)*r));}
            for(int i=0;i<20;i++)puddleTriangles.AddRange(new[]{first,first+(i+1)%20+1,first+i+1});
        }
        AddMesh("Fallen twigs",twigs,twigTriangles,wood);AddMesh("Pine needle litter",needles,needleTriangles,litter);AddMesh("Thaw puddles",puddles,puddleTriangles,water);
    }
    static void Branch(List<Vector3> v,List<int> t,Vector3 a,Vector3 b,float radius)
    {
        var forward=(b-a).normalized;var right=Vector3.Cross(forward,Vector3.up).normalized;var up=Vector3.Cross(right,forward);int start=v.Count;
        for(int end=0;end<2;end++)for(int i=0;i<5;i++){float angle=i*Mathf.PI*2/5;v.Add((end==0?a:b)+(right*Mathf.Cos(angle)+up*Mathf.Sin(angle))*radius*(end==0?1:.55f));}
        for(int i=0;i<5;i++){int n=(i+1)%5;t.AddRange(new[]{start+i,start+n,start+5+i,start+n,start+5+n,start+5+i});}
    }
    void AddMesh(string name,List<Vector3> vertices,List<int> indices,Material material)
    {
        if(vertices.Count==0)return;var mesh=new Mesh{name=name,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32,hideFlags=HideFlags.HideAndDontSave};
        mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);var uv=new List<Vector2>();foreach(var v in vertices)uv.Add(new Vector2(v.x,v.z));mesh.SetUVs(0,uv);
        var colors=new Color[vertices.Count];for(int i=0;i<colors.Length;i++)colors[i]=new Color(1,1,1,name=="Thaw puddles"?(i%21==0?1:0):1);mesh.colors=colors;
        mesh.RecalculateNormals();mesh.RecalculateBounds();meshes.Add(mesh);
        var go=new GameObject(name);go.hideFlags=HideFlags.HideAndDontSave;go.transform.SetParent(generated.transform);go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
    }
    void Clear(){if(generated){if(Application.isPlaying)Destroy(generated);else DestroyImmediate(generated);}foreach(var m in meshes){if(Application.isPlaying)Destroy(m);else DestroyImmediate(m);}meshes.Clear();}
    void OnDisable(){Clear();}
}
