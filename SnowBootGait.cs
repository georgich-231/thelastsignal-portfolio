using UnityEngine;

// Temporary boot animation for our primitive character, driven by actual distance.
// Replace this with animation/foot IK when the finished character is rigged.
[DefaultExecutionOrder(20)]
[RequireComponent(typeof(CharacterController))]
public class SnowBootGait : MonoBehaviour
{
    [SerializeField] private Transform leftBoot;
    [SerializeField] private Transform rightBoot;
    [SerializeField, Min(0.1f)] private float stepDistance = 0.57f;
    [SerializeField, Range(0f, 0.5f)] private float footLift = 0.30f;
    [SerializeField, Range(0f, 0.4f)] private float strideReach = 0.23f;
    private Vector3 leftRest, rightRest, previous;
    private Quaternion leftRotation, rightRotation;
    private CharacterController controller;
    private float phase, blend;

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        if (leftBoot == null) leftBoot = transform.Find("Visuals/Left Boot");
        if (rightBoot == null) rightBoot = transform.Find("Visuals/Right Boot");
        if (leftBoot == null || rightBoot == null) { enabled = false; return; }
        leftRest = leftBoot.localPosition; rightRest = rightBoot.localPosition;
        leftRotation = leftBoot.localRotation; rightRotation = rightBoot.localRotation;
        previous = transform.position;
    }

    private void LateUpdate()
    {
        Vector3 motion = transform.position - previous; motion.y = 0;
        previous = transform.position;
        float distance = motion.magnitude;
        bool walking = controller.isGrounded && distance > 0.0001f && distance < 3f;
        if (walking) phase += distance * Mathf.PI / stepDistance;
        blend = Mathf.MoveTowards(blend, walking ? 1f : 0f, Time.deltaTime * 10f);
        Pose(leftBoot, leftRest, leftRotation, phase);
        Pose(rightBoot, rightRest, rightRotation, phase + Mathf.PI);
    }

    private void Pose(Transform boot, Vector3 rest, Quaternion rotation, float angle)
    {
        float swing = Mathf.Sin(angle);
        // Lift the moving boot out of the powder; the planted boot stays buried.
        float lift = Mathf.Pow(Mathf.Max(0f, swing), 0.8f) * footLift;
        boot.localPosition = rest + new Vector3(0, lift, Mathf.Cos(angle) * strideReach) * blend;
        boot.localRotation = rotation * Quaternion.Euler(-swing * 12f * blend, 0, 0);
    }
}
