using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-60)]
public sealed class PlayerConditions : MonoBehaviour
{
    [Range(0,100)] public float warmth=0;
    public float Hunger => 100;
    public float Thirst => 100;
    public float TemperatureC { get; private set; }=-14;
    public float Hour { get; private set; }=16;
    public bool NearFire { get; private set; }
    public bool CanSprint => warmth>.05f;
    public float ColdSeverity => 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,30,warmth));
    public string ThermalStatus => NearFire?"WARMING UP":warmth<=.05f?"FREEZING / CANNOT RUN":warmth<30?"EXPOSED TO COLD":"WARMTH STABLE";
    OpeningSequence opening;InventoryUI inventory;
    GameObject canvasRoot;CanvasGroup hud,coldGroup;Material frost;
    Text warmthText,status;Image warmthFill, warmthTrack, warmthMarker;
    RectTransform gaugeMark;Image gaugeMarkImage;
    float elapsed,visualCold;
    void Awake(){opening=GetComponent<OpeningSequence>();inventory=GetComponent<InventoryUI>();Build();}
    void Update()
    {
        bool active=!PauseMenu.IsOpen && !inventory.IsOpen && !opening.BlocksGameplay;
        if(active)Tick(Time.deltaTime);
        bool show=active && opening.QuickbarAlpha>.01f;
        hud.alpha=show && warmth<30?opening.QuickbarAlpha:0;
        coldGroup.alpha=show?opening.QuickbarAlpha:0;
        visualCold=Mathf.MoveTowards(visualCold,ColdSeverity,Time.unscaledDeltaTime*.6f);
        frost.SetFloat("_Cold",visualCold);frost.SetFloat("_Breath",Time.time);
        warmthText.text=Mathf.CeilToInt(warmth)+"%";status.text=ThermalStatus;
        float fraction=Mathf.Clamp01(warmth/100);
        Color severity=Color.Lerp(SurvivalUITheme.Warning,SurvivalUITheme.Ice,Mathf.SmoothStep(0,1,warmth/30));
        warmthFill.rectTransform.sizeDelta=new Vector2(224*fraction,5);warmthFill.color=severity;var markColor=severity;markColor.a=.55f+.3f*Mathf.Sin(Time.unscaledTime*4);if(gaugeMarkImage)gaugeMarkImage.color=severity;
        warmthMarker.rectTransform.anchoredPosition=new Vector2(14+224*fraction,-40.5f);warmthMarker.color=markColor;
        warmthTrack.color=Color.Lerp(new Color(.45f,.22f,.16f,.35f),new Color(.30f,.40f,.47f,.35f),warmth/30);
        warmthText.color=severity;status.color=Color.Lerp(SurvivalUITheme.Secondary,severity,.5f);
    }
    public void SetHour(float hour) { elapsed=Mathf.Repeat(hour-16,24)*300; Hour=Mathf.Repeat(hour,24); }
    public void Tick(float seconds)
    {
        elapsed+=Mathf.Max(0,seconds);Hour=Mathf.Repeat(16+elapsed/300,24);
        float daylight=Mathf.Cos((Hour-14)/24*Mathf.PI*2);
        TemperatureC=-13+daylight*4-opening.WeatherSeverity*6;
        float heat=0;
        foreach(var fire in BonfireInteractable.Active)
        {
            if(!fire || !fire.IsLit)continue;
            Vector3 delta=transform.position-fire.InteractionCenter;delta.y=0;
            float gain=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.4f,4.5f,delta.magnitude));
            if(gain<=0)continue;
            bool blocked=false;
            foreach(var hit in Physics.RaycastAll(transform.position, (fire.InteractionCenter+Vector3.up*.5f-transform.position).normalized,Vector3.Distance(transform.position,fire.InteractionCenter+Vector3.up*.5f),~0,QueryTriggerInteraction.Ignore))
                if(hit.collider.GetComponentInParent<BuildingInterior>()!=null){blocked=true;break;}
            if(!blocked)heat=Mathf.Max(heat,gain);
        }
        NearFire=heat>.08f;
        bool sheltered=opening.outpost && opening.outpost.Contains(transform.position);
        float drain=Mathf.Lerp(.07f,.23f,Mathf.InverseLerp(0,-25,TemperatureC))*(sheltered?.25f:1);
        warmth=Mathf.Clamp(warmth+(heat*3.2f-drain)*seconds,0,100);
    }
    void Build()
    {
        canvasRoot=new GameObject("Cold and survival HUD",typeof(Canvas),typeof(CanvasScaler));var canvas=canvasRoot.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=400;
        var scaler=canvasRoot.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1440,900);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        var cold=new GameObject("Frost on lens",typeof(RectTransform),typeof(RawImage),typeof(CanvasGroup));cold.transform.SetParent(canvasRoot.transform,false);var cr=cold.GetComponent<RectTransform>();cr.anchorMin=Vector2.zero;cr.anchorMax=Vector2.one;cr.offsetMin=cr.offsetMax=Vector2.zero;
        frost=new Material(Resources.Load<Material>("FreezingLens"));cold.GetComponent<RawImage>().material=frost;cold.GetComponent<RawImage>().raycastTarget=false;coldGroup=cold.GetComponent<CanvasGroup>();coldGroup.alpha=0;
        var root=new GameObject("Low warmth warning",typeof(RectTransform),typeof(CanvasGroup));root.transform.SetParent(canvasRoot.transform,false);var rect=root.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=Vector2.zero;rect.anchoredPosition=new Vector2(38,40);rect.sizeDelta=new Vector2(260,106);hud=root.GetComponent<CanvasGroup>();hud.alpha=0;hud.blocksRaycasts=false;
        rect.sizeDelta=new Vector2(252,74);
        // Gauge: glass card, a coloured diamond, tracked label, big value, a capsule bar
        // with a glowing tip, and the status in small capitals.
        var panel=SurvivalUITheme.Surface(rect,"Thermal readout",0,0,252,74);panel.color=new Color(1,1,1,.9f);
        gaugeMark=SurvivalUITheme.Diamond(rect,16,15,9,SurvivalUITheme.Ember);gaugeMarkImage=gaugeMark.Find("Mark").GetComponent<Image>();
        SurvivalUITheme.Text(rect,"WARMTH",32,11,150,16,10,false,SurvivalUITheme.Mist);
        warmthText=SurvivalUITheme.Text(rect,"0%",150,4,86,28,24,true,SurvivalUITheme.Snow,TextAnchor.MiddleRight);
        warmthFill=SurvivalUITheme.Bar(rect,"Warmth remaining",14,38,224,5,SurvivalUITheme.Ember,out warmthTrack);
        warmthMarker=SurvivalUITheme.Glow(rect,SurvivalUITheme.Ember,22);warmthMarker.rectTransform.anchorMin=warmthMarker.rectTransform.anchorMax=new Vector2(0,1);
        status=SurvivalUITheme.Text(rect,"FREEZING  ·  CANNOT RUN",14,52,224,14,9,false,SurvivalUITheme.Mist);

    }
    void OnDestroy(){if(canvasRoot)Destroy(canvasRoot);if(frost)Destroy(frost);}
}
