using System;
using System.Collections.Generic;
using UnityEngine;

// Shared per-mesh acceleration data; no extra gameplay colliders or per-frame allocations.
internal sealed class CanopyMeshRaycast
{
    readonly Vector3[] vertices;
    readonly int[] triangles, order;
    readonly Vector3[] centers;
    readonly List<Node> nodes = new List<Node>();
    struct Node { public Bounds bounds; public int start, count, left, right; }

    public CanopyMeshRaycast(Mesh mesh)
    {
        vertices = mesh.vertices; triangles = mesh.triangles;
        order = new int[triangles.Length / 3]; centers = new Vector3[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
            centers[i] = (vertices[triangles[i*3]] + vertices[triangles[i*3+1]] + vertices[triangles[i*3+2]]) / 3;
        }
        if (order.Length > 0) Build(0, order.Length);
    }
    int Build(int start, int count)
    {
        var bounds = new Bounds(vertices[triangles[order[start]*3]], Vector3.zero);
        for (int i=start; i<start+count; i++)
            for (int j=0; j<3; j++) bounds.Encapsulate(vertices[triangles[order[i]*3+j]]);
        int index=nodes.Count; nodes.Add(default);
        var node=new Node { bounds=bounds, start=start, count=count, left=-1, right=-1 };
        if (count>12)
        {
            Vector3 size=bounds.size; int axis=size.x>size.y ? (size.x>size.z?0:2) : (size.y>size.z?1:2);
            Array.Sort(order,start,count,Comparer<int>.Create((a,b)=>centers[a][axis].CompareTo(centers[b][axis])));
            node.left=Build(start,count/2); node.right=Build(start+count/2,count-count/2); node.count=0;
        }
        nodes[index]=node; return index;
    }
    public bool Intersects(Transform transform, Ray worldRay, float worldDistance)
    {
        Vector3 direction=transform.InverseTransformVector(worldRay.direction);
        float scale=direction.magnitude;
        if (scale<1e-9f || nodes.Count==0) return false;
        return Trace(0,new Ray(transform.InverseTransformPoint(worldRay.origin),direction/scale),worldDistance*scale);
    }
    bool Trace(int index, Ray ray, float maximum)
    {
        Node node=nodes[index];
        if (!node.bounds.IntersectRay(ray,out float entry) || entry>maximum) return false;
        if(node.count==0)return Trace(node.left,ray,maximum)||Trace(node.right,ray,maximum);
        for(int i=node.start;i<node.start+node.count;i++)
        {
            int t=order[i]*3; Vector3 a=vertices[triangles[t]];
            Vector3 e1=vertices[triangles[t+1]]-a, e2=vertices[triangles[t+2]]-a;
            Vector3 p=Vector3.Cross(ray.direction,e2); float det=Vector3.Dot(e1,p);
            // Imported trees use very small local units, so use a relative tolerance.
            if(Mathf.Abs(det)<1e-7f*e1.magnitude*e2.magnitude)continue;
            float inv=1/det; Vector3 s=ray.origin-a; float u=Vector3.Dot(s,p)*inv;
            if(u<0 || u>1)continue;
            Vector3 q=Vector3.Cross(s,e1); float v=Vector3.Dot(ray.direction,q)*inv;
            if(v<0 || u+v>1)continue;
            float distance=Vector3.Dot(e2,q)*inv;
            if(distance>0 && distance<maximum)return true;
        }
        return false;
    }
}
