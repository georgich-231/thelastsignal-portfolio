using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NorthernHouseEncounter : MonoBehaviour
{
    public BuildingInterior house;
    public AudioClip rustleClip;
    public AudioClip exteriorScreech;
    public Transform warningNote;
    public Transform noteReadingPosition;
    public Texture2D warningPaper;
    public const string WarningText=StoryWriting.HouseLetter;
    public Vector3 soundPosition = new Vector3(6, 5, 35);
    public float encounterZ = 30;
    public bool HeardRustle { get; private set; }
    public bool ReachedHouse { get; private set; }
    public bool ExteriorCryPlayed { get; private set; }
    OpeningSequence story;
    ShedNightSequence night;
    InventoryUI inventory;
    AudioSource rustle;
    AudioSource screech;PlayerRelativeSpatialSound screechPosition;SurvivalPromptView notePrompt;Canvas noteCanvas;bool entryStarted;
    bool reactionFinished;
    bool houseObjectiveShown;
    float rustleAllowedAt;
    public void RestoreBeforeHouseProgress()
    {
        HeardRustle=reactionFinished=houseObjectiveShown=ExteriorCryPlayed=true;
        // Trail sounds are complete; the note, interior search and watcher remain.
    }
    void Awake()
    {
        story=GetComponent<OpeningSequence>(); night=GetComponent<ShedNightSequence>();
        inventory=GetComponent<InventoryUI>();
        var source=new GameObject("Movement among the northern trees");
        source.transform.position=soundPosition;
        rustle=source.AddComponent<AudioSource>();rustle.playOnAwake=false;
        rustle.spatialBlend=1;rustle.volume=.08f;rustle.rolloffMode=AudioRolloffMode.Linear;
        rustle.minDistance=9;rustle.maxDistance=32;rustle.spread=10;rustle.priority=40;
        var spatial=source.AddComponent<PlayerRelativeSpatialSound>();spatial.player=transform;spatial.SetLocation(soundPosition);
        rustle.dopplerLevel=0;GameAudioSettings.Route(rustle);
        // The cry comes from far off in the trees to the LEFT of the northbound trail:
        // a positioned 3D source (the clip carries its own forest reverb tail and fades
        // out on its own), softened by distance rather than panned.
        var exterior=new GameObject("Distant cry in the western trees");screech=exterior.AddComponent<AudioSource>();screech.playOnAwake=false;
        screech.spatialBlend=1;screech.rolloffMode=AudioRolloffMode.Logarithmic;screech.minDistance=20;screech.maxDistance=160;screech.spread=55;screech.dopplerLevel=0;
        screech.volume=1f;screech.priority=30;screech.clip=exteriorScreech;GameAudioSettings.Route(screech);
        screechPosition=exterior.AddComponent<PlayerRelativeSpatialSound>();screechPosition.player=transform;
        exterior.AddComponent<AudioLowPassFilter>().cutoffFrequency=2800;   // air and trees take the top off at distance
        var ui=new GameObject("House document interaction",typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler));noteCanvas=ui.GetComponent<Canvas>();noteCanvas.renderMode=RenderMode.ScreenSpaceOverlay;noteCanvas.sortingOrder=650;
        var scale=ui.GetComponent<UnityEngine.UI.CanvasScaler>();scale.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1440,900);scale.matchWidthOrHeight=.5f;
        notePrompt=SurvivalPromptView.Create(ui.transform,"Warning left behind");notePrompt.Category.text="FOREST HOUSE / UNSIGNED";notePrompt.Title.text="READ NOTE";notePrompt.Detail.text="A page left on the desk.";notePrompt.Group.alpha=0;
    }
    bool Ready => story && night && night.IsNight && !story.BlocksGameplay && !(inventory && inventory.IsOpen);
    void Update()
    {
        if(!Ready) {notePrompt.Group.alpha=0; if(rustle && rustle.isPlaying)rustle.Pause();if(screech&&screech.isPlaying)screech.Pause();return; }
        if(rustle)rustle.UnPause();
        if(screech)screech.UnPause();
        if(!ExteriorCryPlayed && !story.IsSpeaking && transform.position.z>=encounterZ-18f && !house.Contains(transform.position))
        {
            ExteriorCryPlayed=true;
            // About 40 m off, left of the trail (west, the trail runs north) and a little ahead.
            screechPosition.SetLocation(transform.position+new Vector3(-36f,4f,16f));
            screech.Play();
            rustleAllowedAt=Time.time+Mathf.Max(8f,(exteriorScreech?exteriorScreech.length:0)+3f);
        }
        if(!HeardRustle && ExteriorCryPlayed && Time.time>=rustleAllowedAt && !story.IsSpeaking && transform.position.z>=encounterZ)
        {HeardRustle=true;StartCoroutine(Encounter());}
        if(reactionFinished && !ReachedHouse && house && house.Contains(transform.position))
        {ReachedHouse=true;if(!(GetComponent<HouseSearchProgress>()?.Complete ?? false))story.SetNightObjective(OpeningSequence.ChapterStep.ForestHouseReached);}
        if(reactionFinished && !houseObjectiveShown && !ReachedHouse && HouseVisibleNearby())
        {houseObjectiveShown=true;story.SetNightObjective(OpeningSequence.ChapterStep.ReachForestHouse);}
        if(!entryStarted && house && house.Contains(transform.position) && !story.IsSpeaking){entryStarted=true;StartCoroutine(HouseEntry());}
        bool near=warningNote && house && house.Contains(transform.position) && Vector3.ProjectOnPlane(warningNote.position-transform.position,Vector3.up).sqrMagnitude<2.2f*2.2f;
        notePrompt.Group.alpha=near&&!story.IsSpeaking?1:0;
        if(warningNote)notePrompt.SetWorldAnchor(warningNote.position+Vector3.up*.2f);
        if(notePrompt.Group.alpha>0 && UnityEngine.InputSystem.Keyboard.current!=null && UnityEngine.InputSystem.Keyboard.current.eKey.wasPressedThisFrame)ReadWarning();
    }
    public bool ReadWarning()
    {
        if(!Ready || !warningNote || !house.Contains(transform.position) || Vector3.ProjectOnPlane(warningNote.position-transform.position,Vector3.up).sqrMagnitude>2.2f*2.2f)return false;
        if(!story.OpenHouseDocument(warningPaper,WarningText))return false;
        // Reposition under the opaque document, then keep the normal player camera.
        if(noteReadingPosition)
        {
            var controller=GetComponent<CharacterController>();bool enabledBefore=controller&&controller.enabled;
            if(controller)controller.enabled=false;
            transform.SetPositionAndRotation(GroundedReadingPosition(controller),noteReadingPosition.rotation);
            if(controller)controller.enabled=enabledBefore;
            Camera.main.GetComponent<TopDownCamera>()?.SnapToPlayer();
            // Only the camera sees the window. The character remains facing the desk.
            var orbit=Camera.main.GetComponent<DawnThirdPersonCamera>();
            if(orbit){orbit.Enter(transform);orbit.SetLookAngles(195,8);}
        }
        return true;
    }
    IEnumerator HouseEntry()
    {
        story.Say("YOU","A house this far up. You never mentioned anyone living here.");
        while(story.IsSpeaking || !Ready)yield return null;
    }
    public Vector3 GroundedReadingPosition(CharacterController controller)
    {
        Vector3 at=noteReadingPosition.position;float floor=float.NegativeInfinity;
        foreach(var hit in Physics.RaycastAll(at+Vector3.up,Vector3.down,4f,~0,QueryTriggerInteraction.Ignore))
        {
            if(!hit.transform.IsChildOf(house.transform)||hit.normal.y<.7f)continue;
            string n=hit.collider.name;
            if(n=="floor"||n=="hallway"||n=="q10:Mesh"||n=="mainHouse01"||n.StartsWith("Room"))floor=Mathf.Max(floor,hit.point.y);
        }
        if(!float.IsNegativeInfinity(floor))
            at.y=floor+(controller?controller.height*.5f-controller.center.y:1f)+.025f;
        return at;
    }
    IEnumerator Encounter()
    {
        // A fixed source in the trees on the west/left of the northbound trail.
        rustle.GetComponent<PlayerRelativeSpatialSound>().SetLocation(new Vector3(transform.position.x-7,transform.position.y+.5f,transform.position.z+3));
        rustle.panStereo=0;
        rustle.clip=rustleClip;rustle.Play();
        float elapsed=0;
        float leadIn=rustleClip?rustleClip.length+.25f:2.5f;
        while(elapsed<leadIn){if(Ready)elapsed+=Time.unscaledDeltaTime;yield return null;}
        story.Say("YOU", "Shit... What was that? Keep moving. Just keep moving.");
        while(story.IsSpeaking || !Ready)yield return null;
        reactionFinished=true;
    }
    bool HouseVisibleNearby()
    {
        if(!house || !Camera.main)return false;
        Vector3 target=house.transform.position+Vector3.up*2f;
        if(Vector3.ProjectOnPlane(target-transform.position,Vector3.up).sqrMagnitude>18f*18f)return false;
        Vector3 viewport=Camera.main.WorldToViewportPoint(target);
        if(viewport.z<=0 || viewport.x<0 || viewport.x>1 || viewport.y<0 || viewport.y>1)return false;
        Vector3 origin=Camera.main.transform.position,delta=target-origin;
        var hits=Physics.RaycastAll(origin,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
        foreach(var hit in hits)
        {
            if(hit.transform.IsChildOf(transform))continue;
            return hit.transform.IsChildOf(house.transform);
        }
        return true;
    }
    void OnDestroy(){if(rustle)Destroy(rustle.gameObject);if(screech)Destroy(screech.gameObject);if(noteCanvas)Destroy(noteCanvas.gameObject);}
}
