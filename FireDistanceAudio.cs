using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public sealed class FireDistanceAudio : MonoBehaviour
{
    public Transform listenerTarget;
    [Min(0)] public float fullVolumeDistance = 1.5f;
    [Min(1)] public float silentDistance = 13f;
    [Range(0,1)] public float volume = .10f;
    AudioSource source;
    void Awake()
    {
        source = GetComponent<AudioSource>();
        GameAudioSettings.Route(source);
        if (!listenerTarget) listenerTarget = GameObject.Find("Player")?.transform;
        source.rolloffMode = AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(0,1,1,1));
        source.maxDistance = 1000;
    }
    public float GainAtDistance(float distance) => 1f - Mathf.SmoothStep(0,1,Mathf.InverseLerp(fullVolumeDistance, Mathf.Max(fullVolumeDistance+.1f,silentDistance),distance));
    void LateUpdate()
    {
        if (!listenerTarget) return;
        source.volume = volume * GainAtDistance(Vector3.Distance(listenerTarget.position,transform.position));
    }
}
