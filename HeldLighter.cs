using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System.Collections.Generic;

// An additive right-arm pose keeps the existing walk, run and cold gait intact.
[DefaultExecutionOrder(180), DisallowMultipleComponent]
public sealed class HeldLighter : MonoBehaviour
{
    public Material cleanMaterial;
    Material heldMaterial;
    public bool Introduced { get; private set; }
    public bool IsLit => item && item.IsLit;
    PlayerInventory pack; InventoryUI inventory; OpeningSequence story;
    Transform shoulder, elbow, hand, chest, prop;
    Vector3 gripLocal;
    [Header("Live lighter grip tuning")]
    [Tooltip("Adjust this in Play Mode while the Player is selected. X follows the hand forward axis, Y follows its right axis, and Z moves vertically in world space.")]
    [SerializeField] Vector3 lighterGripPosition=new Vector3(-.045f,.016f,.005f);
    [Tooltip("World-upright rotation correction. Adjust this in Play Mode and watch the held lighter update immediately.")]
    [SerializeField] Vector3 lighterGripRotation=new Vector3(0,0,-7);
    SkinnedMeshRenderer gloveRenderer;Mesh originalGlove,grippingGlove;
    Vector3[] relaxedVertices,curledVertices,poseVertices;
    float lastGripBlend=-1;
    LighterItem item; Light flameLight, reflectedLight; Transform flame;
    Material flameMaterial; readonly List<Mesh> ownedMeshes=new List<Mesh>();
    readonly Color fireColor=new Color(1f,.72f,.30f);
    Canvas canvas; CanvasGroup tutorial; Text detail;
    float blend,showTime,feedbackUntil; bool toggled;
    bool Gameplay => !PauseMenu.IsOpen && !inventory.IsOpen && !story.BlocksGameplay && !GetComponent<GameplayInteraction>().IsBusy;
    void Awake()
    {
        pack=GetComponent<PlayerInventory>();inventory=GetComponent<InventoryUI>();story=GetComponent<OpeningSequence>();
        foreach(var t in GetComponentsInChildren<Transform>())
        {if(t.name=="UpperArm.R")shoulder=t;if(t.name=="Forearm.R")elbow=t;if(t.name=="Hand.R")hand=t;if(t.name=="Chest")chest=t;}
        PrepareGrip();
        BuildTutorial();
    }
    public void Introduce(){Introduced=true;showTime=0;toggled=false;}
    public void RestoreIntroducedProgress(){Introduced=true;showTime=20;toggled=true;}
    public bool TryToggle()
    {
        if(!Gameplay)return false;
        var night=GetComponent<ShedNightSequence>();
        if(!Introduced || !night || !night.IsNight)return false;
        var lighter=pack.EquippedItem as LighterItem;
        if(!lighter){Feedback("Select your lighter in the quickbar first.");return false;}
        if(!lighter.CanUse&&!lighter.IsLit){Feedback("Your lighter is out of fuel or worn out.");return false;}
        pack.Use(pack.EquippedSlot);toggled=true;showTime=Mathf.Max(showTime,9);return true;
    }
    void Feedback(string text){detail.text=text;feedbackUntil=Time.unscaledTime+3;}
    void Update()
    {
        var selected=pack.EquippedItem as LighterItem;
        if(selected!=item){if(item)item.Extinguish();item=selected;RebuildProp();}
        if(Gameplay && Keyboard.current!=null && Keyboard.current.rKey.wasPressedThisFrame)TryToggle();
        bool ready=Introduced&&!story.IsSpeaking;
        if(ready&&Gameplay)showTime+=Time.unscaledDeltaTime;
        bool feedback=Time.unscaledTime<feedbackUntil;
        bool show=Gameplay && (feedback || ready && (!toggled||showTime<12));
        tutorial.alpha=Mathf.MoveTowards(tutorial.alpha,show?1:0,Time.unscaledDeltaTime*3);
        if(!feedback)detail.text="Equip the lighter from your quickbar.\nToggle its flame with R. Fuel burns while lit.";
    }
    void LateUpdate()
    {
        if(!shoulder||!elbow||!hand||!chest)return;
        if(!PauseMenu.IsOpen)blend=Mathf.MoveTowards(blend,item?1:0,Time.deltaTime*4);
        if(blend>0)
        {
            // The target follows the animated chest, retaining its breathing and gait sway.
            Vector3 wrist=chest.position+transform.right*.23f+transform.forward*.30f-Vector3.up*.19f;
            float a=Vector3.Distance(shoulder.position,elbow.position),b=Vector3.Distance(elbow.position,hand.position);
            Vector3 reach=wrist-shoulder.position;float d=Mathf.Clamp(reach.magnitude,.02f,a+b-.005f);Vector3 dir=reach.normalized;
            float along=(a*a-b*b+d*d)/(2*d),height=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            Vector3 pole=Vector3.ProjectOnPlane(transform.right*.7f-Vector3.up,dir).normalized;
            Vector3 bend=shoulder.position+dir*along+pole*height;
            Quaternion upper=Quaternion.FromToRotation(elbow.position-shoulder.position,bend-shoulder.position)*shoulder.rotation;
            shoulder.rotation=Quaternion.Slerp(shoulder.rotation,upper,blend);
            Quaternion lower=Quaternion.FromToRotation(hand.position-elbow.position,wrist-elbow.position)*elbow.rotation;
            elbow.rotation=Quaternion.Slerp(elbow.rotation,lower,blend);
            // Lift the wrist with the forearm; retain the rig's native twist.
            Vector3 fingers=hand.position-elbow.position;
            Quaternion wristRotation=Quaternion.FromToRotation(fingers,transform.forward*.8f-Vector3.up*.2f)*hand.rotation;
            hand.rotation=Quaternion.Slerp(hand.rotation,wristRotation,blend);
        }
        // Rebuild the curled glove only while the grip is changing: once the blend has
        // settled the pose is identical, and re-deforming plus RecalculateNormals on the
        // whole glove every frame cost ~1.7 ms of main thread.
        if(grippingGlove && Mathf.Abs(blend-lastGripBlend)>.0005f)
        {
            for(int i=0;i<poseVertices.Length;i++)poseVertices[i]=Vector3.Lerp(relaxedVertices[i],curledVertices[i],blend);
            grippingGlove.vertices=poseVertices;grippingGlove.RecalculateNormals();lastGripBlend=blend;
        }
        if(prop)
        {
            // -hand.forward runs from this rig's wrist into the curled palm. Keeping
            // the offset in axis directions puts the lighter between the fingers
            // while its shell remains upright in world space.
            Vector3 grip=GripWorldPosition;
            Quaternion upright=GripWorldRotation;
            prop.SetPositionAndRotation(grip,upright);
            prop.gameObject.SetActive(item!=null);
        }
        bool lit=item&&item.IsLit;
        if(flame) {flame.gameObject.SetActive(lit);flame.localScale=new Vector3(.014f,.032f,.014f)*(1+Mathf.PerlinNoise(Time.time*9,2)*.13f);}
        if(flameLight)
        {
            float flicker=.55f*Mathf.PerlinNoise(Time.time*7.3f,7)+.3f*Mathf.PerlinNoise(Time.time*19.7f,3)+.15f*Mathf.Sin(Time.time*13.1f);
            flameLight.enabled=lit;flameLight.intensity=1.18f+flicker*.38f;
            flameLight.color=fireColor;
            if(reflectedLight){reflectedLight.enabled=lit;reflectedLight.intensity=.52f+flicker*.04f;}
        }
    }
    public bool HasLiveGrip => hand&&prop;
    public Vector3 GripPositionTuning => lighterGripPosition;
    public Vector3 GripRotationTuning => lighterGripRotation;
    public Vector3 GripWorldPosition => hand
        ? hand.position+hand.forward.normalized*lighterGripPosition.x+hand.right.normalized*lighterGripPosition.y+Vector3.up*lighterGripPosition.z
        : transform.position;
    public Quaternion GripWorldRotation => Quaternion.LookRotation(transform.forward,Vector3.up)*Quaternion.Euler(lighterGripRotation);
    public void SetGripFromWorld(Vector3 worldPosition,Quaternion worldRotation)
    {
        if(!hand)return;
        Vector3 a=hand.forward.normalized,b=hand.right.normalized,c=Vector3.up,d=worldPosition-hand.position;
        float determinant=Vector3.Dot(a,Vector3.Cross(b,c));
        if(Mathf.Abs(determinant)>.0001f)lighterGripPosition=new Vector3(
            Vector3.Dot(d,Vector3.Cross(b,c))/determinant,
            Vector3.Dot(d,Vector3.Cross(c,a))/determinant,
            Vector3.Dot(d,Vector3.Cross(a,b))/determinant);
        Quaternion baseRotation=Quaternion.LookRotation(transform.forward,Vector3.up);
        Vector3 euler=(Quaternion.Inverse(baseRotation)*worldRotation).eulerAngles;
        lighterGripRotation=new Vector3(Mathf.DeltaAngle(0,euler.x),Mathf.DeltaAngle(0,euler.y),Mathf.DeltaAngle(0,euler.z));
    }
    public void SetGripTuning(Vector3 position,Vector3 rotation)
    {lighterGripPosition=position;lighterGripRotation=rotation;}
    void PrepareGrip()
    {
        if(!hand)return;
        foreach(var renderer in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            int index=System.Array.IndexOf(renderer.bones,hand);if(index<0 || !renderer.sharedMesh.isReadable)continue;
            gloveRenderer=renderer;originalGlove=renderer.sharedMesh;grippingGlove=Instantiate(originalGlove);grippingGlove.name="Temporary lighter grip";
            relaxedVertices=originalGlove.vertices;curledVertices=(Vector3[])relaxedVertices.Clone();poseVertices=(Vector3[])relaxedVertices.Clone();
            var bind=originalGlove.bindposes[index];var inverse=bind.inverse;var weights=originalGlove.boneWeights;
            float maximum=0;Vector3 average=Vector3.zero;int count=0;
            for(int i=0;i<weights.Length;i++)
            {
                var w=weights[i];float weight=(w.boneIndex0==index?w.weight0:0)+(w.boneIndex1==index?w.weight1:0)+(w.boneIndex2==index?w.weight2:0)+(w.boneIndex3==index?w.weight3:0);
                if(weight<.7f)continue;var v=bind.MultiplyPoint3x4(relaxedVertices[i]);maximum=Mathf.Max(maximum,v.y);average+=v;count++;
            }
            gripLocal=count>0?average/count:Vector3.zero;gripLocal.y*=.78f;
            for(int i=0;i<weights.Length;i++)
            {
                var w=weights[i];float weight=(w.boneIndex0==index?w.weight0:0)+(w.boneIndex1==index?w.weight1:0)+(w.boneIndex2==index?w.weight2:0)+(w.boneIndex3==index?w.weight3:0);
                if(weight<.7f)continue;var v=bind.MultiplyPoint3x4(relaxedVertices[i]);float start=maximum*.55f;
                if(v.y<=start)continue;float length=v.y-start;float angle=Mathf.Clamp01(length/(maximum-start))*2.1f;
                float radius=(maximum-start)/2.1f;
                v.y=start+Mathf.Sin(angle)*radius;v.z-=radius*(1-Mathf.Cos(angle));
                curledVertices[i]=inverse.MultiplyPoint3x4(v);
            }
            renderer.sharedMesh=grippingGlove;break;
        }
    }
    void RebuildProp()
    {
        if(prop)Destroy(prop.gameObject);
        foreach(var mesh in ownedMeshes)if(mesh)Destroy(mesh);ownedMeshes.Clear();
        if(flameMaterial)Destroy(flameMaterial);
        if(!item||!hand)return;
        prop=new GameObject("Held field lighter").transform;prop.SetParent(transform,false);
        var root=item.Pickup.PresentationRoot;var filters=root.GetComponentsInChildren<MeshFilter>(true);
        var bounds=filters.Length>0?filters[0].sharedMesh.bounds:new Bounds(Vector3.zero,Vector3.one);
        // The imported asset's +Z is its lid/nozzle end. World pickup rotation must never affect the grip.
        Quaternion upright=Quaternion.Euler(-90,0,0);
        float scale=.082f/Mathf.Max(.001f,bounds.size.z);
        if(heldMaterial)Destroy(heldMaterial);
        heldMaterial=new Material(cleanMaterial?cleanMaterial:filters[0].GetComponent<Renderer>().sharedMaterial);
        if(heldMaterial.HasProperty("_EmissionColor"))heldMaterial.SetColor("_EmissionColor",Color.black);
        heldMaterial.DisableKeyword("_EMISSION");
        heldMaterial.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
        heldMaterial.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        if(heldMaterial.HasProperty("_Smoothness"))heldMaterial.SetFloat("_Smoothness",.65f);
        foreach(var mf in filters)
        {
            var renderer=mf.GetComponent<MeshRenderer>();if(!renderer||!mf.sharedMesh||!mf.sharedMesh.isReadable)continue;
            var mesh=Instantiate(mf.sharedMesh);var vertices=mesh.vertices;
            for(int i=0;i<vertices.Length;i++)vertices[i]=upright*(vertices[i]-bounds.center)*scale+Vector3.up*.02f;
            mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();ownedMeshes.Add(mesh);
            var go=new GameObject("Lighter shell",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(prop,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterials=System.Array.ConvertAll(renderer.sharedMaterials,m=>heldMaterial);
        }
        flame=GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;flame.name="Lighter flame";flame.SetParent(prop,false);flame.localPosition=new Vector3(0,.068f,0);Destroy(flame.GetComponent<Collider>());
        flameMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));flameMaterial.SetColor("_BaseColor",fireColor*3.2f);flame.GetComponent<Renderer>().sharedMaterial=flameMaterial;flame.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        flameLight=new GameObject("Hand flame light - no warmth").AddComponent<Light>();flameLight.transform.SetParent(prop,false);flameLight.transform.localPosition=new Vector3(0,.17f,.42f);flameLight.type=LightType.Point;flameLight.color=fireColor;flameLight.range=11f;flameLight.shadows=LightShadows.Soft;
        flameLight.shadowStrength=.8f;flameLight.shadowBias=.035f;flameLight.shadowNormalBias=.15f;
        flameLight.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>().additionalLightsShadowResolutionTier=1;
        flameLight.renderMode=LightRenderMode.ForcePixel;flameLight.bounceIntensity=1;
        // A restrained bounce fill lifts nearby dark surfaces without increasing
        // the visible flame or the harsh hotspot on the glove.
        reflectedLight=new GameObject("Lighter reflected fill").AddComponent<Light>();reflectedLight.transform.SetParent(prop,false);reflectedLight.transform.localPosition=new Vector3(0,1.15f,0);reflectedLight.type=LightType.Point;reflectedLight.range=9f;reflectedLight.color=new Color(1f,.82f,.56f);reflectedLight.intensity=.24f;reflectedLight.shadows=LightShadows.None;
    }
    void BuildTutorial()
    {
        var go=new GameObject("Lighter tutorial",typeof(Canvas),typeof(CanvasScaler));canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=900;
        var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1440,900);scaler.matchWidthOrHeight=.5f;
        var surface=SurvivalUITheme.Surface(go.transform,"Lighter field guide",0,0,342,166);var r=surface.rectTransform;r.anchorMin=r.anchorMax=r.pivot=Vector2.one;r.anchoredPosition=new Vector2(-32,-180);
        tutorial=surface.gameObject.AddComponent<CanvasGroup>();tutorial.alpha=0;tutorial.blocksRaycasts=false;
        detail=SurvivalGuideCard.Build(surface.transform,"EQUIPMENT", "A little light", "R", "TOGGLE FLAME", "Equip the lighter from your quickbar.\nToggle its flame with R. Fuel burns while lit.");

    }
    void OnDisable(){if(item)item.Extinguish();if(flameLight)flameLight.enabled=false;if(reflectedLight)reflectedLight.enabled=false;if(prop)prop.gameObject.SetActive(false);if(tutorial)tutorial.alpha=0;}
    void OnDestroy(){if(gloveRenderer && originalGlove)gloveRenderer.sharedMesh=originalGlove;if(grippingGlove)Destroy(grippingGlove);if(prop)Destroy(prop.gameObject);if(canvas)Destroy(canvas.gameObject);foreach(var m in ownedMeshes)if(m)Destroy(m);if(flameMaterial)Destroy(flameMaterial);if(heldMaterial)Destroy(heldMaterial);}
}
