using UnityEngine;

public sealed class HouseSearchProgress : MonoBehaviour
{
    public bool NoteRead { get; private set; }
    public bool RifleFound { get; private set; }
    public bool AmmoFound { get; private set; }
    public bool Complete => NoteRead && RifleFound && AmmoFound;
    PlayerInventory pack;
    OpeningSequence story;
    bool announced;
    void Awake() { pack=GetComponent<PlayerInventory>(); story=GetComponent<OpeningSequence>(); }
    public void RecordNoteRead() { NoteRead=true; }
    void Update()
    {
        foreach(var item in pack.slots)
        {
            if(item is RifleItem) RifleFound=true;
            if(item is RifleAmmoItem) AmmoFound=true;
        }
        if(!Complete || announced || story.BlocksGameplay || story.IsSpeaking) return;
        announced=true;
        story.SetNightObjective(OpeningSequence.ChapterStep.FollowEasternBootMarks);
    }
}
