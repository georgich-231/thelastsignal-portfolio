using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Additive two-arm posing preserves the existing breathing, walking and tired gait.
[DefaultExecutionOrder(220)]
public sealed class HeldRifle : MonoBehaviour
{
    public GameObject presentationPrefab;
    public bool IsAiming { get; private set; }
    public Vector3 AimDirection { get; private set; }
    public float AimBlend => aim;
    PlayerInventory pack; PlayerMovement movement; OpeningSequence story; InventoryUI inventory;
    Transform chest, rightArm, rightElbow, rightHand, leftArm, leftElbow, leftHand, prop;
    float pose, aim, guideTime;
    Canvas canvas; CanvasGroup guide, reticle;
    RectTransform reticleRect;
    bool shown;
    public bool EncounterAim {get;set;}
    float recoil;
    float aimPitch;
    public Vector3 MuzzlePosition => prop ? prop.TransformPoint(new Vector3(0,.035f,.53f)) : transform.position+Vector3.up*.8f;
    public void Recoil(){recoil=1;}
    Mesh gripMesh;Vector3[] relaxed,curled,gripVertices;float previousGrip;
    void Awake()
    {
        pack=GetComponent<PlayerInventory>();movement=GetComponent<PlayerMovement>();
        story=GetComponent<OpeningSequence>();inventory=GetComponent<InventoryUI>();
        foreach(var t in GetComponentsInChildren<Transform>())
        {
            switch(t.name){case "Chest":chest=t;break;case "UpperArm.R":rightArm=t;break;
            case "Forearm.R":rightElbow=t;break;case "Hand.R":rightHand=t;break;
            case "UpperArm.L":leftArm=t;break;case "Forearm.L":leftElbow=t;break;case "Hand.L":leftHand=t;break;}
        }
        if(presentationPrefab){prop=Instantiate(presentationPrefab,transform).transform;prop.name="Held hunting rifle";prop.gameObject.SetActive(false);}
        PrepareHands();
        var go=new GameObject("Rifle handling interface",typeof(Canvas),typeof(CanvasScaler));canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=660;
        var scale=go.GetComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1440,900);scale.matchWidthOrHeight=.5f;
        var surface=SurvivalUITheme.Surface(go.transform,"Rifle guide",0,0,342,166);var rect=surface.rectTransform;rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one;rect.anchoredPosition=new Vector2(-32,-180);
        guide=surface.gameObject.AddComponent<CanvasGroup>();guide.alpha=0;guide.blocksRaycasts=false;
        SurvivalGuideCard.Build(rect,"EQUIPMENT","Steady your hands","RMB","HOLD TO AIM","Select the rifle in your quickbar.\nHold the right mouse button to aim.");
        var target=new GameObject("Aim marker",typeof(RectTransform),typeof(CanvasGroup));target.transform.SetParent(go.transform,false);
        reticle=target.GetComponent<CanvasGroup>();reticle.blocksRaycasts=false;reticle.alpha=0;reticleRect=target.GetComponent<RectTransform>();reticleRect.anchorMin=reticleRect.anchorMax=Vector2.zero;
        for(int i=0;i<4;i++)
        {
            var tick=new GameObject("Sight tick",typeof(RectTransform),typeof(Image));tick.transform.SetParent(target.transform,false);var r=tick.GetComponent<RectTransform>();
            r.sizeDelta=i<2?new Vector2(6,1):new Vector2(1,6);r.anchoredPosition=i==0?new Vector2(-9,0):i==1?new Vector2(9,0):i==2?new Vector2(0,-9):new Vector2(0,9);
            var im=tick.GetComponent<Image>();im.color=SurvivalUITheme.Ice;im.raycastTarget=false;
        }
    }
    bool Gameplay => !PauseMenu.IsOpen && !inventory.IsOpen && (EncounterAim||(!story.BlocksGameplay && movement.enabled && !(GetComponent<GameplayInteraction>()?.IsBusy ?? false)));
    void Update()
    {
        bool equipped=pack.EquippedItem is RifleItem;
        IsAiming=equipped&&Gameplay&&Mouse.current!=null&&Mouse.current.rightButton.isPressed;
        AimDirection=transform.forward;
        if(IsAiming&&Camera.main)
        {
            var cam=Camera.main;bool thirdPerson=cam.GetComponent<DawnThirdPersonCamera>()?.IsActive??false;
            Vector2 screen=thirdPerson?new Vector2(Screen.width*.5f,Screen.height*.5f):Mouse.current.position.ReadValue();
            var ray=cam.ScreenPointToRay(screen);
            if(thirdPerson)
            {
                Vector3 point=ray.GetPoint(60);float closest=60;
                foreach(var hit in Physics.RaycastAll(ray,60,~0,QueryTriggerInteraction.Ignore))
                    if(!hit.transform.IsChildOf(transform)&&!hit.collider.GetComponent<CanopyOccluder>()&&hit.distance<closest){closest=hit.distance;point=hit.point;}
                var wolf=GetComponent<DawnWolfEncounter>();
                if(wolf&&wolf.Stage==DawnWolfEncounter.Phase.Focus)
                {
                    float along=Vector3.Dot(wolf.TargetPoint-ray.origin,ray.direction);
                    if(along>0&&Vector3.Distance(ray.GetPoint(along),wolf.TargetPoint)<.65f)point=wolf.TargetPoint;
                }
                var direction=point-MuzzlePosition;
                AimDirection=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
                aimPitch=Mathf.Clamp(-Mathf.Atan2(direction.y,Vector3.ProjectOnPlane(direction,Vector3.up).magnitude)*Mathf.Rad2Deg,-45,55);
            }
            else
            {
            var plane=new Plane(Vector3.up,transform.position+Vector3.up*.7f);
            if(plane.Raycast(ray,out float distance))
            {var d=Vector3.ProjectOnPlane(ray.GetPoint(distance)-transform.position,Vector3.up);if(d.sqrMagnitude>.2f)AimDirection=d.normalized;}
            aimPitch=0;
            }
            reticleRect.anchoredPosition=screen/canvas.scaleFactor;
        }
        if(equipped&&Gameplay&&!story.IsSpeaking&&!shown){guideTime+=Time.unscaledDeltaTime;if(guideTime>9)shown=true;}
        guide.alpha=Mathf.MoveTowards(guide.alpha,equipped&&Gameplay&&!EncounterAim&&!story.IsSpeaking&&!shown?1:0,Time.unscaledDeltaTime*3);
        reticle.alpha=Mathf.MoveTowards(reticle.alpha,IsAiming?1:0,Time.unscaledDeltaTime*6);
        if(EncounterAim&&IsAiming)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(AimDirection),Time.unscaledDeltaTime*360);
    }
    void LateUpdate()
    {
        if(!prop||!chest||!rightHand||!leftHand)return;
        float delta=PauseMenu.IsOpen?0:(EncounterAim?Time.unscaledDeltaTime:Time.deltaTime);
        pose=Mathf.MoveTowards(pose,pack.EquippedItem is RifleItem?1:0,delta*4);
        if(gripMesh && (pose>0||previousGrip>0))
        {
            for(int i=0;i<gripVertices.Length;i++)gripVertices[i]=Vector3.Lerp(relaxed[i],curled[i],pose);
            gripMesh.vertices=gripVertices;gripMesh.RecalculateNormals();previousGrip=pose;
        }
        aim=Mathf.MoveTowards(aim,IsAiming?1:0,delta*4.5f);
        recoil=Mathf.MoveTowards(recoil,0,delta*5);
        prop.gameObject.SetActive(pose>.01f);
        if(pose<=0)return;
        float ease=aim*aim*(3-2*aim);
        Vector3 carry=chest.position+transform.right*.10f+transform.forward*.27f-Vector3.up*.035f;
        Vector3 sight=chest.position+transform.right*.17f+transform.forward*.22f+Vector3.up*.24f;
        prop.position=Vector3.Lerp(carry,sight,ease);
        float breathe=Mathf.Sin(Time.time*1.6f)*.65f;
        prop.position-=transform.forward*(recoil*.055f);
        prop.rotation=transform.rotation*Quaternion.Euler(Mathf.Lerp(24,breathe+aimPitch,ease)-recoil*7,Mathf.Lerp(-17,0,ease),Mathf.Lerp(-9,-2,ease));
        Vector3 rightFingers=(prop.forward*.95f-prop.up*.30f).normalized;
        Vector3 leftFingers=prop.right;
        Vector3 rightGrip=prop.TransformPoint(new Vector3(.018f,-.038f,-.09f));
        Vector3 leftGrip=prop.TransformPoint(new Vector3(0,-.030f,.12f));
        Solve(rightArm,rightElbow,rightHand,rightGrip-rightFingers*.065f,transform.right-Vector3.up*.5f,pose);
        Solve(leftArm,leftElbow,leftHand,leftGrip-leftFingers*.065f,-transform.right*.7f-Vector3.up,pose);
        Quaternion rightRotation=Quaternion.FromToRotation(rightHand.up,rightFingers)*rightHand.rotation;
        Quaternion leftRotation=Quaternion.FromToRotation(leftHand.up,leftFingers)*leftHand.rotation;
        rightHand.rotation=Quaternion.Slerp(rightHand.rotation,rightRotation,pose);
        leftHand.rotation=Quaternion.Slerp(leftHand.rotation,leftRotation,pose);
    }
    static void Solve(Transform upper,Transform elbow,Transform hand,Vector3 target,Vector3 pole,float blend)
    {
        if(!upper||!elbow||!hand)return;
        float a=Vector3.Distance(upper.position,elbow.position),b=Vector3.Distance(elbow.position,hand.position);
        Vector3 delta=target-upper.position;float d=Mathf.Clamp(delta.magnitude,.02f,a+b-.002f);Vector3 dir=delta.normalized;
        float along=(a*a-b*b+d*d)/(2*d);float height=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
        var bend=upper.position+dir*along+Vector3.ProjectOnPlane(pole,dir).normalized*height;
        upper.rotation=Quaternion.Slerp(upper.rotation,Quaternion.FromToRotation(elbow.position-upper.position,bend-upper.position)*upper.rotation,blend);
        elbow.rotation=Quaternion.Slerp(elbow.rotation,Quaternion.FromToRotation(hand.position-elbow.position,target-elbow.position)*elbow.rotation,blend);
    }
    void PrepareHands()
    {
        foreach(var renderer in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            // HeldLighter already owns a runtime copy of this mesh. Share that copy
            // so switching equipment restores the original glove without leaking meshes.
            if(!renderer.sharedMesh || renderer.sharedMesh.name!="Temporary lighter grip")continue;
            gripMesh=renderer.sharedMesh;relaxed=gripMesh.vertices;curled=(Vector3[])relaxed.Clone();gripVertices=(Vector3[])relaxed.Clone();
            var weights=gripMesh.boneWeights;
            foreach(var hand in new[]{rightHand,leftHand})
            {
                int bone=System.Array.IndexOf(renderer.bones,hand);if(bone<0)continue;
                var bind=gripMesh.bindposes[bone];var inverse=bind.inverse;float max=0;
                for(int i=0;i<weights.Length;i++){var w=weights[i];if(w.boneIndex0==bone&&w.weight0>.7f)max=Mathf.Max(max,bind.MultiplyPoint3x4(relaxed[i]).y);}
                for(int i=0;i<weights.Length;i++)
                {
                    var w=weights[i];if(w.boneIndex0!=bone||w.weight0<.7f)continue;
                    var v=bind.MultiplyPoint3x4(relaxed[i]);float start=max*.48f;if(v.y<=start)continue;
                    float angle=(v.y-start)/(max-start)*2.0f,radius=(max-start)/2.0f;
                    v.y=start+Mathf.Sin(angle)*radius;v.z-=radius*(1-Mathf.Cos(angle));curled[i]=inverse.MultiplyPoint3x4(v);
                }
            }
            break;
        }
    }
    void OnDisable(){IsAiming=false;if(prop)prop.gameObject.SetActive(false);if(canvas)canvas.enabled=false;}
    void OnEnable(){if(canvas)canvas.enabled=true;}
    void OnDestroy(){if(prop)Destroy(prop.gameObject);if(canvas)Destroy(canvas.gameObject);}
}
