using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Camera))]
public class TopDownCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 12f, -9f);
    [SerializeField, Min(0.01f)] private float smoothTime = 0.15f;
    [Header("Zoom")]
    [Tooltip("Starting zoom, also restored by the middle mouse button.")]
    [SerializeField, Min(0.1f)] private float viewSize = 7f;
    [SerializeField, Min(0.1f)] private float minViewSize = 4f;
    [SerializeField, Min(0.1f)] private float maxViewSize = 8.5f;
    [Tooltip("Proportional zoom per scroll notch. Smaller values feel gentler.")]
    [SerializeField, Range(0.01f, 0.4f)] private float zoomSensitivity = 0.12f;
    [SerializeField, Min(0.01f)] private float zoomSmoothTime = 0.18f;
    private Vector3 followVelocity;
    private Camera view;
    private float targetViewSize;
    private float zoomVelocity;
    private BuildingInterior activeInterior;
    private float exteriorViewSize;
    private Transform encounterTarget;
    private float encounterSize;
    public void FrameEncounter(Transform subject,float size){encounterTarget=subject;encounterSize=size;}
    public void EndEncounter(){encounterTarget=null;UpdateInteriorZoom();}
    public void SnapToPlayer()
    {
        if(!target)return;
        encounterTarget=null;followVelocity=Vector3.zero;zoomVelocity=0;
        UpdateInteriorZoom();transform.position=target.position+offset;
        if(view)view.orthographicSize=targetViewSize;
        SetAngle();ClampViewToMap();
    }
    public void RelocateView(Vector2 minimum,Vector2 maximum)
    {mapMinimum=minimum;mapMaximum=maximum;followVelocity=Vector3.zero;activeInterior=null;targetViewSize=exteriorViewSize;transform.position=target.position+offset;SetAngle();}
    [Header("Opening map limits")]
    [SerializeField] private bool clampToMap = true;
    [SerializeField] private Vector2 mapMinimum = new Vector2(-76f, -76f);
    [SerializeField] private Vector2 mapMaximum = new Vector2(76f, 24f);
    [SerializeField] private float lowestGround = -1f;

    private void Start()
    {
        view = GetComponent<Camera>();
        view.orthographic = true;
        targetViewSize = Mathf.Clamp(viewSize, minViewSize, maxViewSize);
        exteriorViewSize = targetViewSize;
        view.orthographicSize = targetViewSize;
        if (target != null)
            transform.position = target.position + offset;
        SetAngle();
        ClampViewToMap();
    }

    private void Update()
    {
        if(PauseMenu.IsOpen)return;
        if(encounterTarget){targetViewSize=encounterSize;return;}
        UpdateInteriorZoom();
        if (activeInterior != null) return;

        // Scrolling an inventory/menu should not change the world camera.
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
        float scroll = 0f;
        bool reset = false;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            scroll = Mouse.current.scroll.ReadValue().y;
            // Input System 1.20 normally normalizes each notch to 1 already.
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange)
                scroll /= 120f;
#endif
            reset = Mouse.current.middleButton.wasPressedThisFrame;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        scroll = Input.mouseScrollDelta.y;
        reset = Input.GetMouseButtonDown(2);
#endif
        RequestZoom(scroll, reset);
    }

    private void RequestZoom(float scroll, bool reset)
    {
        if (activeInterior != null) return;
        if (reset)
            targetViewSize = Mathf.Clamp(viewSize, minViewSize, maxViewSize);
        else if (Mathf.Abs(scroll) > 0.0001f)
            // Proportional steps feel consistent at both close and wide views.
            // Scroll is an event delta: multiplying it by frame time loses input.
            targetViewSize = Mathf.Clamp(targetViewSize * Mathf.Exp(-Mathf.Clamp(scroll, -20f, 20f) * zoomSensitivity),
                minViewSize, maxViewSize);
    }

    private void StepZoom(float deltaTime)
    {
        view.orthographicSize = Mathf.SmoothDamp(view.orthographicSize, targetViewSize,
            ref zoomVelocity, zoomSmoothTime, Mathf.Infinity, deltaTime);
        float allowedMinimum = activeInterior != null
            ? Mathf.Min(minViewSize, activeInterior.interiorViewSize)
            : minViewSize;
        view.orthographicSize = Mathf.Clamp(view.orthographicSize, allowedMinimum, maxViewSize);
    }

    private void UpdateInteriorZoom()
    {
        BuildingInterior containing = BuildingInterior.FindContaining(target);
        if (containing == activeInterior)
        {
            if (activeInterior != null)
                targetViewSize = activeInterior.interiorViewSize;
            return;
        }

        if (activeInterior == null && containing != null)
            exteriorViewSize = Mathf.Clamp(targetViewSize, minViewSize, maxViewSize);

        activeInterior = containing;
        targetViewSize = activeInterior != null
            ? activeInterior.interiorViewSize
            : Mathf.Clamp(exteriorViewSize, minViewSize, maxViewSize);
        zoomVelocity = 0f;
    }

    private void LateUpdate()
    {
        if(PauseMenu.IsOpen)return;
        StepZoom(Time.unscaledDeltaTime);
        if (target == null) return;
        transform.position = Vector3.SmoothDamp(transform.position,
            (encounterTarget?encounterTarget.position:target.position) + offset, ref followVelocity, encounterTarget?.38f:smoothTime);
        SetAngle();
        ClampViewToMap();
    }

    // Clamp the whole projected viewport, including zoom and aspect changes.
    // Clamping after smoothing also prevents a single exposed-edge frame.
    private void ClampViewToMap()
    {
        if (!clampToMap || !view) return;
        var plane = new Plane(Vector3.up, new Vector3(0, lowestGround, 0));
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        for (int i=0;i<4;i++)
        {
            Ray ray=view.ViewportPointToRay(new Vector3(i%2,i/2,0));
            if(!plane.Raycast(ray,out float distance))return;
            Vector3 p=ray.GetPoint(distance);Vector2 point=new Vector2(p.x,p.z);
            min=Vector2.Min(min,point);max=Vector2.Max(max,point);
        }
        Vector2 available=mapMaximum-mapMinimum, span=max-min;
        float fit=Mathf.Min(available.x/span.x,available.y/span.y);
        if(fit<1)
        {
            view.orthographicSize*=fit*.999f;targetViewSize=Mathf.Min(targetViewSize,view.orthographicSize);
            ClampViewToMap();return;
        }
        float dx=min.x<mapMinimum.x?mapMinimum.x-min.x:max.x>mapMaximum.x?mapMaximum.x-max.x:0;
        float dz=min.y<mapMinimum.y?mapMinimum.y-min.y:max.y>mapMaximum.y?mapMaximum.y-max.y:0;
        transform.position+=new Vector3(dx,0,dz);
        if(dx!=0)followVelocity.x=0;if(dz!=0)followVelocity.z=0;
    }

    private void OnValidate()
    {
        minViewSize = Mathf.Max(0.1f, minViewSize);
        maxViewSize = Mathf.Max(minViewSize, maxViewSize);
        viewSize = Mathf.Clamp(viewSize, minViewSize, maxViewSize);
    }

    private void SetAngle()
    {
        // A fixed world-space angle prevents rotation when the player turns.
        if (offset.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(-offset, Vector3.up);
    }
}
