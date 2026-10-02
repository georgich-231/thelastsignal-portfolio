using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// One authored encounter. Animation and movement share the same travelled distance.
[DefaultExecutionOrder(260)]
public sealed class DawnWolfEncounter : MonoBehaviour
{
    public GameObject wolf;
    public Animator wolfAnimator;
    public AudioClip growl,whimper,rifleShot;
    public AudioClip[] snowSteps;
    public Material bloodMaterial;
    public float triggerZ=228;
    public enum Phase { Waiting, Listening, Charging, Focus, Wounded, Retreating, Complete }
    public Phase Stage {get;private set;}
    public bool IsBusy=>Stage>=Phase.Listening&&Stage<=Phase.Wounded;
    public bool Completed=>Stage==Phase.Complete;
    public int BloodPatchCount {get;private set;}
    public Vector3 TargetPoint=>wolf.transform.position+Vector3.up*.65f;
    public bool AimOnTarget {get;private set;}
    CursedForestSequence journey;OpeningSequence story;PlayerInventory pack;InventoryUI inventory;
    PlayerMovement movement;HeldRifle rifle;ForestFocus focus;
    AudioSource voice,paws,shot;PlayerRelativeSpatialSound voicePosition,pawPosition;
    CanvasGroup guide;RectTransform card,target;Text instruction;
    GameObject uiRoot,bloodRoot;Light muzzle;float flash,focusTime,stepDistance,walkPhase;
    bool restoreMovement,priorCursor;CursorLockMode priorLock;
    readonly List<Mesh> meshes=new();System.Random random=new System.Random(9821);
    float Delta=>PauseMenu.IsOpen?0:Time.unscaledDeltaTime;
    void Awake()
    {
        journey=GetComponent<CursedForestSequence>();story=GetComponent<OpeningSequence>();pack=GetComponent<PlayerInventory>();
        inventory=GetComponent<InventoryUI>();movement=GetComponent<PlayerMovement>();rifle=GetComponent<HeldRifle>();focus=GetComponent<ForestFocus>();
        voice=Source("Wolf - growl and wounded whine",.42f,out voicePosition);paws=Source("Wolf - paws in snow",.28f,out pawPosition);
        shot=Source("Rifle - forest report",.55f,out _);shot.spatialBlend=0;
        var lightObject=new GameObject("Rifle muzzle flash");lightObject.transform.SetParent(transform,false);muzzle=lightObject.AddComponent<Light>();
        muzzle.type=LightType.Point;muzzle.color=new Color(1,.73f,.36f);muzzle.range=3;muzzle.intensity=0;muzzle.enabled=false;
        bloodRoot=new GameObject("Wolf - scattered blood in snow");BuildUI();
        if(wolf)wolf.SetActive(false);
    }
    AudioSource Source(string label,float volume,out PlayerRelativeSpatialSound spatial)
    {
        var go=new GameObject(label);go.transform.SetParent(transform,false);var s=go.AddComponent<AudioSource>();s.playOnAwake=false;s.volume=volume;s.spatialBlend=1;
        s.rolloffMode=AudioRolloffMode.Linear;s.minDistance=5;s.maxDistance=30;s.dopplerLevel=0;GameAudioSettings.Route(s);
        spatial=go.AddComponent<PlayerRelativeSpatialSound>();spatial.player=transform;return s;
    }
    void Update()
    {
        if(!wolf||!wolfAnimator)return;
        if(PauseMenu.IsOpen){voice.Pause();paws.Pause();shot.Pause();guide.alpha=0;target.gameObject.SetActive(false);muzzle.enabled=false;return;}
        voice.UnPause();paws.UnPause();shot.UnPause();
        if(Stage==Phase.Complete&&wolf.activeSelf)
        {var v=Camera.main.WorldToViewportPoint(TargetPoint);if(v.z<0||v.x<-.1f||v.x>1.1f||v.y<-.1f||v.y>1.1f)wolf.SetActive(false);}
        if(Stage==Phase.Waiting && journey.ArrivedAtDawn && transform.position.z>=triggerZ && Mathf.Abs(transform.position.x-DawnForestRoute.Center(transform.position.z))<3.2f && !story.BlocksGameplay && !inventory.IsOpen && !story.IsSpeaking && Ready())StartCoroutine(Encounter());
        float show=Stage==Phase.Focus?1:0;guide.alpha=Mathf.MoveTowards(guide.alpha,show,Delta*3);
        card.anchoredPosition=new Vector2(-36-(1-guide.alpha)*22,-155);
        target.gameObject.SetActive(Stage==Phase.Focus);
        if(Stage==Phase.Focus)
        {
            focusTime+=Delta;var cam=Camera.main;var screen=cam.WorldToScreenPoint(TargetPoint);
            var canvas=uiRoot.GetComponent<Canvas>();target.anchoredPosition=(Vector2)screen/canvas.scaleFactor;
            float radius=Mathf.Max(28,Vector2.Distance(cam.WorldToScreenPoint(TargetPoint+cam.transform.right*.55f),screen));
            bool thirdPerson=cam.GetComponent<DawnThirdPersonCamera>()?.IsActive??false;
            Vector2 aimPoint=thirdPerson?new Vector2(Screen.width*.5f,Screen.height*.5f):(Mouse.current!=null?Mouse.current.position.ReadValue():Vector2.zero);
            AimOnTarget=Mouse.current!=null&&screen.z>0&&Vector2.Distance(aimPoint,screen)<radius;
            instruction.text=!rifle.IsAiming?"Hold RMB to raise the rifle.\nMove your aim onto the wolf.":AimOnTarget?"LMB  /  FIRE\nOne steady shot. Drive it back.":"Move your aim onto the wolf.\nLMB to fire when your sight is steady.";
            if(Mouse.current!=null&&Mouse.current.leftButton.wasPressedThisFrame)TryShoot();
        }
        flash=Mathf.MoveTowards(flash,0,Delta*12);muzzle.enabled=flash>0;muzzle.intensity=flash*2.6f;muzzle.transform.position=rifle.MuzzlePosition;
    }
    bool Ready()
    {
        bool weapon=false,ammo=false;foreach(var item in pack.slots){if(item is RifleItem)weapon=true;if(item is RifleAmmoItem a&&a.rounds>0)ammo=true;}return weapon&&ammo;
    }
    void EquipRifle()
    {
        for(int i=0;i<pack.slots.Length;i++)if(pack.slots[i] is RifleItem)
        {int key=System.Array.IndexOf(pack.quickbar,i);if(key<0){key=System.Array.FindIndex(pack.quickbar,x=>x<0);if(key<0)key=0;pack.Assign(i,key);}pack.EquippedItem?.Extinguish();pack.SelectQuickSlot(key);break;}
    }
    IEnumerator Encounter()
    {
        Stage=Phase.Listening;restoreMovement=movement.enabled;movement.enabled=false;
        priorCursor=Cursor.visible;priorLock=Cursor.lockState;Cursor.visible=true;Cursor.lockState=CursorLockMode.None;
        story.SilenceDialogue();
        Vector3 origin=Ground(transform.position+new Vector3(-2.1f,0,9.5f));
        wolf.transform.position=origin;wolf.transform.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.position-origin,Vector3.up));
        wolf.SetActive(true);wolfAnimator.speed=1;wolfAnimator.Play("Breathe",0,0);wolfAnimator.Update(0);
        voicePosition.SetLocation(origin);pawPosition.SetLocation(origin);
        for(int i=0;i<3;i++){Paw(.42f);yield return Wait(.5f+i*.09f);}
        voice.clip=growl;voice.pitch=.88f;voice.Play();
        yield return Wait(.8f);story.Say("YOU","What is that...? Stay back.");yield return Wait(.7f);
        EquipRifle();Stage=Phase.Charging;wolfAnimator.speed=0;walkPhase=0;stepDistance=0;
        while(Vector3.ProjectOnPlane(wolf.transform.position-transform.position,Vector3.up).magnitude>3.4f)
        {
            MoveToward(transform.position,5.8f,"Run",9.0f);yield return null;
        }
        Stage=Phase.Focus;focus.EncounterFocus=true;rifle.EncounterAim=true;focusTime=0;
        // Freeze the wolf exactly in its charge pose, while aiming remains responsive.
        wolfAnimator.speed=0;voice.Stop();paws.Stop();
        while(Stage==Phase.Focus)yield return null;
        focus.EncounterFocus=false;
        wolfAnimator.speed=1;wolfAnimator.CrossFadeInFixedTime("Damage",.07f);voice.pitch=.95f;voice.clip=whimper;voice.Play();
        Blood(wolf.transform.position,1.2f);yield return Wait(1.45f);
        rifle.EncounterAim=false;movement.enabled=restoreMovement;Cursor.visible=priorCursor;Cursor.lockState=priorLock;
        Stage=Phase.Retreating;walkPhase=0;stepDistance=0;wolfAnimator.speed=0;
        float nextBlood=.6f,distance=0;Vector3 previous=wolf.transform.position;
        float startZ=wolf.transform.position.z;
        for(float z=startZ+2;z<=Mathf.Min(startZ+64,315);z+=2)
        {
            Vector3 destination=Ground(new Vector3(DawnForestRoute.Center(z)+Mathf.Sin(z*.48f)*.6f,0,z));
            while(Vector3.ProjectOnPlane(wolf.transform.position-destination,Vector3.up).magnitude>.18f)
            {
                // Uneven, short strides after the hit; no death animation or looping impacts.
                bool fleeing=distance>3.5f;
                float limp=1.75f*(.80f+.20f*Mathf.Sin(walkPhase*Mathf.PI*8));
                MoveToward(destination,fleeing?7.2f:limp,fleeing?"Run":"Walk",fleeing?9f:4.6f);
                distance+=Vector3.Distance(previous,wolf.transform.position);previous=wolf.transform.position;
                if(distance>=nextBlood){Blood(wolf.transform.position,1);nextBlood=distance+Range(1.2f,3.4f);}
                voice.volume=.42f*Mathf.Clamp01(1-distance/32);yield return null;
            }
        }
        voice.Stop();paws.Stop();Stage=Phase.Complete;
        // Never visibly pop out if the player manages to keep up with the retreat.
        wolfAnimator.speed=1;wolfAnimator.CrossFadeInFixedTime("Breathe",.2f);
        var view=Camera.main.WorldToViewportPoint(TargetPoint);
        if(view.z<0||view.x<-.1f||view.x>1.1f||view.y<-.1f||view.y>1.1f)wolf.SetActive(false);
        story.Say("YOU","It's gone... Blood in the snow. I hit it.");
    }
    public bool TryShoot()
    {
        if(Stage!=Phase.Focus||PauseMenu.IsOpen||focusTime<.65f||!rifle.IsAiming||!AimOnTarget||rifle.AimBlend<.75f)return false;
        RifleAmmoItem ammo=null;foreach(var item in pack.slots)if(item is RifleAmmoItem a&&a.rounds>0){ammo=a;break;}
        if(!ammo)return false;
        ammo.rounds--;rifle.Recoil();flash=1;shot.PlayOneShot(rifleShot);Stage=Phase.Wounded;return true;
    }
    void MoveToward(Vector3 destination,float speed,string animation,float metresPerClip)
    {
        if(Delta<=0)return;
        Vector3 old=wolf.transform.position;Vector3 flat=Vector3.ProjectOnPlane(destination-old,Vector3.up);
        float travel=Mathf.Min(flat.magnitude,speed*Delta);Vector3 p=Ground(old+flat.normalized*travel);
        if(flat.sqrMagnitude>.01f)wolf.transform.rotation=Quaternion.RotateTowards(wolf.transform.rotation,Quaternion.LookRotation(flat),Delta*220);
        wolf.transform.position=p;walkPhase+=travel/metresPerClip;wolfAnimator.Play(animation,0,walkPhase);wolfAnimator.Update(0);
        voicePosition.SetLocation(p);pawPosition.SetLocation(p);stepDistance+=travel;
        if(stepDistance>(animation=="Run"?.75f:.48f)){stepDistance=0;Paw(animation=="Run"?.75f:.45f);}
    }
    void Paw(float level){if(snowSteps==null||snowSteps.Length==0)return;paws.pitch=Range(1.12f,1.38f);paws.PlayOneShot(snowSteps[random.Next(snowSteps.Length)],level);}
    IEnumerator Wait(float seconds){for(float t=0;t<seconds;t+=Delta)yield return null;}
    static Vector3 Ground(Vector3 p){p.y=CursedForestSequence.Ground(p)+.025f;return p;}
    float Range(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
    void Blood(Vector3 position,float amount)
    {
        if(!bloodMaterial)return;
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var colors=new List<Color>();var triangles=new List<int>();
        int drops=random.Next(3,7);
        for(int j=0;j<drops;j++)
        {
            Vector3 center=position+new Vector3(Range(-.32f,.32f),0,Range(-.36f,.36f));
            float radius=Range(.014f,.064f)*amount*(j==0?1.5f:1);float stretch=Range(.8f,1.65f);float angle=Range(0,Mathf.PI*2);
            Color stain=Color.Lerp(new Color(.11f,.007f,.014f,.90f),new Color(.23f,.023f,.031f,.95f),Range(0,1));
            int start=vertices.Count;
            for(int y=0;y<=3;y++)for(int x=0;x<=3;x++)
            {
                float u=x/3f-.5f,v=y/3f-.5f;
                Vector3 p=center+new Vector3((u*Mathf.Cos(angle)-v*Mathf.Sin(angle))*radius*2,0,(u*Mathf.Sin(angle)+v*Mathf.Cos(angle))*radius*2*stretch);
                p.y=CursedForestSequence.Ground(p)+.016f;vertices.Add(p);uv.Add(new Vector2(x/3f,y/3f));colors.Add(stain);
                if(x<3&&y<3){int a=start+y*4+x;triangles.AddRange(new[]{a,a+4,a+5,a,a+5,a+1});}
            }
        }
        var mesh=new Mesh{name="Terrain-conforming scattered blood"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();meshes.Add(mesh);
        var go=new GameObject("Soaked blood droplets "+(++BloodPatchCount),typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(bloodRoot.transform,false);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;var r=go.GetComponent<MeshRenderer>();r.sharedMaterial=bloodMaterial;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
    }
    void BuildUI()
    {
        uiRoot=new GameObject("Wolf encounter - steady shot",typeof(Canvas),typeof(CanvasScaler));var canvas=uiRoot.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=670;
        var scale=uiRoot.GetComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1440,900);scale.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        var panel=SurvivalUITheme.Surface(uiRoot.transform,"Instinct",0,0,342,182);card=panel.rectTransform;card.anchorMin=card.anchorMax=card.pivot=Vector2.one;
        guide=panel.gameObject.AddComponent<CanvasGroup>();guide.alpha=0;guide.blocksRaycasts=false;
        instruction=SurvivalGuideCard.Build(card,"SURVIVAL","Hold your nerve","RMB","AIM  /  LMB TO FIRE","Hold RMB to raise the rifle.\nMove your aim onto the wolf.");
        SurvivalUITheme.Text(card,"FOCUS ENGAGED",18,159,300,16,10,false,SurvivalUITheme.Ice);
        var targetGO=new GameObject("Wolf focus brackets",typeof(RectTransform));targetGO.transform.SetParent(uiRoot.transform,false);target=targetGO.GetComponent<RectTransform>();target.anchorMin=target.anchorMax=Vector2.zero;
        for(int i=0;i<4;i++)
        {
            var a=new GameObject("Focus corner",typeof(RectTransform),typeof(Image));a.transform.SetParent(target,false);var rect=a.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(8,2);rect.anchoredPosition=new Vector2(i<2?-29:29,i%2==0?-23:23);a.GetComponent<Image>().color=new Color(.66f,.83f,.9f,.65f);a.GetComponent<Image>().raycastTarget=false;
        }
        target.gameObject.SetActive(false);
    }
    void OnDisable()
    {
        if(focus)focus.EncounterFocus=false;if(rifle)rifle.EncounterAim=false;
        if(IsBusy&&movement&&!PauseMenu.IsOpen)movement.enabled=restoreMovement;
        if(voice)voice.Stop();if(paws)paws.Stop();if(shot)shot.Stop();if(wolf)wolf.SetActive(false);
        if(uiRoot)uiRoot.SetActive(false);
    }
    void OnDestroy(){if(uiRoot)Destroy(uiRoot);if(bloodRoot)Destroy(bloodRoot);foreach(var mesh in meshes)if(mesh)Destroy(mesh);}
}
