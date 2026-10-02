using System.Collections.Generic;
using UnityEngine;

public sealed class RifleAmmoItem : InventoryItem
{
    [Min(1)] public int rounds = 12;
    public override void GetInspectAttributes(List<ItemInspectAttribute> attributes)
    {
        base.GetInspectAttributes(attributes);
        attributes.Add(new ItemInspectAttribute("CONTENTS", rounds + " cartridges"));
        attributes.Add(new ItemInspectAttribute("TYPE", "Rifle ammunition"));
    }
}
