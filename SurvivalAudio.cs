using UnityEngine;

[DisallowMultipleComponent]
public sealed class SurvivalAudio : MonoBehaviour
{
    public AudioClip backpack, backpackClose, paper, objective, melody;
    [Range(0,1)] public float musicVolume=.045f;
    AudioSource effects, music, dialogue;
    AudioClip textTick;float nextTextTick;int textTickIndex;
    AudioClip lighterIgnite,lighterExtinguish;
    OpeningSequence opening; InventoryUI inventory;
    public float MusicSuppression { get; set; }
    float lastBackpack=-10, lastObjective=-10;
    void Awake()
    {
        opening=GetComponent<OpeningSequence>();inventory=GetComponent<InventoryUI>();
        effects=Source("Interaction foley");music=Source("Opening melody");
        dialogue=Source("Dialogue lettering");textTick=MakeTextTick();
        music.clip=melody;music.loop=true;music.volume=0;
        if(melody)music.Play();
    }
    AudioSource Source(string name)
    {
        var go=new GameObject(name);go.transform.SetParent(transform,false);
        var source=go.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;GameAudioSettings.Route(source,name=="Opening melody");
        return source;
    }
    void Update()
    {
        float level=musicVolume*(1-Mathf.Clamp01(MusicSuppression));
        if(opening && opening.InPrologue)level*=.45f;
        else if(opening && opening.IsReading)level*=.6f;
        else if(inventory && inventory.IsOpen)level*=.75f;
        music.volume=Mathf.MoveTowards(music.volume,level,Time.unscaledDeltaTime*.035f);
    }
    public void Backpack()
    {
        if(Time.unscaledTime-lastBackpack<.25f)return;
        lastBackpack=Time.unscaledTime;Play(backpack,.36f);
    }
    public void CloseBackpack(){Play(backpackClose?backpackClose:backpack,.30f);}
    public void Lighter(bool lighting)
    {
        if(!lighterIgnite)lighterIgnite=Resources.Load<AudioClip>("Audio/LighterIgnite");
        if(!lighterExtinguish)lighterExtinguish=Resources.Load<AudioClip>("Audio/LighterExtinguish");
        Play(lighting?lighterIgnite:lighterExtinguish,lighting?.12f:.09f);
    }
    public void Paper(bool puttingAway=false) { Play(paper,puttingAway?.23f:.34f); }
    public void Objective()
    {
        if(Time.unscaledTime-lastObjective<.6f)return;
        lastObjective=Time.unscaledTime;Play(objective,.4f);
    }
    void Play(AudioClip clip,float volume)
    {
        if(isActiveAndEnabled && clip && effects && effects.isActiveAndEnabled)effects.PlayOneShot(clip,volume);
    }
    // A soft, short mechanical tick: deliberately quieter than interaction foley.
    static AudioClip MakeTextTick()
    {
        const int rate=44100;var samples=new float[1764];var random=new System.Random(817);
        float smooth=0;
        for(int i=0;i<samples.Length;i++)
        {
            float t=i/(float)rate;smooth=Mathf.Lerp(smooth,(float)random.NextDouble()*2-1,.24f);
            float envelope=Mathf.Min(1,t/.0015f)*Mathf.Exp(-t*180)*Mathf.Clamp01((.04f-t)/.008f);
            samples[i]=(smooth*.6f+Mathf.Sin(t*2*Mathf.PI*620)*.18f)*envelope;
        }
        var clip=AudioClip.Create("Soft dialogue key",samples.Length,1,rate,false);clip.SetData(samples,0);return clip;
    }
    public void TextLetter(char letter)
    {
        if(!char.IsLetterOrDigit(letter)||PauseMenu.IsOpen||inventory.IsOpen||opening.IsReading||Time.unscaledTime<nextTextTick)return;
        if(!dialogue)return;
        if(!textTick)textTick=MakeTextTick();
        nextTextTick=Time.unscaledTime+.055f;
        dialogue.pitch=.94f+(textTickIndex++%5)*.025f;dialogue.PlayOneShot(textTick,.30f);
    }
    void OnDestroy(){if(textTick)Destroy(textTick);}
    void OnDisable(){if(music)music.Stop();if(effects)effects.Stop();if(dialogue)dialogue.Stop();}
}
