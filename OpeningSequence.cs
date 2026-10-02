using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(-70)]
[DisallowMultipleComponent]
public sealed class OpeningSequence : MonoBehaviour
{
    public enum ChapterStep { FollowTrail, FindLighter, LightFire, ReadNote, Complete, InspectSurroundings, DeeperForest, ReturnToShed, SleepUntilMorning, MidnightAwake, ReachForestHouse, ForestHouseReached, FollowEasternBootMarks }
    public BuildingInterior outpost;
    public BonfireInteractable bonfire;
    public Transform note;
    public AudioClip windClip, radioClip;
    public AudioClip[] footsteps;
    public AudioClip[] dirtFootsteps, woodFootsteps;
    public string CurrentSurface { get; private set; } = "Snow";
    Terrain[] terrains; LayeredSnow[] snowLayers;
    ParticleSystem snowfall;
    Transform[] windTrees; Quaternion[] treeRest;
    CanvasGroup prologueText; Text prologueBody,prologueSkip; bool skipPrologue;
    public bool InPrologue => introFading;
    public void SkipPrologue() { skipPrologue=true; }
    float objectiveShownAt=999999f,objectiveHideAt=-1f,hudStartedAt=999999f;
    float PresentationDelta => PauseMenu.IsOpen?0:Time.unscaledDeltaTime;
    public float WeatherSeverity { get; private set; }
    public float QuickbarAlpha => introFading?0:Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.time-hudStartedAt)/.8f));
    public string CurrentObjectiveText => objective?objective.text:"Follow the trail to the forestry outpost";
    public ChapterStep Step { get; private set; }
    public bool IsSpeaking => speech!=null;
    public bool IsReading { get; private set; }
    public bool BlocksGameplay => IsReading || introFading || PauseMenu.IsOpen || (GetComponent<ShedNightSequence>()?.IsBusy ?? false) || (GetComponent<CursedForestSequence>()?.IsBusy ?? false) || (GetComponent<DawnWolfEncounter>()?.IsBusy ?? false);
    public bool CanReadNote => Step>=ChapterStep.ReadNote && NearNote() && !(GetComponent<ShedNightSequence>()?.CanUseBed ?? false);
    public const string Letter=StoryWriting.BrotherLetter;
    PlayerMovement movement;PlayerInventory inventory;InventoryUI inventoryUI;
    CharacterController controller;CharacterLocomotionBalance balance;
    Canvas canvas;CanvasGroup objectiveGroup,subtitleGroup,reader,fade;
    Text objective,subtitle,speaker,transcriptToggleLabel;
    Text documentTitle,documentHeading,documentTranscript,documentWriting;
    RawImage documentPage;bool externalDocument;
    CanvasGroup transcriptOverlay;
    public bool TranscriptVisible { get; private set; }
    int[] dirtOrder; int dirtCursor, lastDirt=-1;
    SurvivalPromptView notePrompt;
    AudioSource wind,radio,boots;
    // The wind loop is a park field recording with birdsong in its upper band.
    // At night a low-pass keeps only the wind body: no birds in the dark.
    AudioLowPassFilter windFilter;ShedNightSequence nightState;
    bool introFading,wasMoving,enteredShelter,hadLighter;
    bool[] lifted=new bool[2];float nextStep;int footIndex;
    Vector3 previous;float travelled;float started;
    bool originalFog;float originalDensity;Color originalFogColor;
    bool priorCursor;CursorLockMode priorLock;GameObject ownedEvents;
    readonly Queue<string[]> lines=new Queue<string[]>();Coroutine speech;
    void Awake()
    {
        movement=GetComponent<PlayerMovement>();inventory=GetComponent<PlayerInventory>();inventoryUI=GetComponent<InventoryUI>();
        controller=GetComponent<CharacterController>();balance=GetComponent<CharacterLocomotionBalance>();
        terrains=Terrain.activeTerrains;snowLayers=new LayeredSnow[terrains.Length];for(int i=0;i<terrains.Length;i++)snowLayers[i]=terrains[i].GetComponent<LayeredSnow>();
        var snow=FindFirstObjectByType<LocalizedSnowfall>();if(snow)snowfall=snow.GetComponent<ParticleSystem>();
        var treeGroup=GameObject.Find("Dense trail edges");var trees=new List<Transform>();if(treeGroup)foreach(Transform t in treeGroup.transform){var lod=t.GetComponentInChildren<LODGroup>();if(lod)trees.Add(lod.transform);}
        windTrees=trees.ToArray();treeRest=new Quaternion[windTrees.Length];for(int i=0;i<windTrees.Length;i++)treeRest[i]=windTrees[i].localRotation;
        BuildUI();wind=Audio("Valley wind",windClip,true,.18f);windFilter=wind.gameObject.AddComponent<AudioLowPassFilter>();windFilter.cutoffFrequency=22000;windFilter.lowpassResonanceQ=1;nightState=GetComponent<ShedNightSequence>();radio=Audio("Handheld radio",radioClip,false,.24f);boots=Audio("Snow footsteps",null,false,.14f);
        originalFog=RenderSettings.fog;originalDensity=RenderSettings.fogDensity;originalFogColor=RenderSettings.fogColor;
    }
    AudioSource Audio(string title,AudioClip clip,bool loop,float volume)
    {
        var go=new GameObject(title);go.transform.SetParent(transform,false);var source=go.AddComponent<AudioSource>();
        GameAudioSettings.Route(source);source.clip=clip;source.loop=loop;source.spatialBlend=0;source.volume=volume;source.playOnAwake=false;
        if(loop && clip)source.Play();return source;
    }
    void Start()
    {
        var worldCamera=Camera.main;
        if(worldCamera)
        {
            var orbit=worldCamera.GetComponent<DawnThirdPersonCamera>();
            if(!orbit)orbit=worldCamera.gameObject.AddComponent<DawnThirdPersonCamera>();
            orbit.Enter(transform);
        }
        var launch=GetComponent<StoryLaunchSettings>();
        if(launch && launch.startAtHouse){StartPreparedNight();StartPreparedHouse(launch);return;}
        if(launch && launch.startAtMidnight){StartPreparedNight();return;}
        previous=transform.position;started=Time.time;Step=ChapterStep.FollowTrail;RefreshObjective();StartCoroutine(Introduction());
    }
    void StartPreparedNight()
    {
        enteredShelter=hadLighter=true;introFading=false;fade.alpha=prologueText.alpha=0;
        prologueSkip.gameObject.SetActive(false);started=hudStartedAt=Time.time;
        var item=FindFirstObjectByType<LighterItem>(FindObjectsInactive.Include);
        if(item)inventory.TryCollect(item.GetComponent<InspectablePickup>());
        inventory.SelectQuickSlot(0);bonfire.SetLit(true);
        var focus=GetComponent<ForestFocus>();if(!focus)focus=gameObject.AddComponent<ForestFocus>();focus.RestoreDiscoveredProgress();
        var start=new Vector3(5.2f,0,13.4f);float floor=0;
        foreach(var terrain in Terrain.activeTerrains){var p=start-terrain.transform.position;var size=terrain.terrainData.size;if(p.x>=0&&p.z>=0&&p.x<=size.x&&p.z<=size.z)floor=terrain.SampleHeight(start)+terrain.transform.position.y;}
        movement.enabled=false;controller.enabled=false;transform.position=new Vector3(start.x,floor+controller.height*.5f+.12f,start.z);transform.rotation=Quaternion.identity;controller.enabled=true;
        previous=transform.position;GetComponent<ShedNightSequence>().StartPreparedNight();
    }
    void StartPreparedHouse(StoryLaunchSettings launch)
    {
        var house=GetComponent<NorthernHouseEncounter>();
        var spawn=launch.houseSpawn;
        Vector3 position=spawn?spawn.position:house.house.transform.position+house.house.transform.forward*10f;
        Quaternion rotation=spawn?spawn.rotation:Quaternion.LookRotation(Vector3.ProjectOnPlane(house.house.transform.position-position,Vector3.up));
        position.y=CursedForestSequence.Ground(position)+controller.height*.5f+.12f;
        controller.enabled=false;transform.SetPositionAndRotation(position,rotation);controller.enabled=true;
        previous=transform.position;house.RestoreBeforeHouseProgress();
        GetComponent<HeldLighter>()?.RestoreIntroducedProgress();
        SetNightObjective(ChapterStep.ReachForestHouse);
        Camera.main.GetComponent<DawnThirdPersonCamera>()?.Enter(transform);
    }
    IEnumerator Introduction()
    {
        introFading=true;wasMoving=movement.enabled;movement.enabled=false;fade.alpha=1;
        string[] cards=StoryWriting.Prologue;
        foreach(string card in cards)
        {
            if(skipPrologue)break;prologueBody.text=card;
            float duration=Mathf.Max(5.5f,card.Length*.07f);
            for(float t=0;t<duration && !skipPrologue;t+=PresentationDelta)
            { prologueText.alpha=Mathf.Min(Mathf.Clamp01(t/.8f),Mathf.Clamp01((duration-t)/.7f));yield return null; }
        }
        prologueText.alpha=0;prologueSkip.gameObject.SetActive(false);
        for(float t=0;t<1.6f;t+=PresentationDelta){fade.alpha=1-Mathf.SmoothStep(0,1,t/1.6f);yield return null;}
        fade.alpha=0;introFading=false;movement.enabled=wasMoving;started=Time.time;hudStartedAt=Time.time;objectiveShownAt=Time.time+1.15f;objectiveHideAt=objectiveShownAt+7;
        Vector3 arrival=transform.position;float arrivalWait=0;
        while(arrivalWait<10 && (arrivalWait<3 || Vector3.Distance(arrival,transform.position)<3))
        {if(!inventoryUI.IsOpen && !IsReading)arrivalWait+=PresentationDelta;yield return null;}
        Say("YOU / RADIO","It's me. I've brought the papers. You can stop avoiding the radio now.");
        Say("YOU","We can argue indoors. Just tell me you're there.");
    }
    void Update()
    {
        if(!outpost || !bonfire)return;
        if(PauseMenu.IsOpen){fade.gameObject.SetActive(false);objectiveGroup.alpha=0;subtitleGroup.alpha=0;notePrompt.Group.alpha=0;return;}
        fade.gameObject.SetActive(true);
        float gust=.5f+.5f*Mathf.Sin(Time.time*.28f+Mathf.Sin(Time.time*.071f));
        float storm=Mathf.Lerp(.3f,1,Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.time-started)/100)));
        float strength=.35f+storm*.4f+gust*.25f;WeatherSeverity=strength;
        bool sheltered=BuildingInterior.FindContaining(transform)!=null;
        bool dark=nightState&&nightState.IsNight&&!nightState.IsDawn;
        windFilter.cutoffFrequency=Mathf.MoveTowards(windFilter.cutoffFrequency,dark?520f:22000f,Time.unscaledDeltaTime*(dark?40000f:4000f));
        wind.volume=(introFading?.07f:Mathf.Lerp(.14f,.29f,strength))*(sheltered?.28f:1)*(dark?1.35f:1f);   // make up the energy the filter removes
        RenderSettings.fog=originalFog;RenderSettings.fogDensity=originalDensity;
        if(snowfall)
        {
            var main=snowfall.main;main.maxParticles=4200;main.startSpeed=0;
            // Keeps falling outside the windows; LocalizedSnowfall removes any flake inside a building.
            var emission=snowfall.emission;emission.rateOverTime=Mathf.Lerp(170,320,strength)*(sheltered?.6f:1f);
            var velocity=snowfall.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.x=Mathf.Lerp(.35f,1.35f,strength);velocity.y=-1.1f;velocity.z=.15f+gust*.35f;
            var noise=snowfall.noise;noise.enabled=true;noise.strength=.16f;noise.frequency=.22f;
        }
        for(int i=0;i<windTrees.Length;i++)if(windTrees[i])windTrees[i].localRotation=treeRest[i]*Quaternion.Euler(.25f*Mathf.Sin(Time.time*.7f+i),0,strength*1.4f*Mathf.Sin(Time.time*.65f+i*.37f));
        bool inside=outpost.Contains(transform.position);
        if(!enteredShelter && inside)
        {
            enteredShelter=true;Say("YOU","You still have Dad's lighter. Thought you'd lost it.");
            if(Step==ChapterStep.FollowTrail)SetStep(ChapterStep.FindLighter);
        }
        bool carrying=false;foreach(var item in inventory.slots)if(item is LighterItem){carrying=true;break;}
        if(carrying && !hadLighter)
        {
            hadLighter=true;if(Step<ChapterStep.LightFire)SetStep(ChapterStep.LightFire);
        }
        if(bonfire.IsLit && Step<ChapterStep.ReadNote)
        {
            SetStep(ChapterStep.ReadNote);Say("YOU","There. You'd tell me I used too much kindling.");
        }
        bool gameplay=!inventoryUI.IsOpen && !BlocksGameplay && Time.timeScale>0 && !GetComponent<GameplayInteraction>().IsBusy;
        float objectiveAlpha=Mathf.SmoothStep(0,1,Mathf.Clamp01((Time.time-objectiveShownAt)/.45f))*Mathf.SmoothStep(0,1,Mathf.Clamp01((objectiveHideAt-Time.time)/.6f));
        objectiveGroup.alpha=inventoryUI.IsOpen||IsReading||introFading?0:objectiveAlpha;
        ((RectTransform)objectiveGroup.transform).anchoredPosition=new Vector2(40-(1-objectiveAlpha)*22,-34);
        subtitleGroup.alpha=inventoryUI.IsOpen||IsReading||introFading?0:(subtitle.text.Length>0?1:0);
        notePrompt.Group.alpha=gameplay && CanReadNote?1:0;
        if(note)notePrompt.SetWorldAnchor(note.position+Vector3.up*.25f);
