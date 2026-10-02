using System.Collections.Generic;
using UnityEngine;

public readonly struct ItemInspectAttribute
{
    public readonly string Label, Value;
    // Negative means this attribute has no progress bar.
    public readonly float Fraction;
    public ItemInspectAttribute(string label, string value, float fraction = -1f)
    { Label = label; Value = value; Fraction = fraction; }
}

// Only weight is universal. Each item type supplies its own inspection attributes and use behavior.
[DisallowMultipleComponent]
public class InventoryItem : MonoBehaviour
{
    [Min(0f)] public float weightKg = .18f;
    public Texture2D inventoryIcon;
    public Texture Icon => inventoryIcon ? inventoryIcon : Thumbnail;
    public RenderTexture Thumbnail { get; private set; }
    public void CaptureThumbnail(RenderTexture source)
    {
        if (Thumbnail != null) return;
        Thumbnail = new RenderTexture(256,256,0,RenderTextureFormat.ARGB32) { name = "Item Thumbnail", filterMode = FilterMode.Bilinear };
        Thumbnail.Create(); Graphics.Blit(source, Thumbnail);
    }
    protected virtual void OnDestroy() { if (Thumbnail != null) { Thumbnail.Release(); Destroy(Thumbnail); } }
    public bool IsStored { get; internal set; }
    public InspectablePickup Pickup => GetComponent<InspectablePickup>();
    public virtual bool IsLit => false;
    public virtual bool CanUse => false;
    public virtual bool HasUseAction => false;
    public virtual string UseLabel => "USE";
    public virtual string UseMessage => "This item has no use action.";
    public virtual bool ToggleUse() => false;
    public virtual void Extinguish() { }
    public virtual void Burn(float seconds) { }
    public virtual void GetInspectAttributes(List<ItemInspectAttribute> attributes)
    { attributes.Add(new ItemInspectAttribute("WEIGHT", $"{weightKg:0.00} kg")); }
}
