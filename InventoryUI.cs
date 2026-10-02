using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

[RequireComponent(typeof(PlayerInventory))]
public sealed class InventoryUI : MonoBehaviour
{
    public bool IsOpen { get; private set; }
    public int SelectedSlot { get; private set; } = -1;
    PlayerInventory inventory;
    PickupInteractionController pickup;
    PlayerMovement movement;
    bool priorMovement, priorCursor;
    CursorLockMode priorLock;
    GameObject canvasRoot, window, hud, ownedEvents;
    CanvasGroup hudFade, windowFade;
    Text weight, title, description, status;
    Text conditionValues, inventoryWarmth; Image inventoryWarmthFill;
    RawImage preview;
    Transform attributeRoot;

    readonly System.Collections.Generic.List<ItemInspectAttribute> attributes = new System.Collections.Generic.List<ItemInspectAttribute>();
    readonly System.Collections.Generic.List<InspectRow> attributeRows = new System.Collections.Generic.List<InspectRow>();
    sealed class InspectRow { public GameObject root; public Text label, value; public Image bar, track; }
    InventorySlotUI[] cells = new InventorySlotUI[20], keys = new InventorySlotUI[6], gameplayKeys = new InventorySlotUI[6];
    InventoryItem previewItem;
    Texture previewTexture;
    Material backdrop;
    RawImage dragGhost;
    Text itemCount;
    Image carryFill;
    Text inspectionHint;
    GameObject emptyInspect;
    Image spotlight;
    Text quickName; CanvasGroup quickNameGroup; int lastQuick = -2; float quickNameAt = -10;
    float inputReady, openedAt;
    int dragging = -1;
    readonly Color orange = SurvivalUITheme.Frost;
    readonly Color muted = SurvivalUITheme.Mist;
    public int DraggedSlot => dragging;
    const float CarryWidth = 134, WarmthWidth = 134, AttributeWidth = 420;

