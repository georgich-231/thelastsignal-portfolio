using UnityEngine;
[DefaultExecutionOrder(50)]
public sealed class CharacterLocomotionAnimator : MonoBehaviour
{
    public Animator animator;
    [Tooltip("Movement speed in metres per second that plays the walk at its authored pace.")]
    [Min(.1f)] public float referenceWalkSpeed = 1f;
    [Tooltip("Walk playback multiplier: lower is slower, higher is faster.")]
    [Range(.25f, 2f)] public float walkAnimationSpeed = 1f;
    [Tooltip("Idle playback multiplier, independent of walking.")]
    [Range(.25f, 2f)] public float idleAnimationSpeed = 1f;
    [Min(.1f)] public float referenceJogSpeed = 2.3f;
    [Range(.25f, 2f)] public float jogAnimationSpeed = 1f;
    [Min(.01f)] public float jogBlendTime = .18f;
    float jogBlend;
    PlayerMovement movement;
    [Min(.01f)] public float fullWalkBlendSpeed = .5f;
    [Min(.1f)] public float blendResponsiveness = 12f;
    Vector3 previous;
    float smoothed;
    void OnEnable()
    {
        previous=transform.position; smoothed=0;
        movement=GetComponent<PlayerMovement>(); jogBlend=0;
        if(!animator) animator=GetComponentInChildren<Animator>();
    }
    void Update()
    {
        var delta=transform.position-previous; previous=transform.position; delta.y=0;
        float speed=Time.deltaTime>0?delta.magnitude/Time.deltaTime:0;
        if(speed>15)speed=0;
        smoothed=Mathf.Lerp(smoothed,speed,1-Mathf.Exp(-blendResponsiveness*Time.deltaTime));
        if(!animator)return;
        float blend=Mathf.Clamp01(smoothed/Mathf.Max(.01f,fullWalkBlendSpeed));
        // Shift requests a faster gait, but a runner cannot reach that gait
        // before their body has accelerated. Keep the walk through take-off.
        float targetJog=Mathf.SmoothStep(0,1,Mathf.InverseLerp(referenceWalkSpeed*1.04f,referenceJogSpeed*.98f,smoothed));
        jogBlend=Mathf.MoveTowards(jogBlend,targetJog,Time.deltaTime/Mathf.Max(.01f,jogBlendTime));
        float pace=Mathf.Lerp(referenceWalkSpeed,referenceJogSpeed,jogBlend);
        float multiplier=Mathf.Lerp(walkAnimationSpeed,jogAnimationSpeed,jogBlend);
        // Playback follows actual displacement; smoothing cadence makes feet slide
        // during acceleration and braking. Blend weights remain smoothed.
        float cadence=Mathf.Clamp(speed/Mathf.Max(.1f,pace)*multiplier,.05f,2f);
        animator.SetFloat("Movement",blend*(1+jogBlend));
        animator.SetFloat("Cadence",Mathf.Lerp(idleAnimationSpeed,cadence,blend));
    }
}
