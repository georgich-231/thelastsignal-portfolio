using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Attach to a capsule with its Capsule Collider removed.
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private Transform movementCamera;
    [SerializeField, Min(0f)] private float walkSpeed = 4f;
    [SerializeField, Min(0f)] private float sprintSpeed = 6f;
    [SerializeField, Min(0f)] private float turnSpeed = 720f;
    [SerializeField, Min(.1f)] private float acceleration = 6f;
    [SerializeField, Min(.1f)] private float braking = 8f;
    [SerializeField] private float gravity = -20f;

    private CharacterController controller;
    private float verticalSpeed;
    private Vector3 planarVelocity;
    private float turningVelocity;
    [SerializeField, Min(.05f)] private float turnSmoothTime=.22f;
    public bool IsJogging { get; private set; }

    private void OnEnable()
    {
        planarVelocity = Vector3.zero;
        verticalSpeed = 0f;
        IsJogging = false;turningVelocity=0;
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (movementCamera == null && Camera.main != null)
            movementCamera = Camera.main.transform;
    }

    private void Update()
    {
        Vector2 input = Vector2.zero;
        bool sprint = false;
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            input.x = ((keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) ? 1f : 0f)
                    - ((keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) ? 1f : 0f);
            input.y = ((keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) ? 1f : 0f)
                    - ((keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) ? 1f : 0f);
            sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        input.x = ((Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) ? 1f : 0f)
                - ((Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) ? 1f : 0f);
        input.y = ((Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) ? 1f : 0f)
                - ((Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) ? 1f : 0f);
        sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
        StepMovement(input, sprint, Time.deltaTime);
    }

    private void StepMovement(Vector2 input, bool sprint, float deltaTime)
    {
        var conditions=GetComponent<PlayerConditions>();
        var rifle=GetComponent<HeldRifle>();bool aiming=rifle && rifle.IsAiming;
        sprint=sprint && !aiming && (conditions==null || conditions.CanSprint);
        IsJogging = sprint && input.sqrMagnitude > .001f;
        // Normalize diagonals so W+D is no faster than W alone.
        input = Vector2.ClampMagnitude(input, 1f);
        Vector3 forward = Vector3.forward;
        if (movementCamera != null)
        {
            forward = Vector3.ProjectOnPlane(movementCamera.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(movementCamera.up, Vector3.up);
            forward.Normalize();
        }
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 direction = right * input.x + forward * input.y;
        float cold=conditions?conditions.ColdSeverity:0;
        float turnAngle=direction.sqrMagnitude>.001f?Vector3.Angle(transform.forward,direction):0;
        float turnPace=Mathf.Lerp(1,.65f,Mathf.InverseLerp(45,150,turnAngle));
        Vector3 targetVelocity = direction * (sprint ? sprintSpeed : walkSpeed) * Mathf.Lerp(1,.88f,cold) * (aiming?.55f:turnPace);
        bool slowing = targetVelocity.sqrMagnitude < planarVelocity.sqrMagnitude
            || Vector3.Dot(planarVelocity, targetVelocity) < 0f;
        planarVelocity = Vector3.MoveTowards(planarVelocity, targetVelocity,
            (slowing ? braking : acceleration) * deltaTime);
        if (aiming || planarVelocity.sqrMagnitude > 0.0025f)
        {
            Quaternion facing = Quaternion.LookRotation(aiming?rifle.AimDirection:planarVelocity, Vector3.up);
            float yaw=Mathf.SmoothDampAngle(transform.eulerAngles.y,facing.eulerAngles.y,ref turningVelocity,turnSmoothTime,Mathf.Min(turnSpeed,240f),deltaTime);
            transform.rotation=Quaternion.Euler(0,yaw,0);
        }

        if (controller.isGrounded && verticalSpeed < 0f)
            verticalSpeed = -2f;
        verticalSpeed += gravity * deltaTime;
        Vector3 velocity = planarVelocity;
        velocity.y = verticalSpeed;
        CollisionFlags collisions = controller.Move(velocity * deltaTime);
        if ((collisions & CollisionFlags.Below) != 0 && verticalSpeed < 0f)
            verticalSpeed = -2f;
        if ((collisions & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
            verticalSpeed = 0f;
    }
}