    void Awake()
    {
        inventory = GetComponent<PlayerInventory>(); pickup = GetComponent<PickupInteractionController>(); movement = GetComponent<PlayerMovement>();
        Build(); inventory.Changed += Refresh; Refresh();
    }
    void EnsureEvents()
    {
        if (EventSystem.current != null) return;
        ownedEvents = new GameObject("Inventory Event System", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        ownedEvents.AddComponent<InputSystemUIInputModule>();
#else
        ownedEvents.AddComponent<StandaloneInputModule>();
#endif
    }
    public void OpenItem(InventoryItem item)
    {
        SelectedSlot = System.Array.IndexOf(inventory.slots, item); SetOpen(true); Select(SelectedSlot);
    }
    public void SetOpen(bool open)
    {
        var opening = GetComponent<OpeningSequence>();
        if (open && opening != null && opening.BlocksGameplay) return;
        if (open == IsOpen || (open && GetComponent<GameplayInteraction>() != null && GetComponent<GameplayInteraction>().IsBusy)) return;
        if (open)
        {
            EnsureEvents(); priorMovement = movement != null && movement.enabled;
            priorCursor = Cursor.visible; priorLock = Cursor.lockState;
            if (movement != null) movement.enabled = false;
            Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            GetComponent<SurvivalAudio>()?.CloseBackpack();
            if (movement != null) movement.enabled = priorMovement;
            Cursor.visible = priorCursor; Cursor.lockState = priorLock;
            pickup.HideInventoryPreview(); previewItem = null; EndDrag();
        }
        IsOpen = open; window.SetActive(open); hud.SetActive(!open); inputReady = Time.unscaledTime + .2f;
        if (open) { openedAt = Time.unscaledTime; Select(SelectedSlot); GetComponent<SurvivalAudio>()?.Backpack(); }
        Refresh();
    }
    void Update()
    {
        var opening = GetComponent<OpeningSequence>();
        if (opening != null && opening.BlocksGameplay) return;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        if (Time.unscaledTime < inputReady || PauseMenu.EscapeHandledFrame == Time.frameCount) return;
        if (k.tabKey.wasPressedThisFrame || k.iKey.wasPressedThisFrame) SetOpen(!IsOpen);
        if (IsOpen && k.escapeKey.wasPressedThisFrame) SetOpen(false);

        for (int n = 0; n < 6; n++) if (NumberPressed(k, n))
        {
            if (IsOpen) inventory.Assign(SelectedSlot, n); else if (GetComponent<GameplayInteraction>() == null || !GetComponent<GameplayInteraction>().IsBusy) inventory.SelectQuickSlot(n);
        }
#endif
    }
#if ENABLE_INPUT_SYSTEM
    static bool NumberPressed(Keyboard k, int n)
    {
        switch (n)
        {
            case 0: return k.digit1Key.wasPressedThisFrame; case 1: return k.digit2Key.wasPressedThisFrame;
            case 2: return k.digit3Key.wasPressedThisFrame; case 3: return k.digit4Key.wasPressedThisFrame;
            case 4: return k.digit5Key.wasPressedThisFrame; default: return k.digit6Key.wasPressedThisFrame;
        }
    }
#endif
    void LateUpdate()
    {
        var opening = GetComponent<OpeningSequence>(); bool hidden = IsOpen || PauseMenu.IsOpen || (opening && opening.IsReading) || (pickup && pickup.IsInspecting);
        var conditions = GetComponent<PlayerConditions>();
        if (conditionValues && conditions)
        {
            conditionValues.text = $"{conditions.TemperatureC:0.0} °C   ·   {Mathf.FloorToInt(conditions.Hour):00}:00";
            inventoryWarmth.text = $"WARMTH   {conditions.warmth:0}%";
            inventoryWarmthFill.rectTransform.sizeDelta = new Vector2(WarmthWidth * conditions.warmth / 100, 4);
            inventoryWarmthFill.color = Color.Lerp(SurvivalUITheme.Ember, SurvivalUITheme.Frost, Mathf.SmoothStep(0, 1, conditions.warmth / 40));
        }
        hudFade.alpha = hidden ? 0 : opening ? opening.QuickbarAlpha : 1; hudFade.blocksRaycasts = hudFade.alpha > .98f; hudFade.interactable = hudFade.blocksRaycasts;

        // Opening the pack: the window fades up and its panels settle.
        if (IsOpen && windowFade) windowFade.alpha = Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.unscaledTime - openedAt) / .22f));
        if (spotlight) { float b = .20f + .04f * Mathf.Sin(Time.unscaledTime * 1.3f); spotlight.color = new Color(.62f, .84f, .95f, preview.texture ? b : 0); }

        // Quickbar: a callout with the item's name whenever the selection changes.
        if (inventory.SelectedQuickSlot != lastQuick)
        {
            if (lastQuick != -2) quickNameAt = Time.unscaledTime;
            lastQuick = inventory.SelectedQuickSlot;
            var sel = lastQuick >= 0 && inventory.quickbar[lastQuick] >= 0 ? inventory.slots[inventory.quickbar[lastQuick]] : null;
            quickName.text = sel != null ? sel.Pickup.displayName.ToUpperInvariant() : "";
        }
        float age = Time.unscaledTime - quickNameAt;
        quickNameGroup.alpha = string.IsNullOrEmpty(quickName.text) ? 0 : Mathf.Clamp01(age / .15f) * (1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - 1.6f) / .5f)));
        ((RectTransform)quickNameGroup.transform).anchoredPosition = new Vector2(0, 92 + 6 * (1 - Mathf.Clamp01(age / .25f)));
    }
    public void Select(int index)
    {
        SelectedSlot = index >= 0 && index < inventory.slots.Length ? index : -1;
        var item = SelectedSlot >= 0 ? inventory.slots[SelectedSlot] : null;
        if (IsOpen && item != previewItem)
        {
            pickup.HideInventoryPreview(); previewItem = item;
            if (item != null) previewTexture = pickup.ShowInventoryPreview(item.Pickup);
        }
        preview.texture = item != null ? previewTexture : null;
        preview.color = item != null ? Color.white : Color.clear;
        Refresh();
    }
    public void BeginDrag(int slot)
    {
        dragging = slot; Select(slot);
        dragGhost.texture = inventory.slots[slot] != null ? inventory.slots[slot].Icon : null;
        dragGhost.gameObject.SetActive(dragGhost.texture != null);
    }
    public void DragPointer(Vector2 screenPosition)
    {
        if (dragging < 0) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot.GetComponent<RectTransform>(), screenPosition, null, out var local);
        dragGhost.rectTransform.anchoredPosition = local + new Vector2(28, -28);
    }
    public void EndDrag() { dragging = -1; if (dragGhost != null) dragGhost.gameObject.SetActive(false); }
    public void DropOn(int index, bool quick)
    {
        if (dragging < 0) return;
        int source = dragging;
        if (quick) inventory.Assign(source, index);
        else { inventory.Move(source, index); Select(index); }
        EndDrag(); Refresh();
    }
    public void Rotate(Vector2 delta) { pickup.RotateInventoryPreview(delta.x, delta.y); }

    void Refresh()
    {
        if (weight == null) return;
        weight.text = $"{inventory.TotalWeight:0.00}  /  {inventory.capacityKg:0.0} kg";
        int count = 0; foreach (var stored in inventory.slots) if (stored != null) count++;
        itemCount.text = $"{count:00}  /  {inventory.slots.Length}  SLOTS";
        carryFill.rectTransform.sizeDelta = new Vector2(CarryWidth * Mathf.Clamp01(inventory.TotalWeight / Mathf.Max(.001f, inventory.capacityKg)), 4);
        var item = SelectedSlot >= 0 ? inventory.slots[SelectedSlot] : null;
        title.text = item != null ? item.Pickup.displayName : "No item selected";
        title.color = item != null ? SurvivalUITheme.Snow : SurvivalUITheme.Slate;
        description.text = item != null ? item.Pickup.description : "";
        inspectionHint.gameObject.SetActive(item != null); emptyInspect.SetActive(item == null);
        RefreshAttributes(item);

        status.text = item != null && item.IsLit ? "FLAME ACTIVE   ·   FUEL IS BURNING" : (inventory.LastMessage ?? "Travel light. Keep the essentials.");
        status.color = item != null && item.IsLit ? SurvivalUITheme.Ember : SurvivalUITheme.Mist;
        for (int n = 0; n < cells.Length; n++) cells[n].Show(inventory.slots[n], n == SelectedSlot, inventory.slots[n] != null ? inventory.slots[n].Icon : null, orange, muted);
        for (int n = 0; n < keys.Length; n++)
        {
            var assigned = inventory.quickbar[n] >= 0 ? inventory.slots[inventory.quickbar[n]] : null;
            keys[n].Show(assigned, n == inventory.SelectedQuickSlot, assigned != null ? assigned.Icon : null, orange, muted);
            gameplayKeys[n].Show(assigned, n == inventory.SelectedQuickSlot, assigned != null ? assigned.Icon : null, orange, muted);
        }
    }

    // ------------------------------------------------------------------ layout

    void Build()
    {
        canvasRoot = new GameObject("Survival Inventory UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 600;
        var scaler = canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1440, 900); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        // Backdrop: the world, frosted and darkened, with a vignette.
        window = Box("Inventory Screen", canvasRoot.transform, 0, 0, 1440, 900, new Color(.01f, .02f, .03f, .92f));
        SurvivalUITheme.Stretch(window.GetComponent<RectTransform>());
        windowFade = window.AddComponent<CanvasGroup>();
        var shader = Shader.Find("WinterPrototype/UI/FrostedBackdrop");
        if (shader != null) { backdrop = new Material(shader); backdrop.SetFloat("_BlurSize", 7); backdrop.SetColor("_Tint", new Color(.008f, .014f, .022f, .98f)); window.GetComponent<Image>().material = backdrop; }
        SurvivalUITheme.Stretch(Box("Backdrop Shade", window.transform, 0, 0, 0, 0, new Color(.004f, .01f, .018f, .9f)).GetComponent<RectTransform>());
        var vignette = Box("Vignette", window.transform, 0, 0, 0, 0, Color.white); SurvivalUITheme.Stretch(vignette.GetComponent<RectTransform>());
        var vi = vignette.GetComponent<Image>(); vi.sprite = SurvivalUITheme.VignetteSprite; vi.raycastTarget = false;
        var layout = new GameObject("Inventory Layout", typeof(RectTransform)); layout.transform.SetParent(window.transform, false);
        var lr = layout.GetComponent<RectTransform>(); lr.anchorMin = lr.anchorMax = Vector2.one * .5f; lr.sizeDelta = new Vector2(1440, 900);
        var p = layout.transform;

        // Header.
        SurvivalUITheme.Diamond(p, 72, 66, 10, SurvivalUITheme.Frost);
        SurvivalUITheme.Text(p, "FIELD EQUIPMENT", 92, 62, 400, 18, 11, false, SurvivalUITheme.Frost);
        var heading = SurvivalUITheme.Text(p, "INVENTORY", 69, 80, 700, 70, 62, true); SurvivalUITheme.Track(heading, .06f);
        SurvivalUITheme.Text(p, "Carry only what matters.", 73, 150, 700, 22, 14, false, muted);
        HintButton(p, "TAB", "CLOSE", 1368, 108, () => SetOpen(false));
        SurvivalUITheme.Line(p, 72, 190, 1296, new Color(.62f, .80f, .90f, .30f));

        // Field status sidebar.
        var sidebar = Frame("Field Status", p, 72, 212, 174, 612);
        Eyebrow(sidebar, "LOADOUT", 20, 22);
        SurvivalUITheme.Text(sidebar, "SURVIVAL\nPACK", 19, 44, 140, 70, 31, true).lineSpacing = .9f;
        SurvivalUITheme.Line(sidebar, 20, 124, 134);
        Eyebrow(sidebar, "CARRY WEIGHT", 20, 142);
        weight = SurvivalUITheme.Text(sidebar, "", 20, 162, 146, 26, 19, true);
        carryFill = SurvivalUITheme.Bar(sidebar, "Carry load", 20, 196, CarryWidth, 4, SurvivalUITheme.Frost, out _);
        SurvivalUITheme.Text(sidebar, "Travel light.\nLeave room for supplies.", 20, 214, 140, 44, 12, false, muted);
        SurvivalUITheme.Line(sidebar, 20, 290, 134);
        Eyebrow(sidebar, "CONDITIONS", 20, 308);
        conditionValues = SurvivalUITheme.Text(sidebar, "", 20, 330, 150, 22, 13, true, SurvivalUITheme.Frost);
        inventoryWarmth = SurvivalUITheme.Text(sidebar, "WARMTH   0%", 20, 370, 150, 16, 10, false, SurvivalUITheme.Snow);
        inventoryWarmthFill = SurvivalUITheme.Bar(sidebar, "Warmth", 20, 390, WarmthWidth, 4, SurvivalUITheme.Frost, out _);
        SurvivalUITheme.Text(sidebar, "HUNGER   100%", 20, 412, 150, 16, 10, false, muted);
        SurvivalUITheme.Bar(sidebar, "Hunger", 20, 432, WarmthWidth, 4, new Color(.62f, .84f, .95f, .75f), out _).rectTransform.sizeDelta = new Vector2(WarmthWidth, 4);
        SurvivalUITheme.Text(sidebar, "THIRST   100%", 20, 454, 150, 16, 10, false, muted);
        SurvivalUITheme.Bar(sidebar, "Thirst", 20, 474, WarmthWidth, 4, new Color(.62f, .84f, .95f, .75f), out _).rectTransform.sizeDelta = new Vector2(WarmthWidth, 4);
        SurvivalUITheme.Text(sidebar, "Hunger and thirst are\nnot tracked yet.", 20, 500, 140, 34, 10, false, SurvivalUITheme.Slate);

        // Backpack grid.
        var pack = Frame("Backpack", p, 266, 212, 612, 484);
        Section(pack, "BACKPACK", 24, 20);
        itemCount = SurvivalUITheme.Text(pack, "", 360, 24, 228, 16, 10, false, muted, TextAnchor.MiddleRight);
        SurvivalUITheme.Line(pack, 24, 54, 564);
        for (int n = 0; n < 20; n++) cells[n] = Slot(pack, 24 + n % 5 * 114, 72 + n / 5 * 100, 106, 90, n, false);

        var quick = Frame("Inventory Quickbar", p, 266, 716, 612, 108);
        Section(quick, "QUICK ACCESS", 24, 12);
        SurvivalUITheme.Text(quick, "DRAG AN ITEM HERE TO ASSIGN", 300, 16, 288, 14, 9, false, SurvivalUITheme.Slate, TextAnchor.MiddleRight);
        for (int n = 0; n < 6; n++) keys[n] = Slot(quick, 24 + n * 95, 40, 88, 56, n, true);

        // Inspect panel with a spotlight behind the rotating model.
        var inspect = Frame("Item Inspect", p, 898, 212, 470, 612);
        Section(inspect, "ITEM INSPECT", 25, 20);
        SurvivalUITheme.Line(inspect, 25, 54, 420);
        title = SurvivalUITheme.Text(inspect, "", 25, 70, 420, 42, 34, true);
        SurvivalUITheme.Text(inspect, "FIELD EQUIPMENT", 26, 112, 418, 14, 9, false, SurvivalUITheme.Frost);
        var light = new GameObject("Spotlight", typeof(RectTransform), typeof(Image)); light.transform.SetParent(inspect, false);
        SurvivalUITheme.Place(light.GetComponent<RectTransform>(), 45, 130, 380, 290);
        spotlight = light.GetComponent<Image>(); spotlight.sprite = SurvivalUITheme.GlowSprite; spotlight.raycastTarget = false;
        var floor = Box("Floor shadow", inspect, 135, 372, 200, 26, new Color(0, 0, 0, .45f)).GetComponent<Image>(); floor.sprite = SurvivalUITheme.GlowSprite; floor.raycastTarget = false;
        var view = new GameObject("Item Model Preview", typeof(RectTransform), typeof(RawImage), typeof(InventoryPreviewDrag)); view.transform.SetParent(inspect, false);
        Rect(view.GetComponent<RectTransform>(), 95, 138, 280, 250);
        preview = view.GetComponent<RawImage>(); view.GetComponent<InventoryPreviewDrag>().owner = this;
        inspectionHint = SurvivalUITheme.Text(inspect, "DRAG TO ROTATE", 25, 400, 420, 14, 9, false, SurvivalUITheme.Slate, TextAnchor.MiddleCenter);
        emptyInspect = new GameObject("Empty Inspect", typeof(RectTransform)); emptyInspect.transform.SetParent(inspect, false); Rect(emptyInspect.GetComponent<RectTransform>(), 95, 188, 280, 192);
        var mark = SurvivalUITheme.Diamond(emptyInspect.transform, 126, 40, 28, new Color(.40f, .50f, .58f, .6f));
        SurvivalUITheme.Text(emptyInspect.transform, "Choose an item\nfrom your backpack.", 0, 100, 280, 44, 13, false, muted, TextAnchor.MiddleCenter);
        description = SurvivalUITheme.Text(inspect, "", 25, 426, 420, 54, 13, false, muted); description.lineSpacing = 1.12f;
        attributeRoot = new GameObject("Selected Item Attributes", typeof(RectTransform)).transform; attributeRoot.SetParent(inspect, false); Rect((RectTransform)attributeRoot, 0, 492, 470, 111);

        // Footer: status on the left, controls on the right.
        status = SurvivalUITheme.Text(p, "", 266, 846, 620, 20, 12, false, muted);
        float fx = 1368;
        fx = Hint(p, "TAB", "CLOSE", fx, 842);
        fx = Hint(p, "1–6", "ASSIGN", fx - 28, 842);
        Hint(p, "DRAG", "MOVE", fx - 28, 842);

        BuildQuickbar();

        var ghost = new GameObject("Dragged Item", typeof(RectTransform), typeof(RawImage)); ghost.transform.SetParent(canvasRoot.transform, false);
        dragGhost = ghost.GetComponent<RawImage>(); dragGhost.raycastTarget = false; dragGhost.color = new Color(1, 1, 1, .85f); dragGhost.rectTransform.anchorMin = dragGhost.rectTransform.anchorMax = Vector2.one * .5f; dragGhost.rectTransform.sizeDelta = new Vector2(84, 84); ghost.SetActive(false);
        window.SetActive(false);
    }

    // The in-game quickbar: six separate glass slots, bottom centre.
    void BuildQuickbar()
    {
        const float size = 56, gap = 8, width = size * 6 + gap * 5;
        hud = new GameObject("Gameplay Quickbar", typeof(RectTransform), typeof(CanvasGroup)); hud.transform.SetParent(canvasRoot.transform, false); hudFade = hud.GetComponent<CanvasGroup>(); hudFade.alpha = 0;
        var hr = hud.GetComponent<RectTransform>(); hr.anchorMin = hr.anchorMax = new Vector2(.5f, 0); hr.pivot = new Vector2(.5f, 0); hr.anchoredPosition = new Vector2(0, 26); hr.sizeDelta = new Vector2(width, size);
        for (int n = 0; n < 6; n++)
        {
            var slot = Slot(hud.transform, n * (size + gap), 0, size, size, n, true, true);
            gameplayKeys[n] = slot;
            slot.caption.gameObject.SetActive(false);
            Rect(slot.icon.rectTransform, 9, 9, size - 18, size - 18);
        }
        // Selected item's name, shown briefly above the bar.
        var callout = new GameObject("Quick item name", typeof(RectTransform), typeof(CanvasGroup)); callout.transform.SetParent(hud.transform, false);
        var cr = callout.GetComponent<RectTransform>(); cr.anchorMin = cr.anchorMax = new Vector2(.5f, 0); cr.pivot = new Vector2(.5f, 0); cr.sizeDelta = new Vector2(420, 26); cr.anchoredPosition = new Vector2(0, 92);
        quickNameGroup = callout.GetComponent<CanvasGroup>(); quickNameGroup.alpha = 0; quickNameGroup.blocksRaycasts = false;
        var scrim = Box("Name scrim", callout.transform, -40, -14, 500, 54, Color.white).GetComponent<Image>(); scrim.sprite = SurvivalUITheme.OvalScrimSprite; scrim.color = new Color(1, 1, 1, .55f); scrim.raycastTarget = false;
        quickName = SurvivalUITheme.Text(callout.transform, "ITEM", 0, 0, 420, 26, 17, true, SurvivalUITheme.Snow, TextAnchor.MiddleCenter); SurvivalUITheme.Track(quickName, .12f); SurvivalUITheme.Soften(quickName, .6f, 1);
    }

    void RefreshAttributes(InventoryItem item)
    {
        attributes.Clear();
        if (item != null) item.GetInspectAttributes(attributes);
        attributeRoot.gameObject.SetActive(item != null);
        for (int n = 0; n < attributes.Count; n++)
        {
            if (n >= attributeRows.Count)
            {
                var root = new GameObject("Attribute Row", typeof(RectTransform)); root.transform.SetParent(attributeRoot, false);
                var label = SurvivalUITheme.Text(root.transform, "LABEL", 25, 6, 206, 16, 10, false, muted);
                var value = SurvivalUITheme.Text(root.transform, "", 234, 2, 211, 22, 17, true, SurvivalUITheme.Snow, TextAnchor.MiddleRight);
                var bar = SurvivalUITheme.Bar(root.transform, "Attribute", 25, 27, AttributeWidth, 3, orange, out var track);
                attributeRows.Add(new InspectRow { root = root, label = label, value = value, bar = bar, track = track });
            }
            var row = attributeRows[n]; var attribute = attributes[n];
            row.root.SetActive(true); row.root.name = attribute.Label;
            Rect(row.root.GetComponent<RectTransform>(), 0, n * 36, 470, 36);
            row.label.text = attribute.Label.ToUpperInvariant(); row.value.text = attribute.Value;
            bool hasBar = attribute.Fraction >= 0;
            row.bar.gameObject.SetActive(hasBar); row.track.gameObject.SetActive(hasBar);
            row.bar.rectTransform.sizeDelta = new Vector2(AttributeWidth * Mathf.Clamp01(attribute.Fraction), 3);
        }
        for (int n = attributes.Count; n < attributeRows.Count; n++) attributeRows[n].root.SetActive(false);
    }

    InventorySlotUI Slot(Transform p, float x, float y, float w, float h, int index, bool quick, bool rail = false)
    {
        var image = SurvivalUITheme.Surface(p, (quick ? "Quick " : "Slot ") + (index + 1), x, y, w, h, !rail);
        var go = image.gameObject;
        var slot = go.AddComponent<InventorySlotUI>(); slot.owner = this; slot.index = index; slot.quick = quick; slot.rail = rail;
        slot.selection = SurvivalUITheme.Surface(go.transform, "Selection wash", 0, 0, w, h, true); slot.selection.sprite = SurvivalUITheme.SoftSelectionSprite; slot.selection.color = Color.clear; slot.selection.raycastTarget = false;
        slot.stroke = SurvivalUITheme.Stroke(go.transform, Color.clear);
        slot.empty = SurvivalUITheme.Text(go.transform, "", 0, 0, w, h, 16, false, SurvivalUITheme.Slate, TextAnchor.MiddleCenter);
        var icon = new GameObject("Item Thumbnail", typeof(RectTransform), typeof(RawImage)); icon.transform.SetParent(go.transform, false);
        float size = Mathf.Min(w - 20, h - 26); Rect(icon.GetComponent<RectTransform>(), (w - size) / 2, 6, size, size);
        slot.icon = icon.GetComponent<RawImage>(); slot.icon.raycastTarget = false;
        slot.caption = SurvivalUITheme.Text(go.transform, "", 5, h - 18, w - 10, 14, 9, false, SurvivalUITheme.Mist, TextAnchor.MiddleCenter);
        slot.caption.horizontalOverflow = HorizontalWrapMode.Overflow;
        if (quick) slot.numberLabel = SurvivalUITheme.Text(go.transform, (index + 1).ToString(), 7, 4, 16, 14, 10, true, SurvivalUITheme.Slate);
        // Selected marker: a short frost bar under the slot, with a glow.
        var marker = SurvivalUITheme.Surface(go.transform, "Selected marker", w * .5f - 12, h + (rail ? 6 : 3), 24, 3, true); marker.sprite = SurvivalUITheme.PillSprite; marker.color = Color.clear;
        slot.marker = marker;
        if (!rail) marker.gameObject.SetActive(false);
        slot.Paint();
        return slot;
    }

    Transform Frame(string name, Transform p, float x, float y, float w, float h) => SurvivalUITheme.Surface(p, name, x, y, w, h).transform;
    void Section(Transform p, string text, float x, float y)
    {
        SurvivalUITheme.Diamond(p, x, y + 6, 8, SurvivalUITheme.Frost, false);
        var t = SurvivalUITheme.Text(p, text, x + 16, y, 320, 22, 17, true, SurvivalUITheme.Snow); SurvivalUITheme.Track(t, .10f);
    }
    void Eyebrow(Transform p, string text, float x, float y) => SurvivalUITheme.Text(p, text, x, y, 150, 14, 9, false, SurvivalUITheme.Frost);

    // Key cap + label, right-aligned at 'right'; returns the left edge.
    float Hint(Transform p, string key, string label, float right, float y)
    {
        var l = SurvivalUITheme.Text(p, label, 0, y + 6, 120, 16, 10, false, SurvivalUITheme.Mist);
        float lw = l.preferredWidth + label.Length * 1.6f;
        l.rectTransform.anchoredPosition = new Vector2(right - lw, -(y + 6));
        var k = SurvivalUITheme.Key(p, key, 0, y, 26);
        var kr = (RectTransform)k.transform.parent; kr.anchoredPosition = new Vector2(right - lw - 8 - kr.sizeDelta.x, -y);
        return right - lw - 8 - kr.sizeDelta.x;
    }
    void HintButton(Transform p, string key, string label, float right, float y, UnityEngine.Events.UnityAction action)
    {
        float left = Hint(p, key, label, right, y);
        var hit = Box("Close button", p, left - 8, y - 6, right - left + 16, 38, new Color(1, 1, 1, 0));
        var b = hit.AddComponent<Button>(); b.onClick.AddListener(action);
        var hover = SurvivalUITheme.Surface(hit.transform, "Hover", 0, 0, right - left + 16, 38, true); hover.sprite = SurvivalUITheme.SoftSelectionSprite; hover.color = new Color(.62f, .84f, .95f, 0); hover.raycastTarget = false;
        var cols = b.colors; b.targetGraphic = hover; cols.normalColor = new Color(1, 1, 1, 0); cols.highlightedColor = new Color(1, 1, 1, .12f); cols.pressedColor = new Color(1, 1, 1, .2f); cols.selectedColor = cols.normalColor; b.colors = cols;
        hover.color = new Color(.62f, .84f, .95f, 1);
    }
    GameObject Box(string name, Transform p, float x, float y, float w, float h, Color color)
    { var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(p, false); Rect(go.GetComponent<RectTransform>(), x, y, w, h); go.GetComponent<Image>().color = color; return go; }
    static void Rect(RectTransform r, float x, float y, float w, float h) { SurvivalUITheme.Place(r, x, y, w, h); }
    void OnDisable() { if (!IsOpen) return; if (window && hud) SetOpen(false); else { IsOpen = false; if (movement) movement.enabled = priorMovement; Cursor.visible = priorCursor; Cursor.lockState = priorLock; } }
    void OnDestroy()
    {
        if (inventory != null) inventory.Changed -= Refresh;
        if (canvasRoot != null) Destroy(canvasRoot); if (ownedEvents != null) Destroy(ownedEvents);
        if (backdrop != null) Destroy(backdrop);
    }
}

