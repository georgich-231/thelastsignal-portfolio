using UnityEngine;
public sealed class SnowHeatSource : MonoBehaviour
{
    [Min(.1f)] public float meltRadius=1.8f;
    void Start()
    {
        foreach(var snow in FindObjectsByType<LayeredSnow>(FindObjectsSortMode.None))
            if(snow.CoverageAt(transform.position)>0) snow.Paint(transform.position,meltRadius,0);
    }
}
