using UnityEngine;
using System.Collections.Generic;

public static class EasternForestRoute
{
    // Leaves the forest house (moved 140m north to z=200) and climbs east out of the
    // northern valley. Same shape as the original route, which started at (17,51).
    static readonly Vector3[] controls={new Vector3(17,0,191),new Vector3(25,0,193),new Vector3(34,0,197),new Vector3(42,0,203),new Vector3(50,0,209),new Vector3(57,0,215),new Vector3(64,0,221)};
    public static Vector3 Start=>controls[0];
    static List<Vector3> samples;
    public static List<Vector3> Samples
    {
        get
        {
            if(samples!=null)return samples;samples=new List<Vector3>();
            for(int segment=0;segment<controls.Length-1;segment++)
                for(int k=0;k<24;k++)
                {float t=k/24f;Vector3 a=controls[Mathf.Max(0,segment-1)],b=controls[segment],c=controls[segment+1],d=controls[Mathf.Min(controls.Length-1,segment+2)];samples.Add(.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t));}
            samples.Add(controls[controls.Length-1]);return samples;
        }
    }
    public static Vector3 Nearest(Vector3 position,out float distance,out float progress)
    {
        Vector3 result=Samples[0];float best=float.MaxValue;float along=0;
        var query=new Vector3(position.x,0,position.z);
        for(int i=0;i<Samples.Count-1;i++)
        {
            var a=Samples[i];var delta=Samples[i+1]-a;float t=Mathf.Clamp01(Vector3.Dot(query-a,delta)/delta.sqrMagnitude);
            var p=a+delta*t;float d=(p-query).sqrMagnitude;
            if(d<best){best=d;result=p;along=i+t;}
        }
        distance=Mathf.Sqrt(best);progress=along/(Samples.Count-1);return result;
    }
    public static List<Vector3> Footsteps()
    {
        var result=new List<Vector3>();float until=0;
        for(int i=1;i<Samples.Count;i++)
        {
            var a=Samples[i-1];var delta=Samples[i]-a;float length=delta.magnitude;
            while(until<=length){var p=a+delta.normalized*until;var side=Vector3.Cross(Vector3.up,delta.normalized);p+=side*(result.Count%2==0?-.12f:.12f);result.Add(p);until+=.57f;}
            until-=length;
        }
        return result;
    }
}