public sealed class InventorySlotUI : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IEndDragHandler, IDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    public InventoryUI owner; public int index; public bool quick, rail;
    public Image selection, stroke, marker; public RawImage icon; public Text caption, empty, numberLabel;
    InventoryItem item;
    bool selected, hovered;
    float changedAt = -10;

    // Idle: a quiet well. Hover: a faint frost wash. Selected: frost outline, wash
    // and the marker bar; the quickbar slot also lifts slightly.
    public void Paint()
    {
        var frost = SurvivalUITheme.Frost;
        stroke.color = selected ? new Color(frost.r, frost.g, frost.b, .9f) : hovered ? new Color(frost.r, frost.g, frost.b, .35f) : new Color(frost.r, frost.g, frost.b, rail ? .08f : 0f);
        selection.color = selected ? new Color(frost.r, frost.g, frost.b, .06f) : hovered ? new Color(frost.r, frost.g, frost.b, .03f) : Color.clear;
        if (marker) marker.color = selected ? frost : Color.clear;
        if (numberLabel != null) numberLabel.color = selected ? SurvivalUITheme.Snow : SurvivalUITheme.Slate;
        if (caption) caption.color = selected ? SurvivalUITheme.Snow : SurvivalUITheme.Mist;
    }
    void LateUpdate()
    {
        if (!rail) return;
        float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.unscaledTime - changedAt) / .18f));
        transform.localScale = Vector3.one * (selected ? Mathf.Lerp(1f, 1.08f, t) : Mathf.Lerp(1.08f, 1f, t));
    }
    public void OnPointerEnter(PointerEventData e) { hovered = owner.IsOpen; Paint(); }
    public void OnPointerExit(PointerEventData e) { hovered = false; Paint(); }
    public void Show(InventoryItem value, bool selected, Texture texture, Color orange, Color muted)
    {
        item = value; icon.texture = texture; icon.color = value != null && texture != null ? Color.white : Color.clear;
        empty.text = value == null && !quick ? "·" : ""; caption.text = value != null ? value.Pickup.displayName.ToUpperInvariant() : "";
        if (this.selected != selected) changedAt = Time.unscaledTime;
        this.selected = selected; Paint();
    }
    int BackpackIndex => quick ? owner.GetComponent<PlayerInventory>().quickbar[index] : index;
    public void OnPointerClick(PointerEventData e) { if (owner.IsOpen) owner.Select(BackpackIndex); else if (quick) owner.GetComponent<PlayerInventory>().SelectQuickSlot(index); }
    public void OnBeginDrag(PointerEventData e) { if (owner.IsOpen && item != null) { owner.BeginDrag(BackpackIndex); owner.DragPointer(e.position); } }
    public void OnDrag(PointerEventData e) { owner.DragPointer(e.position); }
    public void OnEndDrag(PointerEventData e) { owner.EndDrag(); }
    public void OnDrop(PointerEventData e) { owner.DropOn(index, quick); }
}
public sealed class InventoryPreviewDrag : MonoBehaviour, IDragHandler
{
    public InventoryUI owner;
    public void OnDrag(PointerEventData e) { owner.Rotate(e.delta); }
}
