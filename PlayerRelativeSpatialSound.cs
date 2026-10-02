using UnityEngine;

// Place the acoustic source relative to the listener using player-space distance.
// An elevated orthographic camera must not add fifty metres of audio attenuation.
[DefaultExecutionOrder(500)]
public sealed class PlayerRelativeSpatialSound : MonoBehaviour
{
    public Transform player;
    public Vector3 worldPosition;
    public void SetLocation(Vector3 position){worldPosition=position;UpdatePosition();}
    void LateUpdate(){UpdatePosition();}
    public void UpdatePosition()
    {
        var camera=Camera.main;if(!camera||!player)return;
        Vector3 relative=worldPosition-player.position;
        Vector3 right=Vector3.ProjectOnPlane(camera.transform.right,Vector3.up).normalized;
        Vector3 forward=Vector3.Cross(right,Vector3.up);
        float lateral=Vector3.Dot(relative,right),depth=Vector3.Dot(relative,forward);
        transform.position=camera.transform.position+camera.transform.right*lateral+camera.transform.forward*depth+camera.transform.up*Mathf.Clamp(relative.y,-2,2);
    }
}
