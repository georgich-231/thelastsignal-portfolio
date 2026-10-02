using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(PlayerInventory))]
public sealed class GameplayInteraction : MonoBehaviour
{
    public bool IsBusy { get; private set; }
    public bool BlocksPickup => IsBusy || (GetComponent<ShedNightSequence>()?.CanUseBed ?? false) || (opening!=null && (opening.BlocksGameplay || opening.CanReadNote)) || (ui != null && !ui.IsOpen && FindBonfire() != null);
    OpeningSequence opening;
    public string Message { get; private set; }
    PlayerInventory inventory;
    InventoryUI ui;
    PlayerMovement movement;
    Canvas canvas;
    CanvasGroup prompt, fade;
    Text promptText, promptTitle, promptCategory, keyLabel;
    SurvivalPromptView promptView;
    public int DeniedFeedbackCount => promptView != null ? promptView.FeedbackCount : 0;
    bool priorMovement;
    float messageUntil;
    readonly RaycastHit[] hits = new RaycastHit[24];
    void Awake()
    {
        inventory=GetComponent<PlayerInventory>(); ui=GetComponent<InventoryUI>(); movement=GetComponent<PlayerMovement>();
        opening=GetComponent<OpeningSequence>();
        BuildUI();
    }
    public BonfireInteractable FindBonfire()
    {
        BonfireInteractable best=null; float nearest=float.MaxValue;
        foreach(var fire in BonfireInteractable.Active)
        {
            if(fire==null || fire.IsLit) continue;
            float d=Vector2.Distance(new Vector2(transform.position.x,transform.position.z),new Vector2(fire.InteractionCenter.x,fire.InteractionCenter.z));
            if(d <= fire.interactionRadius && d<nearest && Visible(fire)) { best=fire; nearest=d; }
        }
        return best;
    }
    bool Visible(BonfireInteractable fire)
    {
        Vector3 origin=transform.position+Vector3.up*.6f;
        Vector3 delta=fire.InteractionCenter+Vector3.up*.3f-origin;
        int count=Physics.RaycastNonAlloc(origin,delta.normalized,hits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
        for(int n=0;n<count;n++)
        {
            var c=hits[n].collider;
            if(c is TerrainCollider || c.transform.IsChildOf(transform) || c.transform.IsChildOf(fire.transform) || c.GetComponentInParent<CanopyOccluder>()!=null) continue;
            return false;
        }
        return true;
    }
    void Update()
    {
        if(IsBusy) return;
        bool active=ui!=null && !ui.IsOpen && Time.timeScale>0 && (opening==null || !opening.BlocksGameplay);
        var target=active ? FindBonfire() : null;
        prompt.alpha=active && (target!=null || Time.unscaledTime<messageUntil) ? 1 : 0;
        if(target!=null && Time.unscaledTime>=messageUntil)
        {
            promptView.SetWorldAnchor(target.PromptPoint);
            string requirement=Requirement();
            PresentPrompt("LIGHT BONFIRE", requirement.Length > 0 ? requirement : "LIGHTER READY  /  PRESS TO IGNITE", requirement.Length == 0, true);
        }
#if ENABLE_INPUT_SYSTEM
        var k=Keyboard.current;
        if(!active || k==null) return;
        if(k.eKey.wasPressedThisFrame) TryInteract();
        if(k.gKey.wasPressedThisFrame && !IsBusy) DropEquipped();
#endif
    }
    string Requirement()
    {
        var lighter=inventory.EquippedItem as LighterItem;
        if(lighter==null)
        {
            foreach(var item in inventory.slots) if(item is LighterItem) return "SELECT LIGHTER IN QUICKBAR";
            return "LIGHTER REQUIRED";
        }
        if(lighter.condition<=0) return "LIGHTER IS WORN OUT";
        if(lighter.fuelSeconds<1f) return "LIGHTER NEEDS FUEL";
        return "";
    }
    public bool TryInteract()
    {
        if(IsBusy || ui==null || ui.IsOpen || Time.timeScale<=0 || (opening!=null && (opening.BlocksGameplay || opening.CanReadNote))) return false;
        var fire=FindBonfire();
        if(fire==null) return false;
        string requirement=Requirement();
        if(requirement.Length>0) { ShowMessage(requirement); return false; }
        StartCoroutine(LightBonfire(fire,inventory.EquippedItem as LighterItem)); return true;
    }
    public bool DropEquipped()
    {
        if(IsBusy || ui==null || ui.IsOpen || Time.timeScale<=0 || inventory.EquippedItem==null || (opening!=null && opening.BlocksGameplay)) return false;
        bool dropped=inventory.Drop(inventory.EquippedSlot);
        ShowMessage(inventory.LastMessage); return dropped;
    }
    void ShowMessage(string message)
    {
        Message=message;
        var nearby=FindBonfire();
        bool atFire=nearby!=null;
        promptView.SetWorldAnchor(atFire ? nearby.PromptPoint : transform.position+Vector3.up*1.2f);
        if(atFire)
        {
            promptCategory.text="UNABLE TO LIGHT BONFIRE";
            bool missing=message.Contains("REQUIRED");
            promptTitle.text=missing ? "LIGHTER REQUIRED" : message.Contains("SELECT") ? "EQUIP YOUR LIGHTER" : message.Contains("FUEL") ? "LIGHTER IS EMPTY" : "CANNOT LIGHT FIRE";
            promptText.text=missing ? "You need a lighter to light this bonfire." : message.Contains("SELECT") ? "Select your lighter in the quickbar, then try again." : message.Contains("FUEL") ? "Refill your lighter before lighting this fire." : message;
            promptView.Denied();
        }
        else
        {
            promptCategory.text="BACKPACK"; promptTitle.text="EQUIPMENT"; promptText.text=message;
            promptText.color=SurvivalUITheme.Secondary; keyLabel.text="!";
        }
        messageUntil=Time.unscaledTime+2.2f; prompt.alpha=1;
    }
    void PresentPrompt(string title,string detail,bool ready,bool action)
    {
        promptTitle.text=title;
        promptCategory.text=action ? "CAMPFIRE  /  UNLIT" : "FIELD EQUIPMENT";
        keyLabel.text=action ? "E" : "!";
        promptText.text=ready ? "Lighter equipped. Press E to ignite." : detail=="LIGHTER REQUIRED" ? "Requires a lighter." : detail=="SELECT LIGHTER IN QUICKBAR" ? "Select your lighter in the quickbar." : detail=="LIGHTER NEEDS FUEL" ? "Your lighter needs fuel." : detail;
        promptText.color=ready ? SurvivalUITheme.Ice : SurvivalUITheme.Secondary;
    }
    IEnumerator LightBonfire(BonfireInteractable fire,LighterItem lighter)
    {
        IsBusy=true; priorMovement=movement!=null && movement.enabled;
        if(movement!=null) movement.enabled=false;
        prompt.alpha=0; fade.blocksRaycasts=true;
        yield return FadeTo(1,.24f);
        yield return new WaitForSecondsRealtime(.6f);
        bool lit=fire!=null && inventory.EquippedItem==lighter && fire.TryLight(lighter);
        yield return FadeTo(0,.4f);
        fade.blocksRaycasts=false;
        if(movement!=null) movement.enabled=priorMovement;
        IsBusy=false;
        if(!lit) ShowMessage("Unable to light bonfire.");
    }
    IEnumerator FadeTo(float target,float duration)
    {
        float start=fade.alpha;
        for(float t=0;t<duration;t+=Time.unscaledDeltaTime) { fade.alpha=Mathf.Lerp(start,target,t/duration); yield return null; }
        fade.alpha=target;
    }
    void BuildUI()
    {
        var go=new GameObject("World Interaction",typeof(Canvas),typeof(CanvasScaler)); canvas=go.GetComponent<Canvas>();
        canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=900;
        var scale=go.GetComponent<CanvasScaler>(); scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scale.referenceResolution=new Vector2(1440,900); scale.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        promptView=SurvivalPromptView.Create(go.transform,"Bonfire Interaction Prompt");
        prompt=promptView.Group; promptText=promptView.Detail; promptTitle=promptView.Title; promptCategory=promptView.Category; keyLabel=promptView.KeyLabel;
        RectTransform rect;
        var dark=new GameObject("Ignition Fade",typeof(RectTransform),typeof(Image),typeof(CanvasGroup)); dark.transform.SetParent(go.transform,false);
        rect=dark.GetComponent<RectTransform>(); rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.offsetMin=rect.offsetMax=Vector2.zero;
        dark.GetComponent<Image>().color=Color.black; fade=dark.GetComponent<CanvasGroup>(); fade.alpha=0; fade.blocksRaycasts=false;
    }
    void OnDisable()
    {
        StopAllCoroutines();
        if(IsBusy && movement!=null) movement.enabled=priorMovement;
        IsBusy=false; if(fade!=null) { fade.alpha=0; fade.blocksRaycasts=false; } if(prompt!=null) prompt.alpha=0;
    }
    void OnDestroy() { if(canvas!=null) Destroy(canvas.gameObject); }
}
