using UnityEngine;
public sealed class CanopyOccluder : MonoBehaviour
{
    public Renderer visual;
    void Awake(){for(int i=0;i<32;i++)Physics.IgnoreLayerCollision(gameObject.layer,i,true);}
}
