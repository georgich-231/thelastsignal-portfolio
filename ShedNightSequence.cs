using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// A one-shot continuation of the footprint discovery. No progress is written into the editor scene.
[DefaultExecutionOrder(-55), DisallowMultipleComponent]
public sealed class ShedNightSequence : MonoBehaviour
{
    public Transform bed;
    public AudioClip nightMusic, wolfHowl;
    public float trailTriggerZ=23f;
    [Tooltip("Additional leaf sway at night: 0.2 is a subtle 20 percent increase.")]
    [SerializeField, Range(0f, 0.5f)] float nightLeafSwayBoost=0.2f;
    static readonly int NightLeafSwayId=Shader.PropertyToID("_WinterNightLeafSwayBoost");
    float leafSwayBlend;
    public bool IsBusy { get; private set; }
    public bool HasReturned { get; private set; }
    public bool HasSlept { get; private set; }
    public bool IsNight { get; private set; }
    public bool IsDawn { get; private set; }
    Material previousSky;
    public void EnterDawn()
    {
        IsDawn=true;conditions.SetHour(5.25f);
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.42f,.46f,.50f);
        RenderSettings.ambientEquatorColor=new Color(.25f,.29f,.34f);
        RenderSettings.ambientGroundColor=new Color(.12f,.14f,.18f);
        RenderSettings.ambientIntensity=.8f;RenderSettings.reflectionIntensity=.25f;
        if(sun)sun.intensity=0;
        previousSky=RenderSettings.skybox;var sky=Resources.Load<Material>("CursedDawnSky");if(sky)RenderSettings.skybox=sky;
    }
    public bool CanUseBed => HasReturned && !HasSlept && !IsBusy && InsideShed() && NearBed();
    OpeningSequence story; InventoryUI inventory; PlayerMovement movement;
    PlayerConditions conditions; SurvivalAudio audioMix;
    Collider bedCollider; Canvas canvas; CanvasGroup blackout, card;
    SurvivalPromptView prompt; AudioSource music, howl;
    BoxCollider returnBarrier,returnTrigger;
    bool returnArmed=true;
    UnityEngine.Rendering.AmbientMode originalAmbientMode;
    float originalReflection;
    public bool IsTurningBack {get;private set;}
    Light sun; Color originalSun, originalAmbient; float originalIntensity, originalAmbientIntensity;
    float lightingBlend, lightingTarget; bool priorMovement, howlPaused;
    float Delta => PauseMenu.IsOpen?0:Time.unscaledDeltaTime;
    void Awake()
    {
        story=GetComponent<OpeningSequence>();inventory=GetComponent<InventoryUI>();
        movement=GetComponent<PlayerMovement>();conditions=GetComponent<PlayerConditions>();audioMix=GetComponent<SurvivalAudio>();
        if(!bed)bed=GameObject.Find("RangerFieldBed")?.transform;
        if(bed)bedCollider=bed.GetComponentInChildren<Collider>();
        if(!nightMusic)nightMusic=Resources.Load<AudioClip>("NightSequence/MidnightUndertone");
        if(!wolfHowl)wolfHowl=Resources.Load<AudioClip>("NightSequence/SingleWolfHowl");
        sun=RenderSettings.sun;
        if(!sun)foreach(var light in FindObjectsByType<Light>(FindObjectsSortMode.None))if(light.type==LightType.Directional){sun=light;break;}
        if(sun){originalSun=sun.color;originalIntensity=sun.intensity;}
        originalAmbientMode=RenderSettings.ambientMode;originalReflection=RenderSettings.reflectionIntensity;
        originalAmbient=RenderSettings.ambientLight;originalAmbientIntensity=RenderSettings.ambientIntensity;
        music=MakeSource("Midnight music",true);music.clip=nightMusic;music.loop=true;music.volume=0;
        howl=MakeSource("Distant wolf",false);howl.clip=wolfHowl;howl.volume=.24f;howl.panStereo=-.22f;
        howl.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency=3400;
        var gate=new GameObject("Return to shed - temporary story boundary");
        gate.transform.position=new Vector3(9,6,20);returnBarrier=gate.AddComponent<BoxCollider>();returnBarrier.size=new Vector3(52,22,.6f);
        var warning=new GameObject("Return warning trigger");warning.transform.position=new Vector3(9,6,18.3f);
        returnTrigger=warning.AddComponent<BoxCollider>();returnTrigger.size=new Vector3(52,22,1.6f);returnTrigger.isTrigger=true;
        BuildUI();
    }
    AudioSource MakeSource(string label,bool isMusic)
    {
        var go=new GameObject(label);go.transform.SetParent(transform,false);
        var source=go.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;
        GameAudioSettings.Route(source,isMusic);return source;
    }
    bool InsideShed()=>story && story.outpost && story.outpost.Contains(transform.position);
    bool NearBed()
    {
        if(!bedCollider)return false;
        Vector3 at=transform.position;at.y=bedCollider.bounds.center.y;
        return (bedCollider.ClosestPoint(at)-at).sqrMagnitude<1.15f*1.15f;
    }
    void Update()
    {
        if(!story)return;
        leafSwayBlend=Mathf.MoveTowards(leafSwayBlend,IsNight && !IsDawn?1f:0f,Delta*.25f);
        Shader.SetGlobalFloat(NightLeafSwayId,nightLeafSwayBoost*leafSwayBlend);
        blackout.gameObject.SetActive(!PauseMenu.IsOpen);
        if(PauseMenu.IsOpen && howl.isPlaying){howl.Pause();howlPaused=true;}
        else if(!PauseMenu.IsOpen && howlPaused){howl.UnPause();howlPaused=false;}
        bool gameplay=!story.BlocksGameplay && !inventory.IsOpen && !GetComponent<GameplayInteraction>().IsBusy;
        if(!HasReturned && gameplay && (GetComponent<ForestFocus>()?.Discovered ?? false))BeginReturnToShed();
        // The northern gate only exists after the footprints have been found.
        // Before that point the player must be free to complete the fire, note,
        // and focus tutorial in their intended order.
        if(returnBarrier)returnBarrier.enabled=HasReturned && !IsNight;
        if(returnTrigger)returnTrigger.enabled=HasReturned && !IsNight;
        if(transform.position.z<17.4f && !IsTurningBack)returnArmed=true;
        if(HasReturned && !IsNight && returnArmed && gameplay && returnTrigger && returnTrigger.bounds.Contains(transform.position))TryTurnBack();
        if(HasReturned && !HasSlept && !IsBusy && InsideShed() && story.Step==OpeningSequence.ChapterStep.ReturnToShed)
            story.SetNightObjective(OpeningSequence.ChapterStep.SleepUntilMorning);
        prompt.Group.alpha=gameplay && CanUseBed && !story.IsSpeaking?1:0;
        if(bed)prompt.SetWorldAnchor(bed.position+Vector3.up*.9f);
#if ENABLE_INPUT_SYSTEM
        if(prompt.Group.alpha>0 && Keyboard.current!=null && Keyboard.current.eKey.wasPressedThisFrame)TrySleep();
#endif
        if(!IsDawn){
        lightingBlend=Mathf.MoveTowards(lightingBlend,lightingTarget,Delta*(IsBusy?.5f:.018f));
        // The visible sky moon does not illuminate terrain; fire and carried lights provide visibility.
        if(sun){sun.color=Color.Lerp(originalSun,new Color(.68f,.75f,.90f),lightingBlend);sun.intensity=Mathf.Lerp(originalIntensity,0f,lightingBlend);}
        RenderSettings.ambientLight=Color.Lerp(originalAmbient,Color.black,lightingBlend);
        RenderSettings.ambientIntensity=Mathf.Lerp(originalAmbientIntensity,0f,lightingBlend);
        if(lightingBlend>.25f)RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.reflectionIntensity=Mathf.Lerp(originalReflection,0f,lightingBlend);
        }
        music.volume=Mathf.MoveTowards(music.volume,IsDawn?.045f:IsNight?.095f:0,Delta*.035f);
    }
    public void BeginReturnToShed()
    {
        if(HasReturned)return;
        HasReturned=true;
        story.SetNightObjective(OpeningSequence.ChapterStep.ReturnToShed);
        story.Say("YOU","I can't feel my hands. You always said a second missing person helps no one. Back to the shed. First light.");
    }
    public void StartPreparedNight()
    {
        StopAllCoroutines();HasReturned=HasSlept=IsNight=true;IsBusy=IsTurningBack=false;
        lightingBlend=lightingTarget=1;conditions.SetHour(2);conditions.warmth=65;
        blackout.alpha=card.alpha=0;prompt.Group.alpha=0;
        returnBarrier.enabled=returnTrigger.enabled=false;
        audioMix.MusicSuppression=1;if(nightMusic)music.Play();
        story.SetNightObjective(OpeningSequence.ChapterStep.MidnightAwake);
        GetComponent<HeldLighter>()?.Introduce();movement.enabled=true;
    }
    public bool TryTurnBack()
    {
        if(!HasReturned || IsNight || IsBusy || !returnArmed || PauseMenu.IsOpen || inventory.IsOpen || story.InPrologue || story.IsReading)return false;
        returnArmed=false;StartCoroutine(TurnBack());return true;
    }
    IEnumerator TurnBack()
    {
        IsBusy=true;IsTurningBack=true;priorMovement=movement.enabled;movement.enabled=false;
        if(!HasReturned)BeginReturnToShed();
        else if(!story.IsSpeaking)story.Say("YOU","I won't reach you like this. Warm up first. Then follow the tracks.");
        Vector3 direction=story.outpost.transform.position-transform.position;direction.y=0;direction.Normalize();
        Quaternion from=transform.rotation,to=Quaternion.LookRotation(direction);
        for(float t=0;t<.8f;t+=Delta){transform.rotation=Quaternion.Slerp(from,to,Mathf.SmoothStep(0,1,t/.8f));yield return null;}
        var controller=GetComponent<CharacterController>();
        for(float t=0;t<1.8f;t+=Delta)
        {
            if(Delta>0)controller.Move((direction*1.05f+Vector3.down*2)*Delta);
            yield return null;
        }
        IsTurningBack=false;IsBusy=false;movement.enabled=priorMovement;
    }
    public bool TrySleep()
    {
        if(!CanUseBed || inventory.IsOpen || story.BlocksGameplay || story.IsSpeaking || GetComponent<GameplayInteraction>().IsBusy)return false;
        StartCoroutine(Sleep());return true;
    }
    IEnumerator Sleep()
    {
        IsBusy=true;priorMovement=movement.enabled;movement.enabled=false;prompt.Group.alpha=0;
        yield return Fade(0,1,1.35f);
        HasSlept=true;IsNight=true;lightingTarget=1;conditions.SetHour(2);
        // Shelter and the bed restore some warmth; hunger and thirst remain placeholders.
        conditions.warmth=Mathf.Max(conditions.warmth,55);
        audioMix.MusicSuppression=1;if(nightMusic)music.Play();
        for(float t=0;t<3.5f;t+=Delta){card.alpha=Mathf.Min(Mathf.Clamp01(t/.7f),Mathf.Clamp01((3.5f-t)/.7f));yield return null;}
        card.alpha=0;
        if(wolfHowl)howl.Play();
        yield return Fade(1,0,2.3f);
        IsBusy=false;movement.enabled=priorMovement;
        story.SetNightObjective(OpeningSequence.ChapterStep.MidnightAwake);
        story.Say("YOU","Still dark. I can't lie here any longer.");
        GetComponent<HeldLighter>()?.Introduce();
    }
    IEnumerator Fade(float from,float to,float seconds)
    {
        for(float t=0;t<seconds;t+=Delta){blackout.alpha=Mathf.Lerp(from,to,Mathf.SmoothStep(0,1,t/seconds));yield return null;}
        blackout.alpha=to;
    }
    void BuildUI()
    {
        var root=new GameObject("Night sequence",typeof(Canvas),typeof(CanvasScaler));canvas=root.GetComponent<Canvas>();
        canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=2600;
        var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1440,900);scaler.matchWidthOrHeight=.5f;
        prompt=SurvivalPromptView.Create(root.transform,"Bed interaction");
        prompt.Category.text="FORESTRY OUTPOST / BED";prompt.Title.text="REST UNTIL MORNING";prompt.Detail.text="Get out of the cold. Try to sleep.";
        var go=new GameObject("Sleep fade",typeof(RectTransform),typeof(Image),typeof(CanvasGroup));go.transform.SetParent(root.transform,false);
        var rect=go.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
        go.GetComponent<Image>().color=Color.black;go.GetComponent<Image>().raycastTarget=false;
        blackout=go.GetComponent<CanvasGroup>();blackout.alpha=0;blackout.blocksRaycasts=false;
        var text=SurvivalUITheme.Text(go.transform,"L A T E R   T H A T   N I G H T",0,0,700,40,17,false,SurvivalUITheme.Secondary,TextAnchor.MiddleCenter);
        var tr=text.rectTransform;tr.anchorMin=tr.anchorMax=tr.pivot=new Vector2(.5f,.5f);tr.anchoredPosition=new Vector2(0,24);
        card=text.gameObject.AddComponent<CanvasGroup>();card.alpha=0;
        var time=SurvivalUITheme.Text(text.transform,"02:00",0,40,700,48,32,true,SurvivalUITheme.Ivory,TextAnchor.MiddleCenter);
    }
    void OnDisable()
    {
        leafSwayBlend=0;
        Shader.SetGlobalFloat(NightLeafSwayId,0f);
    }
    void OnDestroy()
    {
        if(IsDawn)RenderSettings.skybox=previousSky;
        if(IsBusy && movement)movement.enabled=priorMovement;
        if(audioMix)audioMix.MusicSuppression=0;
        if(sun){sun.color=originalSun;sun.intensity=originalIntensity;}
        RenderSettings.ambientLight=originalAmbient;RenderSettings.ambientIntensity=originalAmbientIntensity;
        if(returnBarrier)Destroy(returnBarrier.gameObject);if(returnTrigger)Destroy(returnTrigger.gameObject);
        RenderSettings.ambientMode=originalAmbientMode;RenderSettings.reflectionIntensity=originalReflection;
        if(canvas)Destroy(canvas.gameObject);
        if(music)Destroy(music.gameObject);if(howl)Destroy(howl.gameObject);
    }
}
