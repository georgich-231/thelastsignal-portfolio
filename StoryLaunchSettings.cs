using UnityEngine;
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class StoryLaunchSettings : MonoBehaviour
{
    [Tooltip("Start outside the shed at 02:00, with the lighter collected, bonfire lit and note/focus sequence complete. Turn off for the normal prologue.")]
    public bool startAtMidnight;
    [Tooltip("Start at the house entrance at 02:00 with all earlier story beats completed. Takes priority over midnight.")]
    public bool startAtHouse;
    public Transform houseSpawn;
    [Tooltip("The original mountain trail entrance used for a fresh prologue.")]
    public Transform prologueSpawn;
    public Material prologueSky;
    void Awake()
    {
        // Establish a clean daytime baseline before the story/camera cache atmosphere.
        RenderSettings.fog=false;
        if(prologueSky)RenderSettings.skybox=prologueSky;
        if(!prologueSpawn)return;
        var controller=GetComponent<CharacterController>();
        bool wasEnabled=controller&&controller.enabled;
        if(controller)controller.enabled=false;
        transform.SetPositionAndRotation(prologueSpawn.position,prologueSpawn.rotation);
        if(controller)controller.enabled=wasEnabled;
    }
}