#if ENABLE_INPUT_SYSTEM
        var key=Keyboard.current;
        if(gameplay && key!=null && key.xKey.wasPressedThisFrame && Time.time>=objectiveHideAt && objectiveGroup.alpha<=.01f)RevealObjective();
        if(introFading && key!=null && key.spaceKey.wasPressedThisFrame)SkipPrologue();
        if(IsReading && key!=null && key.tKey.wasPressedThisFrame)ToggleTranscript();
        if(IsReading && key!=null && key.escapeKey.wasPressedThisFrame)CloseNote();
        else if(gameplay && CanReadNote && key!=null && key.eKey.wasPressedThisFrame)OpenNote();
#endif
    }
    bool NearNote()
    {
        if(!note || !outpost.Contains(transform.position))return false;
        Vector3 delta=transform.position-note.position;delta.y=0;return delta.sqrMagnitude<2.3f*2.3f;
    }
    void SetStep(ChapterStep next){if(Step!=next)GetComponent<SurvivalAudio>()?.Objective();Step=next;RevealObjective();RefreshObjective();}
    public void SetFocusObjective(bool discovered){SetStep(discovered?ChapterStep.DeeperForest:ChapterStep.InspectSurroundings);}
    public void RevealObjective(){objectiveShownAt=Time.time;objectiveHideAt=Time.time+7;}
    public void SetNightObjective(ChapterStep next) { if(next>=ChapterStep.ReturnToShed)SetStep(next); }
    void RefreshObjective()
    {
        if(Step==ChapterStep.ReachForestHouse){objective.text="Explore the house";return;}
        if(Step==ChapterStep.ForestHouseReached){objective.text="Search the house";return;}
        if(Step==ChapterStep.FollowEasternBootMarks){objective.text="Follow the boot marks beyond the house";return;}
        objective.text=Step==ChapterStep.FollowTrail?"Follow the trail to the forestry outpost":Step==ChapterStep.FindLighter?"Search the shed for something to start a fire":Step==ChapterStep.LightFire?"Use your lighter to light the bonfire":Step==ChapterStep.ReadNote?"Read the note in the shed":Step==ChapterStep.InspectSurroundings?"Inspect the surroundings":Step==ChapterStep.DeeperForest?"Go deeper into the forest":Step==ChapterStep.ReturnToShed?"Return to the shed for the night":Step==ChapterStep.SleepUntilMorning?"Rest on the bed inside the shed":Step==ChapterStep.MidnightAwake?"Find the trail beyond the outpost":"Your brother's note found";
    }
    public bool OpenNote()
    {
        if(!CanReadNote || inventoryUI.IsOpen || BlocksGameplay)return false;
        ConfigureDocument(false,null,null);
        return BeginReading();
    }
    public IEnumerator JourneyIntoDeadForest(System.Action arrive)
    {
        bool restore=movement.enabled;movement.enabled=false;introFading=true;skipPrologue=false;
        if(speech!=null){StopCoroutine(speech);speech=null;}lines.Clear();subtitle.text="";
        fade.alpha=1;prologueText.alpha=0;prologueSkip.gameObject.SetActive(true);
        string[] cards=StoryWriting.Journey;
        yield return null;
        foreach(var card in cards)
        {
            if(skipPrologue)break;prologueBody.text=card;
            float duration=Mathf.Max(6,card.Length*.063f);
            for(float t=0;t<duration&&!skipPrologue;t+=PresentationDelta){prologueText.alpha=Mathf.Min(Mathf.Clamp01(t/.8f),Mathf.Clamp01((duration-t)/.8f));yield return null;}
        }
        prologueText.alpha=0;prologueSkip.gameObject.SetActive(false);arrive();
        yield return null;yield return null;
        for(float t=0;t<2.2f;t+=PresentationDelta){fade.alpha=1-Mathf.SmoothStep(0,1,t/2.2f);yield return null;}
        fade.alpha=0;introFading=false;movement.enabled=restore;previous=transform.position;
        hudStartedAt=Time.time;objectiveShownAt=Time.time+1.2f;objectiveHideAt=objectiveShownAt+7;
    }
    public void SetWeatherFog(Color color,float density)
    {originalFog=true;originalFogColor=color;originalDensity=density;RenderSettings.fogColor=color;RenderSettings.fogMode=FogMode.ExponentialSquared;}
    public bool OpenHouseDocument(Texture2D paper,string text)
    {
        if(inventoryUI.IsOpen || BlocksGameplay)return false;
        ConfigureDocument(true,paper,text);return BeginReading();
    }
    void ConfigureDocument(bool external,Texture2D paper,string text)
    {
        externalDocument=external;
        var artwork=Resources.Load<Texture2D>(external?"Story/HouseHandwrittenV2":"Story/BrotherHandwrittenV2");
        documentPage.texture=artwork?artwork:Texture2D.whiteTexture;
        documentPage.color=artwork?Color.white:new Color(.83f,.80f,.71f,1);
        documentTitle.text=external?"DOCUMENT  /  02":"DOCUMENT  /  01";
        documentHeading.text=external?"UNSIGNED NOTE":"YOUR BROTHER'S NOTE";
        documentTranscript.text=external?text:Letter;documentWriting.text=documentTranscript.text;
        SurvivalUITheme.Place((RectTransform)transcriptOverlay.transform,184,100,452,580);
        documentTranscript.rectTransform.sizeDelta=new Vector2(396,464);
        documentTranscript.fontSize=22;documentTranscript.lineSpacing=1.1f;
        documentWriting.fontSize=22;documentWriting.lineSpacing=1.1f;
        documentWriting.gameObject.SetActive(!artwork);
    }
    bool BeginReading()
    {
        if(UnityEngine.EventSystems.EventSystem.current==null)
        {
            ownedEvents=new GameObject("Story Event System",typeof(UnityEngine.EventSystems.EventSystem));
#if ENABLE_INPUT_SYSTEM
            ownedEvents.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            ownedEvents.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }
        priorCursor=Cursor.visible;priorLock=Cursor.lockState;Cursor.visible=true;Cursor.lockState=CursorLockMode.None;
        GetComponent<SurvivalAudio>()?.Paper();SetTranscript(false);wasMoving=movement.enabled;movement.enabled=false;IsReading=true;reader.alpha=1;reader.blocksRaycasts=true;notePrompt.Group.alpha=0;return true;
    }
    public void ToggleTranscript()
    {
        if(IsReading)SetTranscript(!TranscriptVisible);
    }
    void SetTranscript(bool visible)
    {
        TranscriptVisible=visible;transcriptOverlay.alpha=visible?1:0;
        documentWriting.gameObject.SetActive(!visible && documentPage.texture==Texture2D.whiteTexture);
        transcriptToggleLabel.text=visible?"VIEW LETTER":"READ TRANSCRIPT";
    }
    public void CloseNote()
    {
        if(IsReading && externalDocument)GetComponent<HouseSearchProgress>()?.RecordNoteRead();
        if(!IsReading)return;GetComponent<SurvivalAudio>()?.Paper(true);IsReading=false;reader.alpha=0;reader.blocksRaycasts=false;movement.enabled=wasMoving;
        Cursor.visible=priorCursor;Cursor.lockState=priorLock;
        bool first=!externalDocument && Step<ChapterStep.Complete;if(first){SetStep(ChapterStep.Complete);var focus=GetComponent<ForestFocus>();if(!focus)focus=gameObject.AddComponent<ForestFocus>();focus.Unlock();}
    }
    public void Say(string who,string text)
    {
        lines.Enqueue(new[]{who,text});if(speech==null)speech=StartCoroutine(PlayLines());
    }
    public void SilenceDialogue(){if(speech!=null)StopCoroutine(speech);speech=null;lines.Clear();subtitle.text="";subtitleGroup.alpha=0;}
    IEnumerator PlayLines()
    {
        while(lines.Count>0)
        {
            var line=lines.Dequeue();speaker.text=line[0];subtitle.text="";
            for(int i=0;i<line[1].Length;i++)
            {
                // The unrevealed rest stays in place, transparent, so centred lines never shift as they type.
                subtitle.text=line[1].Substring(0,i+1)+(i+1<line[1].Length?"<color=#00000000>"+line[1].Substring(i+1)+"</color>":"");
                char letter=line[1][i];GetComponent<SurvivalAudio>()?.TextLetter(letter);float delay=letter=='.'||letter=='?'||letter=='!'?.22f:letter==','?.11f:.032f;
                for(float elapsed=0;elapsed<delay;){if(!inventoryUI.IsOpen && !IsReading)elapsed+=PresentationDelta;yield return null;}
            }
            // Let unanswered silence carry the scene; no synthetic radio hiss.
            for(float t=0;t<Mathf.Max(3.2f,line[1].Length*.047f);){if(!inventoryUI.IsOpen && !IsReading)t+=PresentationDelta;yield return null;}
            subtitle.text="";yield return new WaitForSecondsRealtime(.35f);
        }
        speech=null;
    }
    void LateUpdate()
    {
        Vector3 delta=transform.position-previous;previous=transform.position;delta.y=0;
        if(delta.magnitude>2){travelled=0;return;}
        travelled+=delta.magnitude;
        if(!controller.isGrounded || inventoryUI.IsOpen || BlocksGameplay || footsteps==null || footsteps.Length==0)return;
        bool contact=false;
        if(balance && balance.IsReady)
        {
            for(int i=0;i<2;i++){float lift=balance.FootLift(i);if(lift>.035f)lifted[i]=true;if(lift<.018f && lifted[i]){lifted[i]=false;contact=true;}}
        }
        else if(travelled>.57f){travelled=0;contact=true;}
        if(contact && delta.sqrMagnitude>.000001f && Time.time>nextStep)
        {
            var bank=SurfaceBank(transform.position);if(bank==null || bank.Length==0)bank=footsteps;
            bool dirt=CurrentSurface=="Dirt";
            boots.pitch=dirt?Random.Range(.975f,1.035f):.97f+(footIndex%3)*.025f;
            boots.volume=CurrentSurface=="Snow"?.065f:CurrentSurface=="Wood"?.40f:Random.Range(.06f,.07f);
            int sample=dirt?NextDirtSample(bank.Length):footIndex%bank.Length;footIndex++;
            boots.PlayOneShot(bank[sample]);nextStep=Time.time+.16f;
        }
    }
    int NextDirtSample(int count)
    {
        if(dirtOrder==null || dirtOrder.Length!=count){dirtOrder=new int[count];dirtCursor=count;}
        if(dirtCursor>=count)
        {
            for(int i=0;i<count;i++)dirtOrder[i]=i;
            for(int i=count-1;i>0;i--){int j=Random.Range(0,i+1);int swap=dirtOrder[i];dirtOrder[i]=dirtOrder[j];dirtOrder[j]=swap;}
            if(count>1 && dirtOrder[0]==lastDirt){int j=Random.Range(1,count);int swap=dirtOrder[0];dirtOrder[0]=dirtOrder[j];dirtOrder[j]=swap;}
            dirtCursor=0;
        }
        lastDirt=dirtOrder[dirtCursor++];return lastDirt;
    }
    AudioClip[] SurfaceBank(Vector3 point)
    {
        if(BuildingInterior.Active.Exists(b=>b && b.Contains(point))){CurrentSurface="Wood";return woodFootsteps;}
        for(int i=0;i<terrains.Length;i++)
        {
            var t=terrains[i];if(!t)continue;var p=point-t.transform.position;var size=t.terrainData.size;
            if(p.x<0||p.z<0||p.x>size.x||p.z>size.z)continue;
            float coverage=snowLayers[i]?snowLayers[i].CoverageAt(point):1;
            CurrentSurface=coverage<.3f?"Dirt":"Snow";return coverage<.3f?dirtFootsteps:footsteps;
        }
        CurrentSurface="Snow";return footsteps;
    }
    public string SurfaceAt(Vector3 point){SurfaceBank(point);return CurrentSurface;}
    CanvasGroup Group(Transform parent,string name)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(CanvasGroup));go.transform.SetParent(parent,false);return go.GetComponent<CanvasGroup>();
    }
    void BuildUI()
    {
        var go=new GameObject("Opening chapter",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=1000;
        var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1440,900);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        objectiveGroup=Group(go.transform,"Chapter objective");SurvivalUITheme.Place((RectTransform)objectiveGroup.transform,40,34,365,110);objectiveGroup.blocksRaycasts=false;
        objective=ObjectiveCardView.Build(objectiveGroup.transform);
        // Dialogue: cinematic, centred, no box. A soft oval scrim keeps the line legible
        // over bright snow; the speaker's name sits above it in tracked frost capitals.
        subtitleGroup=Group(go.transform,"Dialogue");var sr=(RectTransform)subtitleGroup.transform;sr.anchorMin=sr.anchorMax=new Vector2(.5f,0);sr.pivot=new Vector2(.5f,0);sr.anchoredPosition=new Vector2(0,118);sr.sizeDelta=new Vector2(900,96);subtitleGroup.blocksRaycasts=false;
        var scrim=new GameObject("Dialogue scrim",typeof(RectTransform),typeof(Image));scrim.transform.SetParent(sr,false);SurvivalUITheme.Place(scrim.GetComponent<RectTransform>(),-60,-30,1020,150);var scrimImage=scrim.GetComponent<Image>();scrimImage.sprite=SurvivalUITheme.OvalScrimSprite;scrimImage.color=new Color(1,1,1,.9f);scrimImage.raycastTarget=false;
        speaker=SurvivalUITheme.Text(sr,"SPEAKER",0,10,900,14,10,false,SurvivalUITheme.Frost,TextAnchor.MiddleCenter);speaker.text="";
        subtitle=SurvivalUITheme.Text(sr,"",90,32,720,60,21,false,SurvivalUITheme.Snow,TextAnchor.UpperCenter);subtitle.lineSpacing=1.12f;SurvivalUITheme.Soften(subtitle,.75f,1.5f);SurvivalUITheme.Soften(speaker,.6f,1f);
        notePrompt=SurvivalPromptView.Create(go.transform,"Brother's note prompt");notePrompt.Category.text="FORESTRY OUTPOST";notePrompt.Title.text="READ NOTE";notePrompt.Detail.text="Your brother's handwriting.";
        reader=Group(go.transform,"Note reader");var rr=(RectTransform)reader.transform;rr.anchorMin=Vector2.zero;rr.anchorMax=Vector2.one;rr.offsetMin=rr.offsetMax=Vector2.zero;reader.alpha=0;reader.blocksRaycasts=false;
        var shade=new GameObject("Reading shade",typeof(RectTransform),typeof(Image));shade.transform.SetParent(rr,false);var rect=shade.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;shade.GetComponent<Image>().color=new Color(.009f,.015f,.022f,.97f);
        var stage=Group(rr,"Document inspection");var stageRect=(RectTransform)stage.transform;stageRect.anchorMin=stageRect.anchorMax=new Vector2(.5f,.5f);stageRect.pivot=Vector2.one*.5f;stageRect.sizeDelta=new Vector2(820,760);stageRect.anchoredPosition=Vector2.zero;
        documentTitle=SurvivalUITheme.Text(stageRect,"DOCUMENT  /  01",0,5,820,18,11,false,SurvivalUITheme.Secondary,TextAnchor.MiddleCenter);
        var pageGO=new GameObject("Physical paper",typeof(RectTransform),typeof(RawImage));pageGO.transform.SetParent(stageRect,false);var page=documentPage=pageGO.GetComponent<RawImage>();page.texture=Resources.Load<Texture2D>("Story/BrothersNote");page.raycastTarget=false;
        SurvivalUITheme.Place(page.rectTransform,159,39,502,676);page.rectTransform.localRotation=Quaternion.Euler(0,0,1.3f);
        documentWriting=SurvivalUITheme.Text(page.transform,"",53,106,392,515,24,false,new Color(.19f,.16f,.12f));
        documentWriting.font=Resources.Load<Font>("Fonts/PrologueSerif")??SurvivalUITheme.Body;documentWriting.fontStyle=FontStyle.Italic;documentWriting.lineSpacing=1.15f;documentWriting.gameObject.SetActive(false);
        transcriptOverlay=Group(stageRect,"Optional transcript overlay");SurvivalUITheme.Place((RectTransform)transcriptOverlay.transform,204,231,412,276);transcriptOverlay.alpha=0;transcriptOverlay.blocksRaycasts=false;
        var transcriptShade=transcriptOverlay.gameObject.AddComponent<Image>();transcriptShade.color=new Color(.02f,.026f,.029f,.94f);transcriptShade.raycastTarget=false;
        documentHeading=SurvivalUITheme.Text(transcriptOverlay.transform,"YOUR BROTHER'S NOTE",28,24,356,22,11,false,SurvivalUITheme.Secondary);
        SurvivalUITheme.Line(transcriptOverlay.transform,28,59,356,new Color(.5f,.6f,.62f,.25f));
        var transcription=documentTranscript=SurvivalUITheme.Text(transcriptOverlay.transform,Letter,28,84,356,310,24,false,new Color(.94f,.93f,.88f));
        var readingFont=Resources.Load<Font>("Fonts/PrologueSerif");if(readingFont)transcription.font=readingFont;transcription.lineSpacing=1.25f;
        var toggle=SurvivalUITheme.Surface(stageRect,"Toggle transcript",164,721,254,39,true);var toggleButton=toggle.gameObject.AddComponent<Button>();toggleButton.targetGraphic=toggle;toggleButton.onClick.AddListener(ToggleTranscript);
        var toggleColors=toggleButton.colors;toggleColors.highlightedColor=new Color(.65f,.8f,.86f);toggleButton.colors=toggleColors;
        SurvivalUITheme.Key(toggle.transform,"T",10,6,27);transcriptToggleLabel=SurvivalUITheme.Text(toggle.transform,"READ TRANSCRIPT",50,10,196,20,15,true);SurvivalUITheme.Track(transcriptToggleLabel,.1f);
        var close=SurvivalUITheme.Surface(stageRect,"Close note",435,721,221,39,true);var button=close.gameObject.AddComponent<Button>();button.onClick.AddListener(CloseNote);button.targetGraphic=close;var colors=button.colors;colors.highlightedColor=new Color(.65f,.8f,.86f);button.colors=colors;
        SurvivalUITheme.Key(close.transform,"ESC",10,6,27);SurvivalUITheme.Track(SurvivalUITheme.Text(close.transform,"PUT AWAY",60,10,150,20,15,true),.1f);
        fade=Group(go.transform,"Arrival fade");var fr=(RectTransform)fade.transform;fr.anchorMin=Vector2.zero;fr.anchorMax=Vector2.one;fr.offsetMin=fr.offsetMax=Vector2.zero;fade.gameObject.AddComponent<Image>().color=Color.black;fade.alpha=1;fade.blocksRaycasts=false;
        prologueText=Group(fr,"Prologue text");var pr=(RectTransform)prologueText.transform;pr.anchorMin=pr.anchorMax=Vector2.one*.5f;pr.pivot=Vector2.one*.5f;pr.sizeDelta=new Vector2(790,350);pr.anchoredPosition=new Vector2(0,18);
        SurvivalUITheme.Text(pr,StoryWriting.GameTitle.ToUpperInvariant(),0,12,790,24,14,false,SurvivalUITheme.Secondary);
        SurvivalUITheme.Line(pr,0,54,84,new Color(.60f,.70f,.73f,.65f));
        prologueBody=SurvivalUITheme.Text(pr,"",0,105,790,207,33,false,new Color(.87f,.88f,.86f),TextAnchor.UpperLeft);
        var serif=Resources.Load<Font>("Fonts/PrologueSerif");if(serif)prologueBody.font=serif;prologueBody.lineSpacing=1.25f;
        var skip=prologueSkip=SurvivalUITheme.Text(fr,"PRESS SPACE TO SKIP",0,0,260,24,10,false,new Color(.45f,.53f,.58f),TextAnchor.MiddleCenter);skip.rectTransform.anchorMin=skip.rectTransform.anchorMax=new Vector2(.5f,0);skip.rectTransform.pivot=new Vector2(.5f,0);skip.rectTransform.anchoredPosition=new Vector2(0,44);


    }
    void OnDisable()
    {
        if(movement && BlocksGameplay)movement.enabled=wasMoving;if(IsReading){Cursor.visible=priorCursor;Cursor.lockState=priorLock;}IsReading=introFading=false;
        StopAllCoroutines();speech=null;lines.Clear();if(canvas)canvas.gameObject.SetActive(false);
        if(wind)wind.Stop();if(radio)radio.Stop();if(boots)boots.Stop();
        RenderSettings.fog=originalFog;RenderSettings.fogDensity=originalDensity;RenderSettings.fogColor=originalFogColor;
    }
    void OnDestroy(){if(canvas)Destroy(canvas.gameObject);if(ownedEvents)Destroy(ownedEvents);}
}
