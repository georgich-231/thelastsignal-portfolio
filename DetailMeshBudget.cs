using UnityEngine;

// Keep expensive scanned detail close to the player, with a separate cheap shadow mesh.
[DisallowMultipleComponent]
public sealed class DetailMeshBudget : MonoBehaviour
{
    public Mesh detailedMesh, distantMesh;
    public float detailDistance=5f;
    MeshFilter filter;Renderer visual;float nextCheck;bool usingDetail=true;
    void Awake(){filter=GetComponent<MeshFilter>();visual=GetComponent<Renderer>();}
    void LateUpdate()
    {
        if(Time.unscaledTime<nextCheck||!Camera.main)return;
        nextCheck=Time.unscaledTime+.2f;
        bool close=visual.bounds.SqrDistance(Camera.main.transform.position)<detailDistance*detailDistance;
        if(close==usingDetail)return;
        filter.sharedMesh=close?detailedMesh:distantMesh;usingDetail=close;
    }
}
