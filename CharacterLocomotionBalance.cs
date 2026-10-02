using UnityEngine;

// The clips supply the gait; this small additive pass responds to the actual
// ground and changes of direction. Runs before animated footprint contacts.
[DefaultExecutionOrder(-10)]
[RequireComponent(typeof(CharacterController))]
public sealed class CharacterLocomotionBalance : MonoBehaviour
{
    [Range(0,1)] public float groundAdaptation=1;
    [Range(0,8)] public float maximumTurnLean=4;
    Transform visual,spine,chest,hips,head;
    readonly Transform[] thighs=new Transform[2],shins=new Transform[2],feet=new Transform[2];
    readonly RaycastHit[] hits=new RaycastHit[12];
    readonly Vector3[] targets=new Vector3[2],normals=new Vector3[2];
    readonly float[] groundOffsets=new float[2],weights=new float[2];
    readonly float[] footLifts=new float[2];
    public float FootLift(int index) => footLifts[index];
    public bool IsReady => ready;
    CharacterController controller;
    Vector3 previous,previousVelocity;
    float previousHeading,lean,foreLean,coldBlend;
    PlayerConditions conditions; Animator gait;
    bool ready;
    void OnEnable()
    {
        previous=transform.position;previousHeading=transform.eulerAngles.y;previousVelocity=Vector3.zero;
        lean=foreLean=0;groundOffsets[0]=groundOffsets[1]=0;
    }
    void Start()
    {
        controller=GetComponent<CharacterController>();
        conditions=GetComponent<PlayerConditions>();var animator=GetComponentInChildren<Animator>();gait=animator;if(!animator){enabled=false;return;}visual=animator.transform;
        foreach(var t in visual.GetComponentsInChildren<Transform>())
        {
            if(t.name=="Head")head=t;if(t.name=="Spine")spine=t;if(t.name=="Chest")chest=t;if(t.name=="Hips")hips=t;
            for(int i=0;i<2;i++)
            {
                string side=i==0?".L":".R";
                if(t.name=="Thigh"+side)thighs[i]=t;if(t.name=="Shin"+side)shins[i]=t;if(t.name=="Foot"+side)feet[i]=t;
            }
        }
        ready=spine && chest && hips && feet[0] && feet[1] && thighs[0] && thighs[1] && shins[0] && shins[1];
    }
    void LateUpdate()
    {
        if(!controller||!gait||!visual||!spine||!chest||!hips||!feet[0]||!feet[1]||!thighs[0]||!thighs[1]||!shins[0]||!shins[1])
        {ready=false;Start();}
        if(!ready || !controller || !gait || Time.deltaTime<=0)return;
        float dt=Time.deltaTime;
        Vector3 delta=transform.position-previous;previous=transform.position;delta.y=0;
        float turn=Mathf.DeltaAngle(previousHeading,transform.eulerAngles.y)/dt;previousHeading=transform.eulerAngles.y;
        if(delta.sqrMagnitude>4){previousVelocity=Vector3.zero;return;}
        Vector3 velocity=delta/dt;
        float acceleration=Vector3.Dot((velocity-previousVelocity)/dt,transform.forward);previousVelocity=velocity;
        float response=1-Mathf.Exp(-9*dt);
        lean=Mathf.Lerp(lean,Mathf.Clamp(-turn*.018f,-maximumTurnLean,maximumTurnLean)*Mathf.Clamp01(velocity.magnitude),response);
        foreLean=Mathf.Lerp(foreLean,Mathf.Clamp(acceleration*.48f,-2.5f,3.5f),response);
        spine.rotation=Quaternion.AngleAxis(lean,transform.forward)*Quaternion.AngleAxis(foreLean,transform.right)*spine.rotation;
        chest.rotation=Quaternion.AngleAxis(-lean*.3f,transform.forward)*chest.rotation;
        coldBlend=Mathf.MoveTowards(coldBlend,conditions?conditions.ColdSeverity:0,dt*.7f);
        if(coldBlend>.001f)
        {
            // Add fatigue to the animated pose, before ground-contact correction.
            float moving=Mathf.Clamp01(velocity.magnitude/.5f);
            float phase=gait.GetCurrentAnimatorStateInfo(0).normalizedTime*Mathf.PI*2;
            float limp=Mathf.Sin(phase)*moving;
            if(head)head.rotation=Quaternion.AngleAxis(5*coldBlend,transform.right)*head.rotation;
            spine.rotation=Quaternion.AngleAxis((8+Mathf.Sin(Time.time*1.6f)*.8f)*coldBlend,transform.right)*spine.rotation;
            chest.rotation=Quaternion.AngleAxis(4*coldBlend,transform.right)*Quaternion.AngleAxis(limp*2.8f*coldBlend,transform.forward)*chest.rotation;
            hips.position+=(transform.right*limp*.018f-Vector3.up*(.022f+Mathf.Max(0,limp)*.015f))*coldBlend*moving;
            // Rig objects can be rebuilt during editor domain reloads while this
            // component still has a frame of cached pose data.
            if(shins[1])shins[1].rotation=Quaternion.AngleAxis(4*coldBlend*moving,transform.right)*shins[1].rotation;
        }
        for(int i=0;i<2;i++)footLifts[i]=Mathf.Max(0,visual.InverseTransformPoint(feet[i].position).y-.16f);
        if(!controller.isGrounded || groundAdaptation<=0)return;
        float pelvisOffset=0;
        for(int i=0;i<2;i++)
        {
            var foot=feet[i];float lift=footLifts[i];
            weights[i]=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.025f,.17f,lift)))*groundAdaptation;
            targets[i]=foot.position;normals[i]=Vector3.up;
            int count=Physics.RaycastNonAlloc(foot.position+Vector3.up*.38f,Vector3.down,hits,.95f,~0,QueryTriggerInteraction.Ignore);
            float nearest=float.PositiveInfinity;float correction=0;
            for(int h=0;h<count;h++)
            {
                var hit=hits[h];
                if(hit.collider.transform.IsChildOf(transform) || hit.normal.y<.65f || hit.distance>=nearest)continue;
                // The terrain and walkable static surfaces can support a boot.
                if(hit.collider.attachedRigidbody && !hit.collider.attachedRigidbody.isKinematic)continue;
                nearest=hit.distance;correction=Mathf.Clamp(hit.point.y-visual.position.y,-.20f,.20f);normals[i]=hit.normal;
            }
            groundOffsets[i]=Mathf.Lerp(groundOffsets[i],correction,1-Mathf.Exp(-18*dt));
            targets[i]+=Vector3.up*groundOffsets[i]*weights[i];
            pelvisOffset=Mathf.Min(pelvisOffset,groundOffsets[i]*weights[i]);
        }
        hips.position+=Vector3.up*pelvisOffset*.65f;
        for(int i=0;i<2;i++)
        {
            Quaternion ankleRotation=feet[i].rotation;
            Solve(thighs[i],shins[i],feet[i],targets[i],shins[i].position+transform.forward*.5f);
            feet[i].rotation=Quaternion.Slerp(Quaternion.identity,Quaternion.FromToRotation(Vector3.up,normals[i]),weights[i]*.75f)*ankleRotation;
        }
    }
    static void Solve(Transform upper,Transform lower,Transform end,Vector3 target,Vector3 hint)
    {
        Vector3 origin=upper.position,offset=target-origin;
        float a=Vector3.Distance(origin,lower.position),b=Vector3.Distance(lower.position,end.position);
        float d=Mathf.Clamp(offset.magnitude,Mathf.Abs(a-b)+.001f,a+b-.001f);
        Vector3 axis=offset.normalized,bend=Vector3.ProjectOnPlane(hint-origin,axis).normalized;
        float along=(a*a-b*b+d*d)/(2*d);
        Vector3 joint=origin+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
        upper.rotation=Quaternion.FromToRotation(lower.position-origin,joint-origin)*upper.rotation;
        lower.rotation=Quaternion.FromToRotation(end.position-lower.position,origin+axis*d-lower.position)*lower.rotation;
    }
}
