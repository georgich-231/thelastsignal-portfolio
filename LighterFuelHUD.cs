using UnityEngine;
using UnityEngine.UI;

public sealed class LighterFuelHUD : MonoBehaviour
{
    PlayerInventory pack;OpeningSequence story;InventoryUI inventory;PlayerConditions conditions;
    GameObject root;CanvasGroup group;RectTransform rect;Text value,status;Image fill,marker,mark;
    public bool Visible => group && group.alpha>.01f;
    void Awake()
    {
        pack=GetComponent<PlayerInventory>();story=GetComponent<OpeningSequence>();inventory=GetComponent<InventoryUI>();conditions=GetComponent<PlayerConditions>();
        root=new GameObject("Low lighter fuel HUD",typeof(Canvas),typeof(CanvasScaler));var c=root.GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=405;
        var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1440,900);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        var go=new GameObject("Fuel warning",typeof(RectTransform),typeof(CanvasGroup));go.transform.SetParent(root.transform,false);rect=go.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.zero;rect.sizeDelta=new Vector2(252,74);rect.anchoredPosition=new Vector2(38,40);
        group=go.GetComponent<CanvasGroup>();group.alpha=0;group.blocksRaycasts=false;
        // Same gauge as warmth: glass card, diamond, tracked label, value, capsule bar.
        var panel=SurvivalUITheme.Surface(rect,"Fuel readout",0,0,252,74);panel.color=new Color(1,1,1,.9f);
        mark=SurvivalUITheme.Diamond(rect,16,15,9,SurvivalUITheme.Ember).Find("Mark").GetComponent<Image>();
        SurvivalUITheme.Text(rect,"LIGHTER FUEL",32,11,150,16,10,false,SurvivalUITheme.Mist);
        value=SurvivalUITheme.Text(rect,"10%",150,4,86,28,24,true,SurvivalUITheme.Ember,TextAnchor.MiddleRight);
        fill=SurvivalUITheme.Bar(rect,"Fuel remaining",14,38,224,5,SurvivalUITheme.Ember,out _);
        marker=SurvivalUITheme.Glow(rect,SurvivalUITheme.Ember,22);marker.rectTransform.anchorMin=marker.rectTransform.anchorMax=new Vector2(0,1);
        status=SurvivalUITheme.Text(rect,"FUEL RUNNING LOW",14,52,224,14,9,false,SurvivalUITheme.Mist);
    }
    void Update()
    {
        LighterItem lighter=pack.EquippedItem as LighterItem;
        if(!lighter)foreach(var item in pack.slots)if(item is LighterItem found){lighter=found;break;}
        float fraction=lighter?Mathf.Clamp01(lighter.fuelSeconds/Mathf.Max(1,lighter.fuelCapacitySeconds)):1;
        bool gameplay=!PauseMenu.IsOpen&&!inventory.IsOpen&&!story.BlocksGameplay;
        bool show=gameplay&&lighter&&fraction<=.10001f;
        group.alpha=gameplay?Mathf.MoveTowards(group.alpha,show?story.QuickbarAlpha:0,Time.unscaledDeltaTime*3):0;
        var target=new Vector2(38,conditions&&conditions.warmth<30?124:40);rect.anchoredPosition=Vector2.Lerp(rect.anchoredPosition,target,1-Mathf.Exp(-10*Time.unscaledDeltaTime));
        var color=Color.Lerp(SurvivalUITheme.Warning,SurvivalUITheme.Ice,Mathf.Clamp01(fraction/.1f)*.45f);
        fill.rectTransform.sizeDelta=new Vector2(224*fraction,5);fill.color=value.color=mark.color=color;var glow=color;glow.a=.55f+.3f*Mathf.Sin(Time.unscaledTime*4);marker.color=glow;
        marker.rectTransform.anchoredPosition=new Vector2(14+224*fraction,-40.5f);
        value.text=Mathf.CeilToInt(fraction*100)+"%";status.text=fraction<=0?"EMPTY  ·  REFILL REQUIRED":"FUEL RUNNING LOW";
    }
    void OnDestroy(){if(root)Destroy(root);}
}
