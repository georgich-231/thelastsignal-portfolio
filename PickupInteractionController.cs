using System.Collections.Generic;

using UnityEngine;

using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM

using UnityEngine.InputSystem;

#endif



[DisallowMultipleComponent]

public sealed class PickupInteractionController : MonoBehaviour

{

    [Header("Interaction")]

    [SerializeField] float releaseToZeroTime = 0.32f;

    [SerializeField] float targetScanInterval = 0.08f;

    [SerializeField, Range(0.01f, 0.2f)] float visibilityProbeRadius = 0.055f;

    [SerializeField] Color accent = new Color(1f, 0.43f, 0.12f, 1f);



    InspectablePickup current;

    InspectablePickup inspecting;

    PlayerMovement movement;

    float holdProgress;

    float nextScan;

    bool inspectMode;

    float dismissReadyAt;

    bool previousCursorVisible;

    CursorLockMode previousCursorLock;



    Canvas canvas;

    CanvasGroup promptGroup;

    SurvivalPromptView pickupPrompt;

    Image progressRing;

    Text promptName;

    Text promptVerb;

    GameObject inspectionPanel;

    RawImage itemViewport;

    Text itemTitle;

    Text itemDescription;

    RenderTexture itemTexture;

    Camera itemCamera;

    Transform itemTurntable;

    GameObject itemClone;

    Vector3 lastMousePosition;

    float cameraDistance = 3f;

    Font interfaceFont;

    int inspectionLayer;

    readonly RaycastHit[] visibilityHits = new RaycastHit[32];

    readonly List<Light> inspectionLights = new List<Light>();

    readonly Dictionary<Light, int> savedSceneLightMasks = new Dictionary<Light, int>();



    InventoryItem thumbnailItem;

    bool inventoryPreviewActive;

    int previewRenderCount;

    public int PreviewRenderCount => previewRenderCount;

    void Awake()

    {

        movement = GetComponent<PlayerMovement>();

        inspectionLayer = LayerMask.NameToLayer("InspectionItem");

        if (inspectionLayer < 0) inspectionLayer = 2;

        interfaceFont = Resources.Load<Font>("Fonts/BarlowCondensed-SemiBold");

        if (interfaceFont == null) interfaceFont = Resources.Load<Font>("Fonts/Inter-Variable");

        if (interfaceFont == null) interfaceFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        BuildInterface();

        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += PreviewRendered;

    }



    void OnDisable()

    {

        if (current != null) current.SetFocused(false);

        if (inspectMode) CloseInspection(false);

    }



    public bool IsInspecting => inspectMode;

    void Update()

    {

        if (GetComponent<GameplayInteraction>() != null && GetComponent<GameplayInteraction>().BlocksPickup) { promptGroup.alpha = 0; holdProgress = 0; return; }

        if (GetComponent<InventoryUI>() != null && GetComponent<InventoryUI>().IsOpen) { promptGroup.alpha = 0; return; }

        if (inspectMode)

        {

            UpdateInspection();

            return;

        }



        if (Time.unscaledTime >= nextScan)

        {

            nextScan = Time.unscaledTime + targetScanInterval;

            ScanForTarget();

        }



        bool holding = current != null && InteractHeld();

        if (holding)

            holdProgress = Mathf.MoveTowards(holdProgress, 1f, Time.unscaledDeltaTime / Mathf.Max(0.05f, current.holdDuration));

        else

            holdProgress = Mathf.MoveTowards(holdProgress, 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, releaseToZeroTime));



        float shown = current != null ? 1f : 0f;

        promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, shown, Time.unscaledDeltaTime * 7f);

        promptGroup.interactable = false;

        promptGroup.blocksRaycasts = false;

        progressRing.fillAmount = holdProgress;



        if (current != null)

        {

            pickupPrompt.SetWorldAnchor(current.FocusPoint+Vector3.up*.15f);

            promptName.text = string.IsNullOrEmpty(current.displayName) ? "ITEM" : current.displayName.ToUpperInvariant();

            promptVerb.text = holding ? "Taking item..." : "Hold E to pick up";

            if (holdProgress >= 0.999f)

            {

                var inventory = GetComponent<PlayerInventory>();

                var pickup = current;

                if (inventory != null && inventory.TryCollect(pickup))

                {

                    current = null; holdProgress = 0; promptGroup.alpha = 0;

                    GetComponent<InventoryUI>().OpenItem(pickup.GetComponent<InventoryItem>());

                }

                else { holdProgress = 0; promptVerb.text = inventory != null ? inventory.LastMessage : "INVENTORY UNAVAILABLE"; }

            }

        }

    }



    void ScanForTarget()

    {

        InspectablePickup best = null;

        float bestDistance = float.MaxValue;

        for (int i = InspectablePickup.Active.Count - 1; i >= 0; i--)

        {

            InspectablePickup candidate = InspectablePickup.Active[i];

            if (candidate == null || !candidate.isActiveAndEnabled) continue;

            Vector3 offset = candidate.FocusPoint - transform.position;

            offset.y *= 0.35f;

            float distance = offset.magnitude;

            if (distance <= candidate.interactionDistance && distance < bestDistance && HasClearPath(candidate))

            {

                best = candidate;

                bestDistance = distance;

            }

        }

        if (best == current) return;

        if (current != null) current.SetFocused(false);

        current = best;

        if (current != null) current.SetFocused(true);

        holdProgress = Mathf.Min(holdProgress, 0.18f);

    }



    bool HasClearPath(InspectablePickup candidate)

    {

        Vector3 origin = transform.position + Vector3.up * 0.45f;

        Vector3 destination = candidate.FocusPoint + Vector3.up * 0.12f;

        Vector3 direction = destination - origin;

        float distance = direction.magnitude;

        if (distance <= 0.05f) return true;

        direction /= distance;



        int count = Physics.RaycastNonAlloc(origin, direction,

            visibilityHits, Mathf.Max(0f, distance - 0.025f), ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)

        {

            Collider hit = visibilityHits[i].collider;

            if (hit == null || hit is TerrainCollider) continue;

            Transform hitTransform = hit.transform;

            if (hitTransform == transform || hitTransform.IsChildOf(transform)) continue;

            if (hitTransform == candidate.transform || hitTransform.IsChildOf(candidate.transform)) continue;

            if (hit.GetComponentInParent<CanopyOccluder>() != null) continue;

            return false;

        }

        return true;

    }



    void BeginInspection(InspectablePickup pickup)

    {

        inspectMode = true;

        inspecting = pickup;

        current = null;

        holdProgress = 0f;

        promptGroup.alpha = 0f;

        pickup.SetFocused(false);



        previousCursorVisible = Cursor.visible;

        previousCursorLock = Cursor.lockState;

        Cursor.visible = true;

        Cursor.lockState = CursorLockMode.None;

        if (movement != null) movement.enabled = false;



        CreateInspectionObject(pickup);

        pickup.HideForPresentation();

        itemTitle.text = pickup.displayName;

        itemDescription.text = FormatDescription(pickup.description);

        inspectionPanel.SetActive(true);

        dismissReadyAt = Time.unscaledTime + 0.35f;

    }



    void UpdateInspection()

    {

        if (itemTurntable != null)

        {

            bool dragging = PrimaryPointerHeld();

            Vector2 delta = PointerDelta();

            if (dragging)

            {

                itemTurntable.Rotate(Vector3.up, -delta.x * 0.22f, Space.World);

                itemTurntable.Rotate(itemCamera.transform.right, delta.y * 0.17f, Space.World);

            }

            else

            {

                float keyboardTurn = HorizontalInspectInput();

                itemTurntable.Rotate(Vector3.up, (keyboardTurn * 70f + 5.5f) * Time.unscaledDeltaTime, Space.World);

            }



            float zoom = ScrollInput();

            if (Mathf.Abs(zoom) > 0.001f)

            {

                cameraDistance = Mathf.Clamp(cameraDistance - zoom * 0.12f, 1.8f, 4.8f);

                itemCamera.orthographicSize = cameraDistance;

            }

        }



        if (Time.unscaledTime >= dismissReadyAt && (InteractPressed() || ConfirmPressed()))

            CloseInspection(true);

    }



    void CloseInspection(bool destroyPickup)

    {

        inspectionPanel.SetActive(false);

        if (itemClone != null) Destroy(itemClone);

        if (itemClone != null) foreach (var r in itemClone.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m != null) Destroy(m);

        if (itemTurntable != null) Destroy(itemTurntable.gameObject);

        itemClone = null;

        itemTurntable = null;

        if (itemCamera != null) itemCamera.enabled = false;

        RestoreSceneLighting();



        InspectablePickup finished = inspecting;

        inspecting = null;

        inspectMode = false;

        if (movement != null) movement.enabled = true;

        Cursor.visible = previousCursorVisible;

        Cursor.lockState = previousCursorLock;

        if (destroyPickup && finished != null) Destroy(finished.gameObject);

    }



    public Texture ShowInventoryPreview(InspectablePickup pickup)

    {

        HideInventoryPreview();

        CreateInspectionObject(pickup);

        itemClone.SetActive(true);

        foreach (var r in itemClone.GetComponentsInChildren<Renderer>(true)) r.enabled = true;

        thumbnailItem = pickup.GetComponent<InventoryItem>();

        inventoryPreviewActive = true;

        return itemTexture;

    }

    public void RotateInventoryPreview(float x, float y)

    {

        if (itemTurntable == null) return;

        itemTurntable.Rotate(Vector3.up, -x * .25f, Space.World);

        itemTurntable.Rotate(itemCamera.transform.right, y * .2f, Space.World);

        itemCamera.enabled = true;

        foreach(var light in inspectionLights) if(light != null) light.enabled = true;

    }

    void PreviewRendered(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)

    {

        if (camera != itemCamera || !inventoryPreviewActive) return;

        previewRenderCount++;

        if (thumbnailItem != null) thumbnailItem.CaptureThumbnail(itemTexture);

        itemCamera.enabled = false;

        foreach(var light in inspectionLights) if(light != null) light.enabled = false;

    }

    public void HideInventoryPreview()

    {

        inventoryPreviewActive = false; thumbnailItem = null;

        if (itemClone != null) foreach (var r in itemClone.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m != null) Destroy(m);

        if (itemTurntable != null) Destroy(itemTurntable.gameObject);

        itemTurntable = null; itemClone = null;

        if (itemCamera != null) itemCamera.enabled = false;

        RestoreSceneLighting();

    }

    void OnDestroy()

    {

        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= PreviewRendered;

        HideInventoryPreview();

        if (itemCamera != null) Destroy(itemCamera.gameObject);

        foreach (var l in inspectionLights) if (l != null) Destroy(l.gameObject);

        if (itemTexture != null) { itemTexture.Release(); Destroy(itemTexture); }

        if (canvas != null) Destroy(canvas.gameObject);

    }

    void CreateInspectionObject(InspectablePickup pickup)

    {

        if (itemCamera == null) BuildInspectionStage();

        PrepareInspectionLighting();

        itemCamera.enabled = true;

        cameraDistance = 1.12f;

        itemCamera.orthographicSize = cameraDistance;



        GameObject turntableObject = new GameObject("Inspection Turntable");

        turntableObject.transform.position = new Vector3(0f, -1000f, 0f);

        turntableObject.transform.rotation = Quaternion.Euler(-58f, 24f, -6f);

        itemTurntable = turntableObject.transform;



        itemClone = Instantiate(pickup.PresentationRoot.gameObject, itemTurntable);

        itemClone.name = pickup.displayName + " Presentation";

        itemClone.SetActive(true);

        itemClone.transform.localPosition = Vector3.zero;

        itemClone.transform.localRotation = pickup.PresentationRoot.localRotation;

        itemClone.transform.localScale = pickup.PresentationRoot.lossyScale;

        foreach (InspectablePickup component in itemClone.GetComponentsInChildren<InspectablePickup>(true)) component.enabled = false;

        foreach (Collider component in itemClone.GetComponentsInChildren<Collider>(true)) Destroy(component);

        foreach (LineRenderer component in itemClone.GetComponentsInChildren<LineRenderer>(true)) Destroy(component.gameObject);

        SetLayerRecursively(itemClone, inspectionLayer);



        if (pickup.inspectionMesh != null)

        {

            var filter = itemClone.GetComponentInChildren<MeshFilter>(true);

            if (filter != null) filter.sharedMesh = pickup.inspectionMesh;

        }

        Renderer[] cloneRenderers = itemClone.GetComponentsInChildren<Renderer>(true);

        if (cloneRenderers.Length == 0) return;

        foreach (Renderer cloneRenderer in cloneRenderers)

            foreach (Material cloneMaterial in cloneRenderer.materials)

                if (cloneMaterial != null && cloneMaterial.HasProperty("_EmissionColor"))

                    cloneMaterial.SetColor("_EmissionColor", Color.black);

        Bounds bounds = cloneRenderers[0].bounds;

        for (int i = 1; i < cloneRenderers.Length; i++) bounds.Encapsulate(cloneRenderers[i].bounds);

        itemClone.transform.position += itemTurntable.position - bounds.center;

        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);

        if (largest > 0.001f)

        {

            float scale = 1.75f / largest;

            itemClone.transform.localScale *= scale;

            bounds = cloneRenderers[0].bounds;

            for(int i=1;i<cloneRenderers.Length;i++) bounds.Encapsulate(cloneRenderers[i].bounds);

            itemClone.transform.position += itemTurntable.position - bounds.center;

        }

    }



    void BuildInspectionStage()

    {

        GameObject cameraObject = new GameObject("Pickup Inspection Camera");

        DontDestroyOnLoad(cameraObject);

        itemCamera = cameraObject.AddComponent<Camera>();

        itemCamera.transform.position = new Vector3(0f, -1000f, -6f);

        itemCamera.transform.rotation = Quaternion.identity;

        itemCamera.orthographic = true;

        itemCamera.orthographicSize = cameraDistance;

        itemCamera.clearFlags = CameraClearFlags.SolidColor;

        itemCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);

        itemCamera.cullingMask = 1 << inspectionLayer;

        itemCamera.nearClipPlane = 0.05f;

        itemCamera.farClipPlane = 20f;

        itemCamera.allowHDR = false;

        itemCamera.allowMSAA = true;

        itemCamera.depth = -20f;

        itemTexture = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32);

        itemTexture.name = "Inspection 1024 - on demand";

        itemTexture.antiAliasing = 4;

        itemTexture.filterMode = FilterMode.Bilinear;

        itemTexture.Create();

        itemCamera.targetTexture = itemTexture;

        itemViewport.texture = itemTexture;



        CreateStageLight("Inspection Key", new Vector3(38f, -32f, 0f), new Color(1f, 0.8f, 0.68f), 1.25f, false);

        CreateStageLight("Inspection Fill", new Vector3(24f, 142f, 0f), new Color(0.5f, 0.68f, 1f), 0.7f, false);

        CreateStageLight("Inspection Rim", new Vector3(-28f, 196f, 0f), new Color(1f, 0.38f, 0.12f), 0.42f, false);

    }



    void CreateStageLight(string objectName, Vector3 rotation, Color color, float intensity, bool castShadows)

    {

        GameObject lightObject = new GameObject(objectName);

        DontDestroyOnLoad(lightObject);

        lightObject.layer = inspectionLayer;

        lightObject.transform.position = new Vector3(0f, -1000f, 0f);

        lightObject.transform.rotation = Quaternion.Euler(rotation);

        Light stageLight = lightObject.AddComponent<Light>();

        stageLight.type = LightType.Directional;

        stageLight.color = color;

        stageLight.intensity = intensity;

        stageLight.cullingMask = 1 << inspectionLayer;

        stageLight.shadows = LightShadows.None;

        stageLight.enabled = false;

        inspectionLights.Add(stageLight);

    }



    void PrepareInspectionLighting()

    {

        savedSceneLightMasks.Clear();

        foreach (Light sceneLight in FindObjectsByType<Light>(FindObjectsInactive.Exclude))

        {

            if (sceneLight == null || inspectionLights.Contains(sceneLight)) continue;

            savedSceneLightMasks[sceneLight] = sceneLight.cullingMask;

            sceneLight.cullingMask &= ~(1 << inspectionLayer);

        }

        foreach (Light inspectionLight in inspectionLights)

            if (inspectionLight != null) inspectionLight.enabled = true;

    }



    void RestoreSceneLighting()

    {

        foreach (KeyValuePair<Light, int> pair in savedSceneLightMasks)

            if (pair.Key != null) pair.Key.cullingMask = pair.Value;

        savedSceneLightMasks.Clear();

        foreach (Light inspectionLight in inspectionLights)

            if (inspectionLight != null) inspectionLight.enabled = false;

    }



    void BuildInterface()

    {

        GameObject canvasObject = new GameObject("Pickup Interface", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        canvas = canvasObject.GetComponent<Canvas>();

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        canvas.sortingOrder = 480;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        scaler.referenceResolution = new Vector2(1440f, 900f);

        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;



        BuildWorldPrompt(canvasObject.transform);

        BuildInspectionInterface(canvasObject.transform);

    }



    void BuildWorldPrompt(Transform parent)

    {

        var view=SurvivalPromptView.Create(parent,"Interaction Prompt"); pickupPrompt=view;

        view.Category.text="PICKUP  /  FIELD EQUIPMENT";

        promptGroup=view.Group; promptName=view.Title; promptVerb=view.Detail; progressRing=view.Progress;

    }



    void BuildInspectionInterface(Transform parent)

    {

        // Acquired-item presentation: the world frosted and vignetted, the item turning
        // under a soft spotlight, its name set large at the top left, a glass card
        // describing its use, and key-cap controls along the bottom right.
        inspectionPanel = Panel("Item Inspection", parent, Color.white);
        Stretch(inspectionPanel.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
        Image background = inspectionPanel.GetComponent<Image>();
        Shader blurShader = Shader.Find("WinterPrototype/UI/FrostedBackdrop");
        if (blurShader != null) background.material = new Material(blurShader);
        else background.color = new Color(0.018f, 0.025f, 0.035f, 0.94f);
        GameObject shade = Panel("Inspection shade", inspectionPanel.transform, new Color(0.004f, 0.01f, 0.018f, 0.8f));
        Stretch(shade.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f); shade.GetComponent<Image>().raycastTarget = false;
        GameObject vignette = Panel("Inspection vignette", inspectionPanel.transform, Color.white);
        Stretch(vignette.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
        vignette.GetComponent<Image>().sprite = SurvivalUITheme.VignetteSprite; vignette.GetComponent<Image>().raycastTarget = false;

        GameObject viewportBounds = new GameObject("Inspection Viewport Bounds", typeof(RectTransform));
        viewportBounds.transform.SetParent(inspectionPanel.transform, false);
        RectTransform boundsRect = viewportBounds.GetComponent<RectTransform>();
        boundsRect.anchorMin = new Vector2(0.22f, 0.12f);
        boundsRect.anchorMax = new Vector2(0.9f, 0.88f);
        boundsRect.offsetMin = boundsRect.offsetMax = Vector2.zero;
        Image spot = SurvivalUITheme.Glow(viewportBounds.transform, new Color(0.62f, 0.84f, 0.95f, 0.16f), 760f);
        spot.rectTransform.anchorMin = spot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        GameObject viewportObject = new GameObject("Crisp Item View", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(AspectRatioFitter));
        viewportObject.transform.SetParent(viewportBounds.transform, false);
        itemViewport = viewportObject.GetComponent<RawImage>();
        itemViewport.color = Color.white;
        RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
        viewportRect.anchorMin = viewportRect.anchorMax = new Vector2(0.5f, 0.5f);
        viewportRect.pivot = new Vector2(0.5f, 0.5f);
        viewportRect.anchoredPosition = Vector2.zero;
        AspectRatioFitter viewportAspect = viewportObject.GetComponent<AspectRatioFitter>();
        viewportAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        viewportAspect.aspectRatio = 1f;
        viewportBounds.transform.SetSiblingIndex(2);

        // Title block, top left.
        GameObject titleBlock = new GameObject("Inspection Title", typeof(RectTransform));
        titleBlock.transform.SetParent(inspectionPanel.transform, false);
        RectTransform titleRect = titleBlock.GetComponent<RectTransform>();
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 1f); titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(64f, -56f); titleRect.sizeDelta = new Vector2(760f, 160f);
        SurvivalUITheme.Diamond(titleBlock.transform, 0, 8, 11, SurvivalUITheme.Frost);
        SurvivalUITheme.Text(titleBlock.transform, "ACQUIRED  ·  FIELD EQUIPMENT", 22, 3, 600, 18, 12, false, SurvivalUITheme.Frost);
        itemTitle = SurvivalUITheme.Text(titleBlock.transform, "FIELD LIGHTER", 0, 28, 760, 80, 68, true, SurvivalUITheme.Snow);
        SurvivalUITheme.Track(itemTitle, 0.05f); SurvivalUITheme.Soften(itemTitle, 0.5f, 2f);
        SurvivalUITheme.Line(titleBlock.transform, 0, 114, 360, new Color(0.62f, 0.84f, 0.95f, 0.4f));

        // Use card, bottom left.
        Image card = SurvivalUITheme.Surface(inspectionPanel.transform, "Description Panel", 0, 0, 620f, 176f);
        RectTransform descriptionRect = card.rectTransform;
        descriptionRect.anchorMin = descriptionRect.anchorMax = new Vector2(0f, 0f); descriptionRect.pivot = Vector2.zero;
        descriptionRect.anchoredPosition = new Vector2(64f, 64f);
        SurvivalUITheme.Stroke(card.transform, new Color(0.62f, 0.80f, 0.90f, 0.08f));
        Image rail = SurvivalUITheme.Surface(card.transform, "Accent rail", 0, 22, 2, 44, true); rail.sprite = SurvivalUITheme.PillSprite; rail.color = SurvivalUITheme.Frost;
        SurvivalUITheme.Text(card.transform, "USE", 28, 22, 300, 18, 12, false, SurvivalUITheme.Frost);
        SurvivalUITheme.Line(card.transform, 28, 46, 560, new Color(0.62f, 0.80f, 0.90f, 0.22f));
        itemDescription = SurvivalUITheme.Text(card.transform, "", 28, 60, 564, 100, 17, false, SurvivalUITheme.Snow);
        itemDescription.lineSpacing = 1.15f;

        // Controls, bottom right.
        GameObject controls = new GameObject("Inspection Controls", typeof(RectTransform));
        controls.transform.SetParent(inspectionPanel.transform, false);
        RectTransform controlsRect = controls.GetComponent<RectTransform>();
        controlsRect.anchorMin = controlsRect.anchorMax = new Vector2(1f, 0f); controlsRect.pivot = new Vector2(1f, 0f);
        controlsRect.anchoredPosition = new Vector2(-64f, 64f); controlsRect.sizeDelta = new Vector2(640f, 40f);
        float right = 640f;
        right = ControlHint(controls.transform, "E", "CONTINUE", right);
        right = ControlHint(controls.transform, "SCROLL", "ZOOM", right - 30f);
        ControlHint(controls.transform, "DRAG", "ROTATE", right - 30f);
        inspectionPanel.SetActive(false);

    }



    float ControlHint(Transform parent, string key, string label, float right)
    {
        Text l = SurvivalUITheme.Text(parent, label, 0, 11, 140, 18, 11, false, SurvivalUITheme.Mist);
        float lw = l.preferredWidth + label.Length * 1.8f; l.rectTransform.anchoredPosition = new Vector2(right - lw, -11f);
        Text k = SurvivalUITheme.Key(parent, key, 0, 4, 30); RectTransform kr = (RectTransform)k.transform.parent;
        kr.anchoredPosition = new Vector2(right - lw - 10f - kr.sizeDelta.x, -4f);
        return right - lw - 10f - kr.sizeDelta.x;
    }
    GameObject Panel(string objectName, Transform parent, Color color)

    {

        GameObject panel = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

        panel.transform.SetParent(parent, false);

        panel.GetComponent<Image>().color = color;

        return panel;

    }



    void ApplyTextureSprite(Image image, string resourcePath)

    {

        Texture2D texture = Resources.Load<Texture2D>(resourcePath);

        if (texture == null) return;

        image.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),

            new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);

        image.color = Color.white;

        image.type = Image.Type.Simple;

        image.preserveAspect = false;

    }



    static string FormatDescription(string description)

    {

        if (string.IsNullOrWhiteSpace(description)) return string.Empty;

        int sentenceEnd = description.IndexOf(". ", System.StringComparison.Ordinal);

        if (sentenceEnd < 0) return description;

        return description.Substring(0, sentenceEnd + 1) + "\n" + description.Substring(sentenceEnd + 2);

    }



    Text Label(string objectName, Transform parent, string text, int size, FontStyle style, Color color, TextAnchor alignment)

    {

        GameObject labelObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));

        labelObject.transform.SetParent(parent, false);

        Text label = labelObject.GetComponent<Text>();

        label.font = interfaceFont;

        label.text = text;

        label.fontSize = size;

        label.fontStyle = style;

        label.color = color;

        label.alignment = alignment;

        label.raycastTarget = false;

        return label;

    }



    Sprite CreateRingSprite()

    {

        const int size = 128;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);

        texture.name = "Hold Ring";

        Color[] pixels = new Color[size * size];

        Vector2 center = Vector2.one * (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)

            for (int x = 0; x < size; x++)

            {

                float distance = Vector2.Distance(new Vector2(x, y), center);

                float alpha = Mathf.Clamp01(1f - Mathf.Abs(distance - 53f) / 3.2f);

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);

            }

        texture.SetPixels(pixels);

        texture.Apply(false, true);

        return Sprite.Create(texture, new Rect(0f, 0f, size, size), Vector2.one * 0.5f, 100f);

    }



    static void Stretch(RectTransform rect, float left, float right, float bottom, float top)

    {

        rect.anchorMin = Vector2.zero;

        rect.anchorMax = Vector2.one;

        rect.offsetMin = new Vector2(left, bottom);

        rect.offsetMax = new Vector2(-right, -top);

    }



    static void SetRect(RectTransform rect, Vector2 position, Vector2 size, Vector2 anchorMin, Vector2 anchorMax)

    {

        rect.anchorMin = anchorMin;

        rect.anchorMax = anchorMax;

        rect.pivot = new Vector2(0f, 0.5f);

        rect.anchoredPosition = position;

        rect.sizeDelta = size;

    }



    static void SetLayerRecursively(GameObject root, int layer)

    {

        root.layer = layer;

        foreach (Transform child in root.transform) SetLayerRecursively(child.gameObject, layer);

    }



    static bool InteractHeld()

    {

#if ENABLE_INPUT_SYSTEM

        return Keyboard.current != null && Keyboard.current.eKey.isPressed;

#elif ENABLE_LEGACY_INPUT_MANAGER

        return Input.GetKey(KeyCode.E);

#else

        return false;

#endif

    }



    static bool InteractPressed()

    {

#if ENABLE_INPUT_SYSTEM

        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;

#elif ENABLE_LEGACY_INPUT_MANAGER

        return Input.GetKeyDown(KeyCode.E);

#else

        return false;

#endif

    }



    static bool ConfirmPressed()

    {

#if ENABLE_INPUT_SYSTEM

        return Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);

