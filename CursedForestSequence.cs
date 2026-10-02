using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(120)]
public sealed class CursedForestSequence : MonoBehaviour
{
    public GameObject creature;
    public Animator creatureAnimator;
    public Transform watchPoint, escapePoint, dawnSpawn;
    public Transform[] retreatWaypoints;
    [Range(0,1)] public float revealVolume=.40f;
    public AudioClip revealSting, escapeLeaves;
    [Tooltip("Snow steps for the escape; falls back to the player's snow bank.")] public AudioClip[] escapeSteps;
    public Vector2 dawnMapMinimum=new Vector2(180,180),dawnMapMaximum=new Vector2(308,340);
    public bool IsBusy {get;private set;}
    public bool EncounterCompleted {get;private set;}
    public bool ArrivedAtDawn {get;private set;}
    public bool EncounterStarted {get;private set;}
    OpeningSequence story;HouseSearchProgress search;InventoryUI inventory;PlayerMovement movement;
    AudioSource sting,leaves;AudioLowPassFilter stepFilter;PlayerRelativeSpatialSound leafPosition;TopDownCamera cameraRig;
    Transform headBone;Quaternion watchingHeadLocal;Vector3 faceAxisLocal;bool transitioning,gazing;
    // Stance travel / stance fraction: .96/.64 for feet, 1.02/.66 for hands.
    const float CrawlMetersPerCycle=1.52f;
    const float StepsPerCycle=3f;
    public float CrawlDistance {get;private set;}
    public float CrawlPhase {get;private set;}
    float Delta=>PauseMenu.IsOpen?0:Time.unscaledDeltaTime;
    void Awake()
    {
        story=GetComponent<OpeningSequence>();search=GetComponent<HouseSearchProgress>();inventory=GetComponent<InventoryUI>();movement=GetComponent<PlayerMovement>();cameraRig=Camera.main.GetComponent<TopDownCamera>();
        sting=Source("The watcher - window reveal",false,revealVolume);sting.clip=revealSting;
        // The escape is heard as quick, light paws in the snow, not a rustle of leaves.
        leaves=Source("The watcher - retreat through snow",true,1f);leaves.loop=false;
        stepFilter=leaves.gameObject.AddComponent<AudioLowPassFilter>();stepFilter.cutoffFrequency=7000;
        if(escapeSteps==null||escapeSteps.Length==0)escapeSteps=story?story.footsteps:null;
        leafPosition=leaves.gameObject.AddComponent<PlayerRelativeSpatialSound>();leafPosition.player=transform;
        if(creature)creature.SetActive(false);
        if(creature)foreach(var bone in creature.GetComponentsInChildren<Transform>(true))if(bone.name=="Head")headBone=bone;
    }
    AudioSource Source(string label,bool spatial,float volume)
    {
        var go=new GameObject(label);go.transform.SetParent(transform,false);var source=go.AddComponent<AudioSource>();source.playOnAwake=false;source.volume=volume;source.spatialBlend=spatial?1:0;source.rolloffMode=AudioRolloffMode.Linear;source.minDistance=10;source.maxDistance=35;source.dopplerLevel=0;GameAudioSettings.Route(source);return source;
    }
    void Update()
    {
        if(!creature||!dawnSpawn)return;
        if(PauseMenu.IsOpen){if(sting.isPlaying)sting.Pause();if(leaves.isPlaying)leaves.Pause();return;}
        sting.UnPause();leaves.UnPause();
        if(!EncounterStarted&&search.NoteRead&&!story.BlocksGameplay&&!inventory.IsOpen){EncounterStarted=true;StartCoroutine(Watcher());}
        if(EncounterCompleted&&search.Complete&&!transitioning&&!story.BlocksGameplay&&!inventory.IsOpen)
        {
            EasternForestRoute.Nearest(transform.position,out float distance,out float progress);
            if(transform.position.x>34&&transform.position.z>EasternForestRoute.Start.z+6&&distance<5){transitioning=true;StartCoroutine(Journey());}
        }
    }
    IEnumerator Watcher()
    {
        IsBusy=true;bool restore=movement.enabled;movement.enabled=false;
        story.SilenceDialogue();
        creature.transform.position=watchPoint.position;
        var facing=transform.position-creature.transform.position;facing.y=0;creature.transform.rotation=Quaternion.LookRotation(facing);
        creature.SetActive(true);creatureAnimator.speed=1;creatureAnimator.Play("Watching",0,0);creatureAnimator.Update(0);
        if(headBone){watchingHeadLocal=headBone.localRotation;var eye=headBone.GetComponentsInChildren<Transform>();Vector3 face=creature.transform.forward;foreach(var socket in eye)if(socket.name.StartsWith("Eye.")){face=socket.up;break;}faceAxisLocal=headBone.InverseTransformDirection(face);}gazing=true;
        // Reveal in the unchanged gameplay view immediately after putting the note away.
        sting.Play();
        for(float t=0;t<2.2f;t+=Delta)yield return null;
        gazing=false;
        IsBusy=false;movement.enabled=restore;
        creatureAnimator.CrossFade("DropToCrawl",.10f);
        Vector3 from=watchPoint.position,to=retreatWaypoints!=null&&retreatWaypoints.Length>0?retreatWaypoints[0].position:escapePoint.position,heading=(to-from).normalized;
        Quaternion start=creature.transform.rotation,end=Quaternion.LookRotation(Vector3.ProjectOnPlane(heading,Vector3.up));
        for(float t=0;t<.4f;t+=Delta){creature.transform.rotation=Quaternion.Slerp(start,end,Mathf.SmoothStep(0,1,t/.4f));yield return null;}
        // Sample the crawl from actual travelled distance, not an estimated speed.
        // Starting at .12 matches the final contact pose of DropToCrawl.
        creatureAnimator.speed=0;CrawlDistance=0;CrawlPhase=.12f;int lastContact=Mathf.FloorToInt(CrawlPhase*StepsPerCycle);
        float routeLength=0;Vector3 previous=from;
        if(retreatWaypoints!=null)foreach(var point in retreatWaypoints)if(point){routeLength+=Vector3.Distance(previous,point.position);previous=point.position;}
        float retreatDuration=Mathf.Max(2.4f,routeLength/4f);
        bool explained=false;
        for(float t=0;t<retreatDuration;t+=Delta)
        {
            float blend=Mathf.Clamp01(t/retreatDuration);Vector3 p=LeftwardEscapePosition(from,blend);var displacement=Vector3.ProjectOnPlane(p-creature.transform.position,Vector3.up);
            if(Delta>0 && displacement.sqrMagnitude>.000001f){CrawlDistance+=displacement.magnitude;creature.transform.rotation=Quaternion.LookRotation(displacement);}
            CrawlPhase=.12f+CrawlDistance/CrawlMetersPerCycle;
            creatureAnimator.Play("CrawlEscape",0,CrawlPhase);creatureAnimator.Update(0);
            if(headBone)watchingHeadLocal=headBone.localRotation;
            creature.transform.position=p;leafPosition.SetLocation(p);
            // Three audible landings per crawl cycle - an uneven, loping rhythm. As it gets
            // away the steps thin out to nothing and lose their crunch (fewer highs), so
            // they fade into the distance instead of stopping.
            int contact=Mathf.FloorToInt(CrawlPhase*StepsPerCycle);
            float away=Mathf.Clamp01(t/retreatDuration);
            stepFilter.cutoffFrequency=Mathf.Lerp(6500f,1100f,Mathf.SmoothStep(0,1,away));
            if(contact!=lastContact){lastContact=contact;SnowStep(.07f*Mathf.Pow(1f-away,1.6f));}
            if(t>=3f&&!explained){explained=true;story.Say("YOU","Something's out there. A wild animal, maybe. No wonder they were afraid to stay here.");}
            yield return null;
        }
        gazing=false;creature.SetActive(false);
        IsBusy=false;EncounterCompleted=true;
        // The player was reading at the desk and never saw the watcher.
    }
    void SnowStep(float level)
    {
        if(escapeSteps==null||escapeSteps.Length==0||level<=0)return;
        leaves.pitch=Random.Range(1.02f,1.16f);   // light, but unhurried
        leaves.PlayOneShot(escapeSteps[Random.Range(0,escapeSteps.Length)],level*Random.Range(.75f,1.1f));
    }
    IEnumerator Journey()
    {
        IsBusy=true;
        yield return story.JourneyIntoDeadForest(Arrive);
        IsBusy=false;
    }
    void Arrive()
    {
        var cc=GetComponent<CharacterController>();cc.enabled=false;transform.SetPositionAndRotation(dawnSpawn.position,dawnSpawn.rotation);cc.enabled=true;
        cameraRig.RelocateView(dawnMapMinimum,dawnMapMaximum);
        GetComponent<ShedNightSequence>().EnterDawn();
        var fog=FindFirstObjectByType<GameAtmosphereController>();if(fog)fog.SetState(GameAtmosphereController.FogState.HeavyFog);
        story.SetWeatherFog(new Color(.34f,.39f,.44f),.032f);
        ArrivedAtDawn=true;
    }
    public static float Ground(Vector3 p)
    {foreach(var t in Terrain.activeTerrains){var q=p-t.transform.position;var s=t.terrainData.size;if(q.x>=0&&q.z>=0&&q.x<=s.x&&q.z<=s.z)return t.SampleHeight(p)+t.transform.position.y;}return p.y;}
    Vector3 LeftwardEscapePosition(Vector3 from,float progress)
    {
        // Authored world-space route stays outside the walls even when the player turns.
        float length=0;Vector3 prior=from;
        if(retreatWaypoints!=null)foreach(var point in retreatWaypoints)if(point){length+=Vector3.Distance(prior,point.position);prior=point.position;}
        float remaining=Mathf.Clamp01(progress)*length;prior=from;
        if(retreatWaypoints!=null)foreach(var point in retreatWaypoints)if(point)
        {
            float segment=Vector3.Distance(prior,point.position);
            if(remaining<=segment){var p=Vector3.Lerp(prior,point.position,segment>.001f?remaining/segment:1);p.y=Ground(p);return p;}
            remaining-=segment;prior=point.position;
        }
        var result=length>0?prior:Vector3.Lerp(from,escapePoint.position,progress);result.y=Ground(result);return result;
    }
    void LateUpdate()
    {
        if(!gazing||!headBone||PauseMenu.IsOpen)return;
        var look=(transform.position+Vector3.up*.72f-headBone.position).normalized;
        // Absolute animation-space base: multiplying the previous frame's result
        // accumulates tilt between animation evaluations and makes the eyes flicker.
        var baseRotation=headBone.parent.rotation*watchingHeadLocal;
        headBone.rotation=Quaternion.FromToRotation(baseRotation*faceAxisLocal,look)*baseRotation;
    }
}
