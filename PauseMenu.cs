using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

// Pause: the world frosted behind a vignette; the game's title and PAUSED on the
// left over large text-only menu items; the current objective on a glass card to
// the right. Options open as a tabbed panel with chevron selectors and sliders.
[DefaultExecutionOrder(-100)]
public sealed class PauseMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    public static int EscapeHandledFrame { get; private set; } = -1;
    GameObject canvasRoot, home, options, ownedEvents, audioTab, graphicsTab;
    readonly System.Collections.Generic.List<System.Action> selectors = new();
    CanvasGroup group; RectTransform card, objectiveCard, objectiveFooter; Text context;
    PauseMenuItem audioItem, graphicsItem;
    PlayerMovement movement; OpeningSequence opening; InventoryUI inventory;
    bool priorMovement, priorCursor; CursorLockMode priorLock; float priorTime, progress;
    Material backdrop;
    const float Left = 128;

    void Awake() { IsOpen = false; movement = GetComponent<PlayerMovement>(); opening = GetComponent<OpeningSequence>(); inventory = GetComponent<InventoryUI>(); Build(); }
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            EscapeHandledFrame = Time.frameCount;
            if (IsOpen) { if (options.activeSelf) ShowOptions(false); else Resume(); }
            else if (inventory && inventory.IsOpen) inventory.SetOpen(false);
            else if (opening && opening.IsReading) opening.CloseNote();
            else Open();
        }
        progress = Mathf.MoveTowards(progress, IsOpen ? 1 : 0, Time.unscaledDeltaTime * 5);
        group.alpha = Mathf.SmoothStep(0, 1, progress); card.anchoredPosition = new Vector2(Mathf.Lerp(-18, 0, group.alpha), 0);
    }
    public void Open()
    {
        if (IsOpen) return;
        if (!EventSystem.current) { ownedEvents = new GameObject("Pause Event System", typeof(EventSystem), typeof(InputSystemUIInputModule)); }
        priorTime = Time.timeScale; priorMovement = movement.enabled; priorCursor = Cursor.visible; priorLock = Cursor.lockState;
        IsOpen = true; Time.timeScale = 0; movement.enabled = false; Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
        group.blocksRaycasts = true; group.interactable = true; ShowOptions(false);
        context.text = opening ? opening.CurrentObjectiveText : "Follow the trail.";
        // The card hugs its objective: short ones do not leave an empty slab of glass.
        float h = Mathf.Max(36, context.preferredHeight);
        context.rectTransform.sizeDelta = new Vector2(432, h);
        objectiveCard.sizeDelta = new Vector2(492, 62 + h + 64);
        objectiveFooter.anchoredPosition = new Vector2(0, -(62 + h + 14));
    }
    public void Resume()
    {
        if (!IsOpen) return; IsOpen = false; Time.timeScale = priorTime; movement.enabled = priorMovement;
        Cursor.visible = priorCursor; Cursor.lockState = priorLock; group.blocksRaycasts = false; group.interactable = false;
        GameAudioSettings.Instance?.Save(); GameGraphicsSettings.Instance?.Save(); EventSystem.current?.SetSelectedGameObject(null);
    }
    public void ShowOptions(bool show) { home.SetActive(!show); options.SetActive(show); if (show) { ShowTab(false); RefreshSelectors(); } }
    public void ShowTab(bool graphics)
    {
        audioTab.SetActive(!graphics); graphicsTab.SetActive(graphics);
        if (audioItem) audioItem.Active = !graphics; if (graphicsItem) graphicsItem.Active = graphics;
        if (graphics) RefreshSelectors();
    }

    // ------------------------------------------------------------------ rows

    const float RowX = 40, RowW = 700, ControlX = 450;
    void BuildGraphicsTab(Transform parent)
    {
        var g = GameGraphicsSettings.Ensure();
        float y = 96;
        SelectorRow(parent, "QUALITY", "Shadows, detail distance, fog and effects", y,
            () => GameGraphicsSettings.PresetNames[(int)g.Quality],
            d => g.SetQuality((GameGraphicsSettings.Preset)Mathf.Clamp((int)g.Quality + d, 0, 3)));
        SelectorRow(parent, "ANTI-ALIASING", "TAA is smoothest; SMAA is sharper in motion", y += 66,
            () => GameGraphicsSettings.AntiAliasingNames[(int)g.AA],
            d => g.SetAntiAliasing(g.AA == GameGraphicsSettings.AntiAliasing.Temporal ? GameGraphicsSettings.AntiAliasing.Smaa : GameGraphicsSettings.AntiAliasing.Temporal));
        SelectorRow(parent, "SHARPENING", "Restores crisp edges on distant detail", y += 66,
            () => GameGraphicsSettings.SharpenNames[g.Sharpen],
            d => g.SetSharpen(Mathf.Clamp(g.Sharpen + d, 0, 3)));
        SelectorRow(parent, "RESOLUTION", "Render size of the game window", y += 66,
            () => { var r = g.Resolutions[g.ResolutionIndex()]; return r.x + " × " + r.y; },
            d => g.SetResolution(g.Resolutions[Mathf.Clamp(g.ResolutionIndex() + d, 0, g.Resolutions.Count - 1)]));
        SelectorRow(parent, "DISPLAY MODE", "Fullscreen, borderless or windowed", y += 66,
            () => GameGraphicsSettings.DisplayModeNames[(int)g.Mode],
            d => g.SetDisplayMode((GameGraphicsSettings.DisplayMode)(((int)g.Mode + d + 3) % 3)));
        SelectorRow(parent, "VSYNC", "Match the monitor refresh rate", y += 66,
            () => g.VSync ? "ON" : "OFF",
            d => g.SetVSync(!g.VSync));
    }
    void RowFrame(Transform parent, string title, string hint, float y)
    {
        SurvivalUITheme.Line(parent, RowX, y + 53, RowW, new Color(.62f, .80f, .90f, .14f));   // quiet separator under each row
        var t = SurvivalUITheme.Text(parent, title, RowX, y, 380, 24, 20, true); SurvivalUITheme.Track(t, .08f);
        SurvivalUITheme.Text(parent, hint, RowX, y + 25, 400, 16, 12, false, SurvivalUITheme.Mist);
    }
    void SelectorRow(Transform parent, string title, string hint, float y, System.Func<string> value, System.Action<int> step)
    {
        RowFrame(parent, title, hint, y);
        var pill = SurvivalUITheme.Surface(parent, title + " value", ControlX + 44, y + 1, 162, 38, true);
        var label = SurvivalUITheme.Text(pill.transform, "VALUE", 0, 0, 162, 38, 17, true, SurvivalUITheme.Frost, TextAnchor.MiddleCenter); label.text = value();
        selectors.Add(() => label.text = value());
        Chevron(parent, false, ControlX, y + 1, () => { step(-1); RefreshSelectors(); });
        Chevron(parent, true, ControlX + 212, y + 1, () => { step(1); RefreshSelectors(); });
    }
    void Chevron(Transform parent, bool right, float x, float y, UnityEngine.Events.UnityAction action)
    {
        var surface = SurvivalUITheme.Surface(parent, right ? "Next" : "Previous", x, y, 38, 38, true);
        var button = surface.gameObject.AddComponent<Button>(); button.targetGraphic = surface;
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.6f, 1.9f, 2.1f); colors.selectedColor = Color.white; colors.pressedColor = new Color(.7f, .9f, 1); colors.fadeDuration = .12f; button.colors = colors;
        SurvivalUITheme.Text(surface.transform, right ? "›" : "‹", 0, -2, 38, 38, 26, true, SurvivalUITheme.Snow, TextAnchor.MiddleCenter);
        button.onClick.AddListener(action);
    }
    void RefreshSelectors() { foreach (var refresh in selectors) refresh(); }

    void VolumeRow(Transform parent, string title, string hint, float y, float initial, UnityEngine.Events.UnityAction<float> change)
    {
        RowFrame(parent, title, hint, y);
        const float w = 214;
        var number = SurvivalUITheme.Text(parent, Mathf.RoundToInt(initial * 100) + "%", ControlX + w + 16, y + 9, 64, 22, 17, true, SurvivalUITheme.Frost, TextAnchor.MiddleLeft);
        var root = new GameObject(title + " volume", typeof(RectTransform), typeof(Slider)); root.transform.SetParent(parent, false); SurvivalUITheme.Place(root.GetComponent<RectTransform>(), ControlX + 6, y + 6, w, 28);
        var rail = SurvivalUITheme.Surface(root.transform, "Rail", 0, 12, w, 4, true); rail.sprite = SurvivalUITheme.PillSprite; rail.color = new Color(.35f, .45f, .52f, .45f);
        var fillArea = new GameObject("Fill area", typeof(RectTransform)); fillArea.transform.SetParent(root.transform, false); SurvivalUITheme.Place(fillArea.GetComponent<RectTransform>(), 0, 12, w, 4);
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)); fill.transform.SetParent(fillArea.transform, false); SurvivalUITheme.Stretch(fill.GetComponent<RectTransform>());
        var fi = fill.GetComponent<Image>(); fi.sprite = SurvivalUITheme.PillSprite; fi.type = Image.Type.Sliced; fi.color = SurvivalUITheme.Frost;
        var handleArea = new GameObject("Handle area", typeof(RectTransform)); handleArea.transform.SetParent(root.transform, false); SurvivalUITheme.Stretch(handleArea.GetComponent<RectTransform>());
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)); handle.transform.SetParent(handleArea.transform, false);
        var hr = handle.GetComponent<RectTransform>(); hr.sizeDelta = new Vector2(16, 0); hr.anchorMin = new Vector2(0, .5f); hr.anchorMax = new Vector2(0, .5f);
        var glow = SurvivalUITheme.Glow(handle.transform, new Color(.62f, .84f, .95f, .45f), 38);
        var knob = new GameObject("Knob", typeof(RectTransform), typeof(Image)); knob.transform.SetParent(handle.transform, false);
        var kr = knob.GetComponent<RectTransform>(); kr.anchorMin = kr.anchorMax = kr.pivot = Vector2.one * .5f; kr.sizeDelta = new Vector2(16, 16);
        var ki = knob.GetComponent<Image>(); ki.sprite = SurvivalUITheme.PillSprite; ki.type = Image.Type.Sliced; ki.color = SurvivalUITheme.Snow;
        hr.sizeDelta = new Vector2(16, -12);   // the slider stretches the handle over the 28 px area
        var slider = root.GetComponent<Slider>(); slider.fillRect = fill.GetComponent<RectTransform>(); slider.handleRect = hr; slider.targetGraphic = ki; slider.minValue = 0; slider.maxValue = 1; slider.SetValueWithoutNotify(initial);
        var sc = slider.colors; sc.highlightedColor = new Color(.85f, .95f, 1f); sc.pressedColor = SurvivalUITheme.Frost; slider.colors = sc;
        slider.onValueChanged.AddListener(v => { number.text = Mathf.RoundToInt(v * 100) + "%"; change(v); });
    }

    // ------------------------------------------------------------------ build

    void Build()
    {
        canvasRoot = new GameObject("Pause interface", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup)); var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2200;
        var scaler = canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1440, 900); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        group = canvasRoot.GetComponent<CanvasGroup>(); group.alpha = 0; group.blocksRaycasts = false; group.interactable = false;

        // Veil: frosted world, darker toward the menu side, vignetted.
        var veil = Full(canvasRoot.transform, "Winter veil", new Color(.006f, .013f, .022f, .94f));
        var shader = Shader.Find("WinterPrototype/UI/FrostedBackdrop");
        if (shader != null) { backdrop = new Material(shader); backdrop.SetFloat("_BlurSize", 7); backdrop.SetColor("_Tint", new Color(.006f, .012f, .02f, .97f)); veil.material = backdrop; }
        Full(canvasRoot.transform, "Winter shade", new Color(.004f, .009f, .016f, .82f));
        var side = Full(canvasRoot.transform, "Menu side", Color.white); side.sprite = SurvivalUITheme.ScrimSprite; side.color = new Color(1, 1, 1, .75f);
        var vig = Full(canvasRoot.transform, "Vignette", Color.white); vig.sprite = SurvivalUITheme.VignetteSprite;

        var cardGO = new GameObject("Pause layout", typeof(RectTransform)); cardGO.transform.SetParent(canvasRoot.transform, false);
        card = cardGO.GetComponent<RectTransform>(); card.anchorMin = card.anchorMax = Vector2.one * .5f; card.pivot = Vector2.one * .5f; card.sizeDelta = new Vector2(1440, 900);
        var c = card.transform;

        // Title block.
        SurvivalUITheme.Diamond(c, Left, 150, 10, SurvivalUITheme.Frost);
        SurvivalUITheme.Text(c, StoryWriting.GameTitle.ToUpperInvariant(), Left + 20, 146, 700, 18, 11, false, SurvivalUITheme.Frost);
        var paused = SurvivalUITheme.Text(c, "PAUSED", Left - 4, 168, 700, 100, 96, true); SurvivalUITheme.Track(paused, .08f); SurvivalUITheme.Soften(paused, .5f, 2);
        SurvivalUITheme.Line(c, Left, 280, 420, new Color(.62f, .84f, .95f, .35f));

        home = Panel(c, "Pause home"); options = Panel(c, "Options");

        // Home: menu on the left, objective card on the right.
        PauseMenuItem.Create(home.transform, "RESUME", "Return to the forest", Left, 318, Resume);
        PauseMenuItem.Create(home.transform, "OPTIONS", "Audio, graphics and display", Left, 398, () => ShowOptions(true));
        var obj = SurvivalUITheme.Surface(home.transform, "Objective card", 820, 318, 492, 230); objectiveCard = obj.rectTransform;
        SurvivalUITheme.Stroke(obj.transform, new Color(.62f, .80f, .90f, .08f));
        SurvivalUITheme.Diamond(obj.transform, 30, 34, 9, SurvivalUITheme.Frost);
        SurvivalUITheme.Text(obj.transform, "CURRENT OBJECTIVE", 48, 30, 400, 16, 10, false, SurvivalUITheme.Frost);
        context = SurvivalUITheme.Text(obj.transform, "", 30, 62, 432, 110, 30, true, SurvivalUITheme.Snow); context.lineSpacing = 1.02f;
        var footer = new GameObject("Objective footer", typeof(RectTransform)); footer.transform.SetParent(obj.transform, false);
        objectiveFooter = footer.GetComponent<RectTransform>(); SurvivalUITheme.Place(objectiveFooter, 0, 184, 492, 40);
        SurvivalUITheme.Line(footer.transform, 30, 0, 432, new Color(.62f, .80f, .90f, .22f));
        SurvivalUITheme.Text(footer.transform, "The world waits while you are paused.", 30, 12, 432, 18, 12, false, SurvivalUITheme.Mist);

        // Options: section menu left, settings panel right.
        audioItem = PauseMenuItem.Create(options.transform, "AUDIO", "Volume levels", Left, 318, () => ShowTab(false));
        graphicsItem = PauseMenuItem.Create(options.transform, "GRAPHICS", "Quality and display", Left, 398, () => ShowTab(true));
        PauseMenuItem.Create(options.transform, "BACK", "Return to the pause menu", Left, 510, () => ShowOptions(false));
        var sheet = SurvivalUITheme.Surface(options.transform, "Settings sheet", 560, 300, 780, 510);
        SurvivalUITheme.Stroke(sheet.transform, new Color(.62f, .80f, .90f, .08f));
        audioTab = Panel(sheet.transform, "Audio tab"); graphicsTab = Panel(sheet.transform, "Graphics tab");
        TabHeader(audioTab.transform, "AUDIO", "Balance the forest against the story.");
        TabHeader(graphicsTab.transform, "GRAPHICS", "Changes apply immediately.");
        var audio = GameAudioSettings.Instance;
        VolumeRow(audioTab.transform, "MASTER", "Overall game volume", 96, audio.Master, audio.SetMaster);
        VolumeRow(audioTab.transform, "MUSIC", "Background melody and soundtrack", 162, audio.Music, audio.SetMusic);
        VolumeRow(audioTab.transform, "EFFECTS", "Footsteps, wind, fire and interactions", 228, audio.Effects, audio.SetEffects);
        BuildGraphicsTab(graphicsTab.transform);
        ShowTab(false);

        // Footer hints.
        SurvivalUITheme.Line(c, Left, 832, 1184, new Color(.62f, .80f, .90f, .16f));
        float x = 1312;
        x = Hint(c, "ESC", "BACK", x, 846);
        Hint(c, "CLICK", "SELECT", x - 28, 846);
        options.SetActive(false);
    }

    void TabHeader(Transform p, string title, string subtitle)
    {
        SurvivalUITheme.Diamond(p, 40, 36, 8, SurvivalUITheme.Frost, false);
        var t = SurvivalUITheme.Text(p, title, 56, 28, 400, 24, 20, true); SurvivalUITheme.Track(t, .12f);
        SurvivalUITheme.Text(p, subtitle, 40, 56, 600, 16, 12, false, SurvivalUITheme.Mist);
        SurvivalUITheme.Line(p, 40, 80, 700, new Color(.62f, .80f, .90f, .22f));
    }
    float Hint(Transform p, string key, string label, float right, float y)
    {
        var l = SurvivalUITheme.Text(p, label, 0, y + 6, 120, 16, 10, false, SurvivalUITheme.Mist);
        float lw = l.preferredWidth + label.Length * 1.6f; l.rectTransform.anchoredPosition = new Vector2(right - lw, -(y + 6));
        var k = SurvivalUITheme.Key(p, key, 0, y, 26); var kr = (RectTransform)k.transform.parent;
        kr.anchoredPosition = new Vector2(right - lw - 8 - kr.sizeDelta.x, -y);
        return right - lw - 8 - kr.sizeDelta.x;
    }
    GameObject Panel(Transform parent, string name) { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); SurvivalUITheme.Stretch(go.GetComponent<RectTransform>()); return go; }
    Image Full(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); SurvivalUITheme.Stretch(go.GetComponent<RectTransform>());
        var i = go.GetComponent<Image>(); i.color = color; i.raycastTarget = name == "Winter veil"; return i;
    }
    void OnDisable() { if (IsOpen) Resume(); }
    void OnDestroy() { if (canvasRoot) Destroy(canvasRoot); if (ownedEvents) Destroy(ownedEvents); if (backdrop) Destroy(backdrop); }
}