#elif ENABLE_LEGACY_INPUT_MANAGER

        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

#else

        return false;

#endif

    }



    static bool PrimaryPointerHeld()

    {

#if ENABLE_INPUT_SYSTEM

        return Mouse.current != null && Mouse.current.leftButton.isPressed;

#elif ENABLE_LEGACY_INPUT_MANAGER

        return Input.GetMouseButton(0);

#else

        return false;

#endif

    }



    static Vector2 PointerDelta()

    {

#if ENABLE_INPUT_SYSTEM

        return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;

#elif ENABLE_LEGACY_INPUT_MANAGER

        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 9f;

#else

        return Vector2.zero;

#endif

    }



    static float ScrollInput()

    {

#if ENABLE_INPUT_SYSTEM

        return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;

#elif ENABLE_LEGACY_INPUT_MANAGER

        return Input.mouseScrollDelta.y;

#else

        return 0f;

#endif

    }



    static float HorizontalInspectInput()

    {

#if ENABLE_INPUT_SYSTEM

        if (Keyboard.current == null) return 0f;

        return (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed ? 1f : 0f)

             - (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed ? 1f : 0f);

#elif ENABLE_LEGACY_INPUT_MANAGER

        return (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)

             - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);

#else

        return 0f;

#endif

    }

}









