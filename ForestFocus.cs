using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DefaultExecutionOrder(100)]
public sealed class ForestFocus : MonoBehaviour
{
    public bool Unlocked {get;private set;}
    public bool Discovered {get;private set;}
    public float Strength {get;private set;}
    public bool IsFocusing => Strength>.5f;
    public bool EncounterFocus {get;set;}
    OpeningSequence story;InventoryUI inventory;GameplayInteraction interaction;
    GameObject uiRoot,trackRoot,volumeRoot;CanvasGroup tutorial,indicator;RectTransform tutorialRect;
    Volume volume;VolumeProfile profile;Material trackMaterial;Texture2D sole,focusSole;
    AudioSource noise;AudioClip noiseClip;float visibleTime,tutorialTime;bool hasFocused,ready,discoveryPending;
    readonly List<RawImage> highlights=new();
    Material highlightMaterial;
    readonly List<Transform> tracks=new();
    // World-space glow per print (FocusHighlight layer, drawn after post-processing
    // with depth testing, so the character and anything else in front hides it).
    readonly List<MeshRenderer> glows=new();
    Material glowMaterial;MaterialPropertyBlock glowBlock;
    float originalFixed;
    void Awake()
    {
        story=GetComponent<OpeningSequence>();inventory=GetComponent<InventoryUI>();interaction=GetComponent<GameplayInteraction>();originalFixed=Time.fixedDeltaTime;
        BuildUI();BuildLook();BuildTracks();BuildAudio();
    }
    public void Unlock()
    {
        if(Unlocked)return;Unlocked=true;tutorialTime=0;
        story.Say("YOU","The relay. Of course you went back. There must be tracks outside.");

    }
    public void RestoreDiscoveredProgress()
    {
        Unlocked=Discovered=ready=hasFocused=true;discoveryPending=false;tutorialTime=20;
    }
    void Update()
    {
        if(Unlocked&&!ready&&!story.IsSpeaking){ready=true;story.SetFocusObjective(false);}
        if(discoveryPending&&!story.IsSpeaking){discoveryPending=false;if(!(GetComponent<ShedNightSequence>()?.HasReturned ?? false))story.SetFocusObjective(true);}
        bool gameplay=!PauseMenu.IsOpen&&!inventory.IsOpen&&(EncounterFocus||(!story.BlocksGameplay&&!interaction.IsBusy));
        bool held=gameplay&&(EncounterFocus||(ready&&Keyboard.current!=null&&Keyboard.current.fKey.isPressed));
        if(held)hasFocused=true;
        Strength=Mathf.MoveTowards(Strength,held?1:0,Time.unscaledDeltaTime/(held?.65f:.4f));
        if(!PauseMenu.IsOpen){Time.timeScale=gameplay?Mathf.Lerp(1,EncounterFocus?.025f:.24f,Strength):1;Time.fixedDeltaTime=originalFixed*Time.timeScale;}
        volume.weight=gameplay?Strength:0;
        GameAudioSettings.Instance?.SetFocusDuck(gameplay?Strength:0);
        noise.volume=gameplay?Strength*.028f*(GameAudioSettings.Instance?GameAudioSettings.Instance.Effects:1):0;
        if(ready&&gameplay)tutorialTime+=Time.unscaledDeltaTime;
        float show=ready&&gameplay&&!Discovered&&(!hasFocused||tutorialTime<12)?1:0;
        tutorial.alpha=Mathf.MoveTowards(tutorial.alpha,show,Time.unscaledDeltaTime*2.5f);
        tutorialRect.anchoredPosition=new Vector2(-38-(1-tutorial.alpha)*18,-210);
        indicator.alpha=gameplay?Strength:0;
        trackMaterial.color=new Color(.27f,.31f,.38f,.13f);
        UpdateHighlights(gameplay&&!EncounterFocus);
        if(!Unlocked||Discovered||!gameplay||Strength<.7f){visibleTime=0;return;}
        bool seen=false;var cam=Camera.main;
        foreach(var track in tracks)
        {
            if(Vector3.Distance(transform.position,track.position)>9)continue;
            Vector3 viewport=cam.WorldToViewportPoint(track.position);
            if(viewport.z<=0||viewport.x<.08f||viewport.x>.92f||viewport.y<.1f||viewport.y>.9f)continue;
            Vector3 ray=track.position+Vector3.up*.08f-cam.transform.position;
            bool blocked=false;
            foreach(var hit in Physics.RaycastAll(cam.transform.position,ray.normalized,ray.magnitude-.08f,~0,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(transform)&&!(hit.collider is TerrainCollider)){blocked=true;break;}
            if(!blocked){seen=true;break;}
        }
        visibleTime=seen?visibleTime+Time.unscaledDeltaTime:0;
        if(visibleTime>1.1f)
        {
            Discovered=true;story.Say("YOU","Bootprints. Someone went beyond the outpost. Please let them be yours.");discoveryPending=true;GetComponent<ShedNightSequence>()?.BeginReturnToShed();
        }
    }
    // The focus-mode glow of one boot print, as a signed distance to a real boot
    // sole: a broad forefoot with a rounded toe, a narrow waist under the arch, and
    // a separate heel block. A bright rim traces the outline (how a pressed print
    // catches light at its walls), the floor carries faint lug chevrons, and a soft
    // halo fades outward. u is across the print, v along it (toe at +1).
    static Vector2[] s_SoleOutline;
    static float BootGlow(float u,float v)
    {
        // Boot sole as a width profile from heel (t=0) to toe (t=1): a rounded heel
        // block, a waist that narrows only gently, a broad ball, and a rounded toe.
        float Blend(float a,float b,float x0,float x1,float x)=>Mathf.Lerp(a,b,Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(x0,x1,x)));
        float Step(float x0,float x1,float x)=>Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(x0,x1,x));   // GLSL smoothstep; Mathf.SmoothStep interpolates
        float Offset(float tt)=>-.07f*Mathf.Exp(-Mathf.Pow((tt-.45f)/.16f,2f))+.04f*Mathf.Exp(-Mathf.Pow((tt-.72f)/.2f,2f));
        float Width(float tt)
        {
            float w=tt<.45f?Blend(.56f,.45f,.28f,.45f,tt):Blend(.45f,.72f,.45f,.7f,tt);
            float eh=Mathf.Clamp01(tt/.16f),et=Mathf.Clamp01((1f-tt)/.26f);
            return w*Mathf.Sqrt(1f-(1f-eh)*(1f-eh))*Mathf.Sqrt(1f-(1f-et)*(1f-et));   // circular heel and toe caps
        }
        // Exact signed distance to the sampled outline: one rim width all the way
        // round, and no glints where the width curve turns vertical at the tips.
        if(s_SoleOutline==null)
        {
            const int n=160;var outline=new Vector2[n*2];
            for(int i=0;i<n;i++)
            {
                float tt=i/(n-1f),y=-.88f+1.76f*tt,w=Width(tt),o=Offset(tt);
                outline[i]=new Vector2(o-w,y);outline[n*2-1-i]=new Vector2(o+w,y);
            }
            s_SoleOutline=outline;
        }
        var q=new Vector2(u,v);float best=float.MaxValue;
        for(int i=0;i<s_SoleOutline.Length;i++)
        {
            Vector2 p0=s_SoleOutline[i],seg=s_SoleOutline[(i+1)%s_SoleOutline.Length]-p0;
            float h=Mathf.Clamp01(Vector2.Dot(q-p0,seg)/Mathf.Max(1e-8f,seg.sqrMagnitude));
            best=Mathf.Min(best,(q-p0-seg*h).sqrMagnitude);
        }
        float t=(v+.88f)/1.76f,offset=Offset(t),hw=Mathf.Max(.01f,Width(Mathf.Clamp01(t)));
        float d=(t>=0f&&t<=1f&&Mathf.Abs(u-offset)<Width(t)?-1f:1f)*Mathf.Sqrt(best);
        // Heel breast: the step between the heel block and the waist.
        float breast=Mathf.Exp(-Mathf.Pow((t-.3f)/.018f,2f))*(Mathf.Abs(u-offset)<hw*.85f?1f:0f);
        float inside=d<0?1f:0f;
        float rim=Mathf.Exp(-Mathf.Pow(d/.055f,2f));
        float halo=d>0?Mathf.Exp(-d/.07f)*.28f:0f;
        // Lug chevrons on the tread, stronger under the ball and heel.
        float lug=Step(.35f,.65f,Mathf.Abs(Mathf.Sin((v*9f-Mathf.Abs(u)*2.2f)*Mathf.PI)));
        float floor=inside*(.26f+.16f*lug)*(1f-.7f*breast);
        // Fade out at the texture border so nothing glints past the heel or toe.
        float border=(1f-Step(.9f,1f,Mathf.Abs(v)))*(1f-Step(.9f,1f,Mathf.Abs(u)));
        return Mathf.Clamp01(Mathf.Max(rim,floor)+halo)*border;
    }
    void AddGlow(GameObject print,Mesh mesh)
    {
        int layer=LayerMask.NameToLayer("FocusHighlight");
        if(layer<0)return;
        if(!glowMaterial)
        {
            var source=Resources.Load<Material>("FocusTrackGlow");if(!source)return;
            glowMaterial=new Material(source);glowMaterial.mainTexture=focusSole;glowBlock=new MaterialPropertyBlock();
        }
        var go=new GameObject("Focus glow",typeof(MeshFilter),typeof(MeshRenderer));go.layer=layer;
        go.transform.SetParent(print.transform,false);go.transform.localScale=new Vector3(1.08f,1f,1.06f);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;
        var r=go.GetComponent<MeshRenderer>();r.sharedMaterial=glowMaterial;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.enabled=false;
        glows.Add(r);
    }
    // Only a short stretch lights up: a couple of prints behind, about five pairs
    // ahead up the trail, fading at the far end - enough to follow, not a runway.
    void UpdateGlows(bool gameplay)
    {
        if(glows.Count==0)return;
        int nearest=0;float best=float.MaxValue;
        for(int i=0;i<tracks.Count;i++){float d=(tracks[i].position-transform.position).sqrMagnitude;if(d<best){best=d;nearest=i;}}
        for(int i=0;i<glows.Count;i++)
        {
            float a=0;int rel=i-nearest;
            // Reveal by distance: the patch you are standing near lights up; the next
            // one, 20-30m on, has to be found by walking on and focusing again.
            float dist=Vector3.Distance(tracks[i].position,transform.position);
            if(gameplay&&Strength>.01f&&dist<12f)
                a=1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(8f,12f,dist));
            bool on=a>.001f;
            if(glows[i].enabled!=on)glows[i].enabled=on;
            if(!on)continue;
            glowBlock.SetColor("_Color",new Color(.45f,.85f,1f,a*Strength));
            glows[i].SetPropertyBlock(glowBlock);
        }
    }
    void UpdateHighlights(bool gameplay)
    {
        if(glows.Count>0){UpdateGlows(gameplay);return;}
        var cam=Camera.main;if(!cam)return;var canvas=uiRoot.GetComponent<RectTransform>();float scale=uiRoot.GetComponent<Canvas>().scaleFactor;
        for(int i=0;i<tracks.Count;i++)
        {
            var t=tracks[i];var image=highlights[i];Vector3 center=cam.WorldToScreenPoint(t.position);
            bool visible=gameplay&&Strength>.01f&&center.z>0&&center.x>0&&center.x<Screen.width&&center.y>0&&center.y<Screen.height&&(t.position-transform.position).sqrMagnitude<22*22;
            if(visible)
            {
                Vector3 ray=t.position+Vector3.up*.04f-cam.transform.position;
                foreach(var hit in Physics.RaycastAll(cam.transform.position,ray.normalized,ray.magnitude-.06f,~0,QueryTriggerInteraction.Ignore))
                    if(!hit.transform.IsChildOf(transform)&&!(hit.collider is TerrainCollider)){visible=false;break;}
            }
            image.color=new Color(.48f,.79f,1f,visible?Strength*.88f:0);
            if(!visible)continue;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas,center,null,out var point);
            var rect=image.rectTransform;rect.anchoredPosition=point;
            var toe=cam.WorldToScreenPoint(t.position+t.forward*.19f);var heel=cam.WorldToScreenPoint(t.position-t.forward*.19f);
            var left=cam.WorldToScreenPoint(t.position-t.right*.095f);var right=cam.WorldToScreenPoint(t.position+t.right*.095f);
            rect.sizeDelta=new Vector2(Vector2.Distance(left,right),Vector2.Distance(toe,heel))/scale;
            rect.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(toe.y-heel.y,toe.x-heel.x)*Mathf.Rad2Deg-90);
        }
    }
    void BuildLook()
    {
        volumeRoot=new GameObject("Focus color and lens");volume=volumeRoot.AddComponent<Volume>();volume.isGlobal=true;volume.priority=100;volume.weight=0;
        profile=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=profile;
        var color=profile.Add<ColorAdjustments>(true);color.saturation.Override(-88);color.contrast.Override(8);color.postExposure.Override(.12f);color.colorFilter.Override(new Color(.88f,.94f,1));
        var edge=profile.Add<Vignette>(true);edge.intensity.Override(.34f);edge.smoothness.Override(.65f);edge.color.Override(new Color(.035f,.055f,.08f));
    }
    void BuildAudio()
    {
        var go=new GameObject("Focus hush");go.transform.SetParent(transform,false);noise=go.AddComponent<AudioSource>();noise.playOnAwake=false;noise.loop=true;noise.spatialBlend=0;noise.volume=0;
        var mixer=Resources.Load<UnityEngine.Audio.AudioMixer>("OpeningAudio");noise.outputAudioMixerGroup=mixer.FindMatchingGroups("Master")[0];
        const int n=88200;var samples=new float[n];var rng=new System.Random(561);float smooth=0;
        for(int i=0;i<n;i++){smooth=Mathf.Lerp(smooth,(float)rng.NextDouble()*2-1,.13f);samples[i]=smooth*.5f;}
        // Crossfade the loop boundary to avoid a repeating click.
        for(int i=0;i<512;i++){float t=i/511f;float a=samples[i],b=samples[n-512+i];samples[n-512+i]=Mathf.Lerp(b,a,t);}
        for(int i=0;i<128;i++){float fade=i/127f;samples[i]*=fade;samples[n-1-i]*=fade;}
        noiseClip=AudioClip.Create("Filtered focus noise",n,1,44100,false);noiseClip.SetData(samples,0);noise.clip=noiseClip;noise.Play();
    }
    void BuildTracks()
    {
        trackRoot=new GameObject("Buried ranger tracks");sole=new Texture2D(128,256,TextureFormat.RGBA32,false);sole.wrapMode=TextureWrapMode.Clamp;
        focusSole=new Texture2D(128,256,TextureFormat.RGBA32,false);focusSole.wrapMode=TextureWrapMode.Clamp;focusSole.filterMode=FilterMode.Bilinear;
        for(int y=0;y<256;y++)for(int x=0;x<128;x++)
        {
            float u=(x/127f-.5f)*2,v=(y/255f-.5f)*2;
            float toe=Mathf.Sqrt(Mathf.Pow((u+.055f)/.77f,2)+Mathf.Pow((v-.32f)/.59f,2));
            float heel=Mathf.Pow(Mathf.Pow(Mathf.Abs(u/.59f),4)+Mathf.Pow(Mathf.Abs((v+.65f)/.25f),4),.25f);
            float arch=Mathf.Sqrt(Mathf.Pow((u-.09f)/.49f,2)+Mathf.Pow((v+.19f)/.42f,2));
            float distance=Mathf.Min(toe,Mathf.Min(heel,arch));
            float noise=Mathf.PerlinNoise(x*.14f+18,y*.13f);
            float mask=Mathf.Clamp01((1-distance+(noise-.5f)*.08f)*15);
            float row=Mathf.Repeat(v*8+Mathf.Abs(u)*.75f,1);
            float groove=Mathf.SmoothStep(.10f,.21f,row)*(1-Mathf.SmoothStep(.69f,.83f,row));
            float tread=Mathf.Lerp(.10f,.92f,groove);
            float heelGap=1-.80f*Mathf.Exp(-Mathf.Pow((v+.38f)/.045f,2));tread*=heelGap;
            float snow=Mathf.Lerp(.25f,1,Mathf.SmoothStep(.3f,.72f,Mathf.PerlinNoise(x*.035f+6,y*.028f)));
            float edge=Mathf.Exp(-Mathf.Pow((distance-.93f)/.065f,2));
            sole.SetPixel(x,y,new Color(.8f,.88f,1,mask*tread*snow));
            focusSole.SetPixel(x,y,new Color(1,1,1,BootGlow(u,v)));
        }
        sole.Apply();focusSole.Apply();
        trackMaterial=new Material(Resources.Load<Material>("FocusTracks"));trackMaterial.mainTexture=sole;trackMaterial.color=new Color(.27f,.31f,.38f,.13f);
        highlightMaterial=new Material(Resources.Load<Material>("FocusTracks"));highlightMaterial.mainTexture=focusSole;highlightMaterial.color=Color.clear;
        var eastern=EasternForestRoute.Footsteps();eastern.AddRange(DawnForestRoute.Footsteps());
        // The ranger walked from the shed to the forest house; the tracks end where the
        // eastern route begins.
        // Real walking, not a dotted line: a ~0.76m step, feet ~30cm apart with some
        // sway, toes turned out, the line wandering a little. Night snow has since
        // drifted over whole stretches and filled single prints, so the trail comes and
        // goes - which is what makes it something to search for.
        var steps=new List<(Vector3 p,float yaw,bool left)>();
        void Walk(List<Vector3> line,float seed,float clearStart=0)
        {
            float travelled=0,next=.3f;bool left=false;int n=0;
            // Patches: ~2m of prints where the ground held them, then 20-30m that the
            // wind has filled. The first patch starts at the shed door (clearStart).
            float patchStart=0,patchEnd=Mathf.Max(2.2f,clearStart);int patch=0;
            for(int k=1;k<line.Count;k++)
            {
                var a=line[k-1];var b=line[k];a.y=b.y=0;float len=Vector3.Distance(a,b);if(len<1e-4f)continue;var dir=(b-a)/len;
                while(next<=travelled+len)
                {
                    var c=a+dir*(next-travelled);var side=Vector3.Cross(Vector3.up,dir);
                    float wander=Mathf.PerlinNoise(next*.35f+seed,1.7f)-.5f;
                    var p=c+side*((left?-.15f:.15f)+wander*.14f);
                    float yaw=Mathf.Atan2(dir.x,dir.z)*Mathf.Rad2Deg+(left?-7f:7f)+Mathf.Sin(n*12.9898f)*5f;
                    while(next>patchEnd){patch++;float gap=Mathf.Lerp(20f,30f,Mathf.Repeat(Mathf.Sin(patch*12.9898f+seed)*43758.55f,1f));patchStart=patchEnd+gap;patchEnd=patchStart+Mathf.Lerp(1.8f,2.6f,Mathf.Repeat(Mathf.Sin(patch*78.233f+seed)*43758.55f,1f));}
                    if(next>=patchStart)steps.Add((p,yaw,left));
                    left=!left;n++;next+=.76f+.1f*(Mathf.PerlinNoise(next*.5f,seed)-.5f);
                }
                travelled+=len;
            }
        }
        var approach=new List<Vector3>();
        for(float z=10.7f;z<=EasternForestRoute.Start.z;z+=.25f){float t=Mathf.Clamp01((z-10.7f)/8);float x=z<=18.7f?5.2f+3.5f*t+Mathf.Sin(t*3)*.5f:8.6f+7*Mathf.Sin((z-19)*.043f)-3*Mathf.Sin((z-19)*.09f);approach.Add(new Vector3(x,0,z));}
        Walk(approach,3.1f,3f);   // first patch right outside the shed: the story waits on it being found
        Walk(new List<Vector3>(EasternForestRoute.Samples),11.7f);
        var dawn=new List<Vector3>();for(float z=209;z<318;z+=.5f)dawn.Add(new Vector3(DawnForestRoute.Center(z),0,z));
        Walk(dawn,27.3f);
        _=eastern;
        for(int i=0;i<steps.Count;i++)
        {
            var (p,trackYaw,left)=steps[i];
            Terrain terrain=null;
            foreach(var tr in Terrain.activeTerrains){var q=p-tr.transform.position;var sz=tr.terrainData.size;if(q.x>=0&&q.z>=0&&q.x<=sz.x&&q.z<=sz.z){terrain=tr;break;}}
            if(!terrain)continue;
            p.y=terrain.SampleHeight(p)+terrain.transform.position.y+.012f;
            var go=new GameObject("Buried bootprint "+(i+1),typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(trackRoot.transform,false);go.transform.position=p;
            var normal=terrain.terrainData.GetInterpolatedNormal((p.x-terrain.transform.position.x)/terrain.terrainData.size.x,(p.z-terrain.transform.position.z)/terrain.terrainData.size.z);
            go.transform.rotation=Quaternion.FromToRotation(Vector3.up,normal)*Quaternion.Euler(0,trackYaw,0);
            var mesh=new Mesh();var verts=new Vector3[45];var uv=new Vector2[45];var triangles=new List<int>();
            for(int yy=0;yy<=8;yy++)for(int xx=0;xx<=4;xx++)
            {
                int index=yy*5+xx;Vector3 local=new Vector3((xx/4f-.5f)*.16f,0,(yy/8f-.5f)*.34f);
                Vector3 world=go.transform.TransformPoint(local);world.y=terrain.SampleHeight(world)+terrain.transform.position.y+.012f;
                verts[index]=go.transform.InverseTransformPoint(world);uv[index]=new Vector2(left?xx/4f:1-xx/4f,yy/8f);
                if(xx<4&&yy<8){triangles.AddRange(new[]{index,index+5,index+6,index,index+6,index+1});}
            }
            mesh.vertices=verts;mesh.uv=uv;mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();go.GetComponent<MeshFilter>().mesh=mesh;
            var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=trackMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;tracks.Add(go.transform);
            AddGlow(go,mesh);
            var highlight=new GameObject("Focus blue bootprint "+i,typeof(RectTransform),typeof(RawImage));highlight.transform.SetParent(uiRoot.transform,false);highlight.transform.SetAsFirstSibling();
            var image=highlight.GetComponent<RawImage>();image.texture=focusSole;image.raycastTarget=false;image.color=Color.clear;
            image.rectTransform.anchorMin=image.rectTransform.anchorMax=image.rectTransform.pivot=Vector2.one*.5f;
            if(!left)image.uvRect=new Rect(1,0,-1,1);highlights.Add(image);

        }
    }
    void BuildUI()
    {
        uiRoot=new GameObject("Focus interface",typeof(Canvas),typeof(CanvasScaler));var canvas=uiRoot.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=650;
        var scaler=uiRoot.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1440,900);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        var go=new GameObject("Focus tutorial",typeof(RectTransform),typeof(CanvasGroup));go.transform.SetParent(uiRoot.transform,false);tutorial=go.GetComponent<CanvasGroup>();tutorial.alpha=0;tutorial.blocksRaycasts=false;tutorialRect=go.GetComponent<RectTransform>();tutorialRect.anchorMin=tutorialRect.anchorMax=new Vector2(1,1);tutorialRect.pivot=new Vector2(1,1);tutorialRect.sizeDelta=new Vector2(310,190);
        tutorialRect.sizeDelta=new Vector2(342,166);
        var panel=SurvivalUITheme.Surface(go.transform,"Listening to the forest",0,0,342,166);
        SurvivalGuideCard.Build(panel.transform,"PERCEPTION", "Look closer", "F", "HOLD TO FOCUS", "Quiet the world and search the snow.\nHidden tracks glow pale blue in focus.");
        var active=new GameObject("Focus active",typeof(RectTransform),typeof(CanvasGroup));active.transform.SetParent(uiRoot.transform,false);var rect=active.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-34);rect.sizeDelta=new Vector2(240,36);indicator=active.GetComponent<CanvasGroup>();indicator.alpha=0;indicator.blocksRaycasts=false;
        SurvivalUITheme.Text(rect,"F O C U S",0,0,240,30,16,true,SurvivalUITheme.Ice,TextAnchor.MiddleCenter);
    }
    void OnDisable(){if(!PauseMenu.IsOpen)Time.timeScale=1;Time.fixedDeltaTime=originalFixed;GameAudioSettings.Instance?.SetFocusDuck(0);if(noise)noise.Stop();}
    void OnDestroy(){if(uiRoot)Destroy(uiRoot);if(trackRoot){foreach(var t in tracks)if(t)Destroy(t.GetComponent<MeshFilter>().sharedMesh);Destroy(trackRoot);}if(volumeRoot)Destroy(volumeRoot);if(profile)Destroy(profile);if(trackMaterial)Destroy(trackMaterial);if(highlightMaterial)Destroy(highlightMaterial);if(sole)Destroy(sole);if(focusSole)Destroy(focusSole);if(noiseClip)Destroy(noiseClip);if(glowMaterial)Destroy(glowMaterial);}
}
