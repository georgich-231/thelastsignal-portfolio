using UnityEngine;

// State lives on the original item, so dropping never resets fuel or duplicates it.
[DisallowMultipleComponent]
public sealed class LighterItem : InventoryItem
{

    [Min(1f)] public float fuelCapacitySeconds = 2400f;
    [Min(0f)] public float fuelSeconds = 2400f;
    [Range(0f, 100f)] public float condition = 100f;
    [Min(0f)] public float conditionLossPerMinute = .125f;
    bool lit;
    float earliestExtinguish;
    public override bool IsLit => lit;


    public override bool CanUse => condition > 0f && fuelSeconds > 0f;
    void SetLit(bool value)
    {
        if(lit==value)return;
        lit=value;
        if(value)earliestExtinguish=Time.time+2f;
        FindFirstObjectByType<SurvivalAudio>()?.Lighter(value);
    }
    public override bool ToggleUse() { if(IsLit && Time.time<earliestExtinguish)return true;SetLit(!IsLit && CanUse); return IsLit; }
    public override void Extinguish() { SetLit(false); }
    public override void Burn(float seconds)
    {
        if (!IsLit || seconds <= 0f) return;
        float consumed = Mathf.Min(seconds, fuelSeconds);
        fuelSeconds = Mathf.Max(0f, fuelSeconds - consumed);
        condition = Mathf.Max(0f, condition - consumed * conditionLossPerMinute / 60f);
        if (!CanUse) SetLit(false);
    }
    public bool TryIgnite()
    {
        if(condition <= 0 || fuelSeconds < 1f) return false;
        fuelSeconds -= 1f;
        condition = Mathf.Max(0, condition - conditionLossPerMinute / 60f);
        return true;
    }
    public override bool HasUseAction => true;
    public override string UseLabel => IsLit ? "EXTINGUISH" : "LIGHT";
    public override string UseMessage => IsLit ? "Lighter lit" : !CanUse ? (condition <= 0 ? "Lighter is worn out" : "Lighter is out of fuel") : "Lighter extinguished";
    public override void GetInspectAttributes(System.Collections.Generic.List<ItemInspectAttribute> attributes)
    {
        base.GetInspectAttributes(attributes);
        attributes.Add(new ItemInspectAttribute("CONDITION", $"{condition:0}%"));
        int secs = Mathf.CeilToInt(fuelSeconds);
        float fraction = fuelSeconds / Mathf.Max(1f, fuelCapacitySeconds);
        attributes.Add(new ItemInspectAttribute("FUEL REMAINING", $"{secs / 60:00}:{secs % 60:00}  /  {fraction * 100:0}%", fraction));
    }
    // Returns the amount accepted. Future fuel containers must subtract this amount.
    public float Refill(float seconds)
    {
        if (seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return 0f;
        float accepted = Mathf.Min(seconds, Mathf.Max(0f, fuelCapacitySeconds - fuelSeconds));
        fuelSeconds += accepted;
        return accepted;
    }
}



