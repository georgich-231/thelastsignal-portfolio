using System.Collections.Generic;
using UnityEngine;

public sealed class RifleItem : InventoryItem
{
    public override void GetInspectAttributes(List<ItemInspectAttribute> attributes)
    {
        base.GetInspectAttributes(attributes);
        attributes.Add(new ItemInspectAttribute("TYPE", "Hunting rifle"));
        attributes.Add(new ItemInspectAttribute("HANDLING", "Two-handed"));
    }
}
