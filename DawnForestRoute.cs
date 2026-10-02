using UnityEngine;
using System.Collections.Generic;
public static class DawnForestRoute
{
    public static float Center(float z)=>244+7*Mathf.Sin((z-208)*.045f)+3*Mathf.Sin((z-208)*.10f);
    public static List<Vector3> Footsteps()
    {
        var points=new List<Vector3>();
        for(float z=209;z<318;z+=.59f)points.Add(new Vector3(Center(z)+(points.Count%2==0?-.12f:.12f),0,z));
        return points;
    }
}
