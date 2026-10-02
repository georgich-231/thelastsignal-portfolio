using UnityEngine;

public sealed class ForestVisibilityBudget : MonoBehaviour
{
    public float detailDistance=24f;
    public float cullDistance=38f;
    RealtimePine[] trees;
    float nextCheck;
    void Start(){trees=FindObjectsByType<RealtimePine>(FindObjectsSortMode.None);}
    void OnDisable()
    {
        if(trees==null)return;
        foreach(var tree in trees)
        {
            if(!tree)continue;tree.visibilityState=-1;
            foreach(var r in tree.detailed)if(r){r.enabled=true;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;}
            foreach(var r in tree.distant)if(r)r.enabled=false;
        }
    }
    void LateUpdate()
    {
        if(Time.unscaledTime<nextCheck||!Camera.main)return;
        nextCheck=Time.unscaledTime+.15f;
        Vector3 cameraPosition=Camera.main.transform.position;
        foreach(var tree in trees)
        {
            if(!tree)continue;
            float distance=tree.worldBounds.SqrDistance(cameraPosition);
            bool hiddenByFog=RenderSettings.fog && RenderSettings.fogMode==FogMode.ExponentialSquared && RenderSettings.fogDensity>=.085f;
            float threshold=detailDistance+(tree.visibilityState==0?2f:tree.visibilityState==1?-2f:0f);
            bool close=distance<threshold*threshold,visible=!hiddenByFog || distance<cullDistance*cullDistance;
            int state=visible?(close?0:1):2;
            if(tree.visibilityState==state)continue;
            tree.visibilityState=state;
            foreach(var r in tree.detailed)if(r)
            {
                r.enabled=visible&&close;
                r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // Low-detail geometry supplies shadows at both distances.
            foreach(var r in tree.distant)if(r)
            {
                r.enabled=visible;
                r.shadowCastingMode=close?UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly:UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }
    }
}
