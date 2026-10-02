using UnityEngine;
using UnityEngine.Audio;

[DefaultExecutionOrder(-200)]
public sealed class GameAudioSettings : MonoBehaviour
{
    public static GameAudioSettings Instance { get; private set; }
    public float Master { get; private set; }
    public float Music { get; private set; }
    public float Effects { get; private set; }
    AudioMixer mixer; AudioMixerGroup musicGroup,effectsGroup;
    float nextRoute,focusDuck;
    void Awake()
    {
        Instance=this;mixer=Resources.Load<AudioMixer>("OpeningAudio");
        musicGroup=mixer.FindMatchingGroups("Music")[0];effectsGroup=mixer.FindMatchingGroups("Effects")[0];
        Master=Mathf.Clamp01(PlayerPrefs.GetFloat("Audio.Master",1));
        Music=Mathf.Clamp01(PlayerPrefs.GetFloat("Audio.Music",1));
        Effects=Mathf.Clamp01(PlayerPrefs.GetFloat("Audio.Effects",1));
    }
    void Start(){Apply();RouteSources();}
    void Update(){if(Time.unscaledTime>=nextRoute){nextRoute=Time.unscaledTime+.5f;RouteSources();}}
    void RouteSources()
    {
        foreach(var source in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            if(!source.outputAudioMixerGroup)source.outputAudioMixerGroup=source.name=="Opening melody"?musicGroup:effectsGroup;
    }
    public static void Route(AudioSource source,bool music=false)
    {
        if(Instance)source.outputAudioMixerGroup=music?Instance.musicGroup:Instance.effectsGroup;
    }
    public void SetMaster(float value){Master=Mathf.Clamp01(value);Apply();}
    public void SetMusic(float value){Music=Mathf.Clamp01(value);Apply();}
    public void SetEffects(float value){Effects=Mathf.Clamp01(value);Apply();}
    void Apply()
    {
        mixer.SetFloat("MasterVolume",Decibels(Master));mixer.SetFloat("MusicVolume",Decibels(Music*Mathf.Lerp(1,.20f,focusDuck)));mixer.SetFloat("EffectsVolume",Decibels(Effects*Mathf.Lerp(1,.28f,focusDuck)));
        PlayerPrefs.SetFloat("Audio.Master",Master);PlayerPrefs.SetFloat("Audio.Music",Music);PlayerPrefs.SetFloat("Audio.Effects",Effects);
    }
    public void SetFocusDuck(float amount)
    {
        focusDuck=Mathf.Clamp01(amount);if(!mixer)return;
        mixer.SetFloat("MusicVolume",Decibels(Music*Mathf.Lerp(1,.20f,focusDuck)));mixer.SetFloat("EffectsVolume",Decibels(Effects*Mathf.Lerp(1,.28f,focusDuck)));
    }
    static float Decibels(float value)=>value<=.001f?-80:20*Mathf.Log10(value);
    public void Save()=>PlayerPrefs.Save();
    void OnDestroy(){if(Instance==this){Save();Instance=null;}}
}
