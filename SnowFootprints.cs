using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class SnowFootprints : MonoBehaviour
{
    [SerializeField] private DeformableSnow snow;
    [SerializeField, Min(0.1f)] private float stepDistance = 0.57f;
    [SerializeField, Min(0f)] private float footSeparation = 0.18f;
    [SerializeField, Min(0.1f)] private float bootWidth = 0.32f;
    [SerializeField, Min(0.1f)] private float bootLength = 0.56f;
    private CharacterController controller;
    private Vector3 previous;
    private float distanceSinceStep;
    private bool leftFoot;
    private bool hasPlanted;
    private DeformableSnow[] snowFields;
    [Header("Animated boot contacts")]
    [SerializeField] private Transform characterVisual;
    [SerializeField] private Transform leftBoot;
    [SerializeField] private Transform rightBoot;
    [SerializeField] private float ankleHeight = .16f;
    [SerializeField] private float soleForwardOffset = .07f;
    private readonly bool[] airborne = new bool[2];
    private CharacterLocomotionBalance balance;

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        balance = GetComponent<CharacterLocomotionBalance>();
        snowFields = FindObjectsByType<DeformableSnow>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (snow == null) snow = SnowAt(transform.position);
        previous = transform.position;
        if (!characterVisual) characterVisual=transform.Find("Arctic Character");
        if(characterVisual)
            foreach(var bone in characterVisual.GetComponentsInChildren<Transform>())
            {
                if(bone.name=="Foot.L")leftBoot=bone;
                if(bone.name=="Foot.R")rightBoot=bone;
            }
    }

    private void LateUpdate()
    {
        Vector3 current = transform.position;
        Vector3 delta = current - previous;
        delta.y = 0;
        float distance = delta.magnitude;
        snow = SnowAt(current);
        if(characterVisual && leftBoot && rightBoot)
        {
            // Evaluate actual deformed boot positions after Animator evaluation.
            // Arm once during swing, then stamp once when that boot comes down.
            if(snow && distance<3f)
            {
                for(int index=0;index<2;index++)
                {
                    var boot=index==0?leftBoot:rightBoot;
                    float lift=balance && balance.IsReady ? balance.FootLift(index) : characterVisual.InverseTransformPoint(boot.position).y-ankleHeight;
                    if(lift>.035f)airborne[index]=true;
                    if(lift<.018f && (airborne[index] || !hasPlanted) && controller.isGrounded)
                    {
                        var foot=boot.position+transform.forward*soleForwardOffset;
                        foot.y=controller.bounds.min.y;
                        snow.Stamp(foot,transform.forward,bootWidth,bootLength);
                        airborne[index]=false;
                    }
                }
                if(controller.isGrounded)hasPlanted=true;
            }
            else {hasPlanted=false;airborne[0]=airborne[1]=false;}
            previous=current;
            return;
        }
        if (snow == null || !controller.isGrounded || distance > 3f)
        {
            previous = current;
            distanceSinceStep = 0;
            hasPlanted = false;
            return;
        }
        if (!hasPlanted)
        {
            Vector3 planted = current; planted.y = controller.bounds.min.y;
            Vector3 side = transform.right * footSeparation;
            bool a = snow.Stamp(planted - side, transform.forward, bootWidth, bootLength);
            bool b = snow.Stamp(planted + side, transform.forward, bootWidth, bootLength);
            hasPlanted = a || b;
        }
        if (distance > 0.0001f)
        {
            Vector3 forward = delta / distance;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float travelled = stepDistance - distanceSinceStep;
            // Place evenly spaced prints along actual motion, including low-FPS frames.
            while (travelled <= distance)
            {
                Vector3 foot = Vector3.Lerp(previous, current, travelled / distance);
                foot.y = controller.bounds.min.y;
                foot += right * (leftFoot ? -footSeparation : footSeparation);
                if (snow.Stamp(foot, forward, bootWidth, bootLength)) leftFoot = !leftFoot;
                travelled += stepDistance;
            }
            distanceSinceStep = (distanceSinceStep + distance) % stepDistance;
        }
        previous = current;
    }

    private DeformableSnow SnowAt(Vector3 worldPosition)
    {
        if (Contains(snow, worldPosition)) return snow;
        if (snowFields == null || snowFields.Length == 0)
            snowFields = FindObjectsByType<DeformableSnow>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var field in snowFields)
            if (Contains(field, worldPosition)) return field;
        return null;
    }

    private static bool Contains(DeformableSnow field, Vector3 worldPosition)
    {
        if (!field) return false;
        var terrain = field.GetComponent<Terrain>();
        if (!terrain || !terrain.terrainData) return false;
        Vector3 local = worldPosition - terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        return local.x >= 0f && local.z >= 0f && local.x <= size.x && local.z <= size.z;
    }
}