// A large text-only menu entry: on hover (or while its tab is active) it brightens,
// slides right and a frost bar grows in beside it.
public sealed class PauseMenuItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    Text label, hint; RectTransform bar, body; Image barImage;
    System.Action action; bool hovered; float t;
    public bool Active;
    public static PauseMenuItem Create(Transform parent, string title, string subtitle, float x, float y, System.Action onClick)
    {
        var go = new GameObject(title, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        SurvivalUITheme.Place(go.GetComponent<RectTransform>(), x - 24, y, 420, 64);
        go.GetComponent<Image>().color = new Color(0, 0, 0, 0);   // hit area
        var m = go.AddComponent<PauseMenuItem>(); m.action = onClick;
        var b = new GameObject("Bar", typeof(RectTransform), typeof(Image)); b.transform.SetParent(go.transform, false);
        m.bar = b.GetComponent<RectTransform>(); SurvivalUITheme.Place(m.bar, 0, 12, 3, 34);
        m.barImage = b.GetComponent<Image>(); m.barImage.sprite = SurvivalUITheme.PillSprite; m.barImage.type = Image.Type.Sliced; m.barImage.color = SurvivalUITheme.Frost; m.barImage.raycastTarget = false;
        var content = new GameObject("Content", typeof(RectTransform)); content.transform.SetParent(go.transform, false);
        m.body = content.GetComponent<RectTransform>(); SurvivalUITheme.Place(m.body, 24, 0, 400, 64);
        m.label = SurvivalUITheme.Text(content.transform, title, 0, 2, 400, 40, 38, true, SurvivalUITheme.Mist); SurvivalUITheme.Track(m.label, .08f);
        m.hint = SurvivalUITheme.Text(content.transform, subtitle, 2, 44, 400, 16, 12, false, SurvivalUITheme.Slate);
        return m;
    }
    public void OnPointerEnter(PointerEventData e) => hovered = true;
    public void OnPointerExit(PointerEventData e) => hovered = false;
    public void OnPointerClick(PointerEventData e) => action?.Invoke();
    void OnDisable() { hovered = false; t = 0; }
    void Update()
    {
        bool lit = hovered || Active;
        t = Mathf.MoveTowards(t, lit ? 1 : 0, Time.unscaledDeltaTime * 7);
        float s = Mathf.SmoothStep(0, 1, t);
        body.anchoredPosition = new Vector2(24 + 12 * s, 0);
        bar.sizeDelta = new Vector2(3, 34 * s);
        bar.anchoredPosition = new Vector2(0, -12 - 17 * (1 - s));
        barImage.color = new Color(.62f, .84f, .95f, s);
        label.color = Color.Lerp(SurvivalUITheme.Mist, SurvivalUITheme.Snow, s);
        hint.color = Color.Lerp(SurvivalUITheme.Slate, SurvivalUITheme.Mist, s);
    }
}
