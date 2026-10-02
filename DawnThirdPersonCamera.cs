using UnityEngine;
using UnityEngine.InputSystem;

// Shoulder camera everywhere; indoor detection changes atmosphere, not controls.
[DefaultExecutionOrder(-40)]
[RequireComponent(typeof(Camera))]
public sealed class DawnThirdPersonCamera : MonoBehaviour
{
    [Range(.02f,.5f)] public float sensitivity=.12f;
    public float followDistance=3.25f;
    public float aimDistance=2.25f;
    public float shoulderOffset=.42f;
    public float minimumPitch=-27,maximumPitch=60;
    [Header("Outdoor visibility")]
    [Tooltip("Exponential-squared fog: scenery is almost completely obscured by 30 metres at 0.09.")]
    [Range(.02f,.1f)] public float minimumOutdoorFogDensity=.09f;
    public float outdoorFarClip=65f;
    public float daylightFarClip=1000f;
    // Matches the night sky's horizon: fogged ridges must fade INTO the dark sky,
    // not to a lighter grey that outlines every mountain against it.
    public Color nightFogColor=new Color(.028f,.034f,.05f);
    [Header("Interior readability")]
    [Range(0,2)] public float indoorFillIntensity=.72f;
    public float indoorFillRange=9f;
    public float indoorFollowDistance=2.1f;
    public bool IsActive=>enabled&&target;
    public Vector2 LookAngles=>new Vector2(yaw,pitch);
    public void SetLookAngles(float heading,float elevation)
    {yaw=Mathf.Repeat(heading,360);pitch=Mathf.Clamp(elevation,minimumPitch,maximumPitch);}
    Transform target;Camera view;OpeningSequence story;InventoryUI inventory;HeldRifle rifle;
    Vector3 pivot,pivotVelocity;float yaw,pitch=12,boom,boomVelocity,aimBlend;
    bool previousOrthographic,previousCursor,outdoors,initialized;CursorLockMode previousLock;
    float previousNear,previousFar,previousFov;
    CameraClearFlags previousClearFlags;
    TopDownCamera overhead;
    Material nightSky, previousSky;
    ShedNightSequence nightSequence;
    Light indoorFill;
    bool originalFog,savedOutdoorFog;FogMode originalFogMode,savedOutdoorFogMode;
    float originalFogDensity,savedOutdoorFogDensity;Color originalFogColor,savedOutdoorFogColor;
    float baseDistance=-1,baseVelocity,fillWeight;
    readonly System.Collections.Generic.Dictionary<Renderer,UnityEngine.Rendering.ShadowCastingMode> hiddenBody=new();

