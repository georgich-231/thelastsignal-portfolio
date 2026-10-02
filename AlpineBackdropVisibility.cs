using UnityEngine;
// The dawn chapter occupies a separate terrain outside the main valley.
// Keep decorative mountain meshes from covering that playable scene area.
public sealed class AlpineBackdropVisibility : MonoBehaviour
{
 Renderer[] surfaces;
 ShedNightSequence chapter;
 bool hidden;
 void Start(){surfaces=GetComponentsInChildren<Renderer>();chapter=FindFirstObjectByType<ShedNightSequence>();}
 void LateUpdate(){bool dawn=chapter&&chapter.IsDawn;if(dawn==hidden)return;hidden=dawn;foreach(var r in surfaces)if(r)r.enabled=!hidden;}
}