    public void Enter(Transform player)
    {
        target=player;view=GetComponent<Camera>();overhead=GetComponent<TopDownCamera>();
        nightSequence=player.GetComponent<ShedNightSequence>();
        nightSky=Resources.Load<Material>("WinterNightSky");
        if(!initialized)
        {
            initialized=true;
            previousOrthographic=view.orthographic;previousNear=view.nearClipPlane;previousFar=view.farClipPlane;previousFov=view.fieldOfView;
            previousClearFlags=view.clearFlags;
            previousSky=RenderSettings.skybox;
            previousCursor=Cursor.visible;previousLock=Cursor.lockState;
            originalFog=RenderSettings.fog;originalFogMode=RenderSettings.fogMode;originalFogDensity=RenderSettings.fogDensity;originalFogColor=RenderSettings.fogColor;
            SaveOutdoorFog();
        }
        BuildIndoorFill();
        story=player.GetComponent<OpeningSequence>();inventory=player.GetComponent<InventoryUI>();rifle=player.GetComponent<HeldRifle>();
        yaw=player.eulerAngles.y;pitch=12;boom=followDistance;pivot=player.position+Vector3.up*.62f;pivotVelocity=Vector3.zero;
        enabled=true;
        SetOutdoor(BuildingInterior.FindContaining(target)==null,true);
    }
    bool MenuOpen=>PauseMenu.IsOpen||(inventory&&inventory.IsOpen)||(story&&(story.IsReading||story.InPrologue));
    void Update()
    {
        if(!target)return;
        bool shouldBeOutdoors=BuildingInterior.FindContaining(target)==null;
        if(shouldBeOutdoors!=outdoors)SetOutdoor(shouldBeOutdoors,false);   // eased, not snapped
        ApplyOutdoorVisibility();
        if(MenuOpen){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;return;}
        // Locking centers the reticle; menus retain a normal, usable cursor.
        bool justLocked=Cursor.lockState!=CursorLockMode.Locked;
        Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;
        if(!justLocked&&Mouse.current!=null)Orbit(Mouse.current.delta.ReadValue());
        transform.rotation=Quaternion.Euler(pitch,yaw,0);
    }
    public void Orbit(Vector2 mouseDelta)
    {
        yaw=Mathf.Repeat(yaw+mouseDelta.x*sensitivity,360);
        pitch=Mathf.Clamp(pitch-mouseDelta.y*sensitivity,minimumPitch,maximumPitch);
    }
    void LateUpdate()
    {
        if(!target)return;
        ApplyOutdoorVisibility();
        if(indoorFill)
        {
            fillWeight=Mathf.MoveTowards(fillWeight,outdoors?0:1,Time.unscaledDeltaTime*2.2f);
            indoorFill.intensity=indoorFillIntensity*Mathf.SmoothStep(0,1,fillWeight);
            indoorFill.enabled=fillWeight>.005f;
        }
        if(!outdoors)
        {
            if(indoorFill)indoorFill.transform.position=target.position+Vector3.up*2.4f;
        }
        if(!MenuOpen)PlaceCamera(false);
    }
    void SetOutdoor(bool value,bool snap)
    {
        outdoors=value;
        if(overhead)overhead.enabled=false;
        view.orthographic=false;view.fieldOfView=62;view.nearClipPlane=.06f;
        view.farClipPlane=outdoorFarClip;view.clearFlags=CameraClearFlags.Skybox;
        if(outdoors)
        {
            ApplyOutdoorVisibility();
            // (fill light fades out in LateUpdate)
        }
        else
        {
            ApplyOutdoorVisibility();
            // (fill light fades in in LateUpdate)
        }
        PlaceCamera(snap);
    }
    void BuildIndoorFill()
    {
        if(indoorFill)return;
        var go=new GameObject("Soft interior camera fill");
        indoorFill=go.AddComponent<Light>();indoorFill.type=LightType.Point;indoorFill.shadows=LightShadows.None;
        indoorFill.color=new Color(.78f,.84f,1f);indoorFill.intensity=indoorFillIntensity;indoorFill.range=indoorFillRange;
        indoorFill.renderMode=LightRenderMode.ForcePixel;indoorFill.enabled=false;
    }
    void SaveOutdoorFog()
    {
        savedOutdoorFog=RenderSettings.fog;savedOutdoorFogMode=RenderSettings.fogMode;
        savedOutdoorFogDensity=RenderSettings.fogDensity;savedOutdoorFogColor=RenderSettings.fogColor;
    }
    void RestoreOutdoorFog()
    {
        RenderSettings.fog=savedOutdoorFog;RenderSettings.fogMode=savedOutdoorFogMode;
        RenderSettings.fogDensity=savedOutdoorFogDensity;RenderSettings.fogColor=savedOutdoorFogColor;
    }
    void ApplyOutdoorVisibility()
    {
        bool night=nightSequence && nightSequence.IsNight && !nightSequence.IsDawn;
        view.farClipPlane=night?outdoorFarClip:daylightFarClip;
        if(!night)
        {
            RenderSettings.fog=false;
            if(RenderSettings.skybox==nightSky && previousSky)RenderSettings.skybox=previousSky;
            return;
        }
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;
        RenderSettings.fogDensity=Mathf.Max(RenderSettings.fogDensity,minimumOutdoorFogDensity);
        if (!nightSequence || !nightSequence.IsDawn)
        {
            RenderSettings.fogColor=nightFogColor;
            if(nightSky)RenderSettings.skybox=nightSky;
        }
    }
    void PlaceCamera(bool snap)
    {
        float dt=Time.unscaledDeltaTime;
        aimBlend=Mathf.MoveTowards(aimBlend,rifle&&rifle.IsAiming?1:0,dt*5);
        var desiredPivot=target.position+Vector3.up*.62f;
        pivot=snap?desiredPivot:Vector3.SmoothDamp(pivot,desiredPivot,ref pivotVelocity,.055f,Mathf.Infinity,dt);
        Quaternion rotation=Quaternion.Euler(pitch,yaw,0);
        // Crossing a doorway eases between the outdoor and indoor distances over about
        // half a second instead of jumping; walls still pull the camera in at once.
        float baseTarget=outdoors?followDistance:indoorFollowDistance;
        if(snap||baseDistance<0){baseDistance=baseTarget;baseVelocity=0;}
        else baseDistance=Mathf.SmoothDamp(baseDistance,baseTarget,ref baseVelocity,.35f,Mathf.Infinity,dt);
        float distance=Mathf.Lerp(baseDistance,aimDistance,aimBlend);
        Vector3 displacement=rotation*(Vector3.back*distance+Vector3.right*Mathf.Lerp(shoulderOffset,.56f,aimBlend));
        float length=displacement.magnitude,allowed=length;
        // Sweep the entire camera volume. Pull in immediately, ease back out.
        foreach(var hit in Physics.SphereCastAll(pivot,.20f,displacement/length,length,~0,QueryTriggerInteraction.Ignore))
        {
            if(hit.transform.IsChildOf(target)||hit.collider.GetComponent<CanopyOccluder>())continue;
            allowed=Mathf.Min(allowed,Mathf.Max(.10f,hit.distance-.08f));
        }
        if(snap||allowed<boom){boom=allowed;boomVelocity=0;}
        else boom=Mathf.SmoothDamp(boom,allowed,ref boomVelocity,.22f,Mathf.Infinity,dt);
        transform.SetPositionAndRotation(pivot+displacement.normalized*boom,rotation);
        view.fieldOfView=Mathf.Lerp(62,53,aimBlend);
        UpdateBodyVisibility();
    }
    // When a wall pushes the camera into the character, the character stops being
    // drawn (its shadow stays), the way modern third-person games handle it -
    // instead of the view filling with the inside of a coat.
    void UpdateBodyVisibility()
    {
        if(!target)return;
        Vector3 a=target.position-Vector3.up*.75f,b=target.position+Vector3.up*.75f,p=transform.position;
        Vector3 ab=b-a;float t=Mathf.Clamp01(Vector3.Dot(p-a,ab)/ab.sqrMagnitude);
        float distance=Vector3.Distance(p,a+ab*t);
        bool hidden=hiddenBody.Count>0;
        bool hide=hidden?distance<.72f:distance<.56f;   // hysteresis: no flicker at the edge
        if(hide==hidden)return;
        if(hide)
        {
            foreach(var r in target.GetComponentsInChildren<Renderer>())
            {
                if(r is ParticleSystemRenderer||!r.enabled)continue;
                hiddenBody[r]=r.shadowCastingMode;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }
        else ShowBody();
    }
    void ShowBody()
    {
        foreach(var kv in hiddenBody)if(kv.Key)kv.Key.shadowCastingMode=kv.Value;
        hiddenBody.Clear();
    }
    void OnDisable()
    {
        ShowBody();
        if(!view||!initialized)return;
        view.orthographic=previousOrthographic;view.nearClipPlane=previousNear;view.farClipPlane=previousFar;view.fieldOfView=previousFov;
        view.clearFlags=previousClearFlags;
        if(RenderSettings.skybox==nightSky)RenderSettings.skybox=previousSky;
        Cursor.lockState=previousLock;Cursor.visible=previousCursor;
        RenderSettings.fog=originalFog;RenderSettings.fogMode=originalFogMode;RenderSettings.fogDensity=originalFogDensity;RenderSettings.fogColor=originalFogColor;
        if(indoorFill)Destroy(indoorFill.gameObject);
        if(overhead){overhead.enabled=true;overhead.SnapToPlayer();}
    }
}
