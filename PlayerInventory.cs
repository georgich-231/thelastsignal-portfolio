using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerInventory : MonoBehaviour
{
    [Min(0f)] public float capacityKg = 25f;
    public InventoryItem[] slots = new InventoryItem[20];
    public int[] quickbar = new int[] { -1, -1, -1, -1, -1, -1 };
    public int SelectedQuickSlot { get; private set; } = 0;
    public int EquippedSlot => SelectedQuickSlot >= 0 && SelectedQuickSlot < quickbar.Length ? quickbar[SelectedQuickSlot] : -1;
    public InventoryItem EquippedItem => EquippedSlot >= 0 && EquippedSlot < slots.Length ? slots[EquippedSlot] : null;
    public void SelectQuickSlot(int index) { if(index<0 || index>=quickbar.Length) return; SelectedQuickSlot=index; Changed?.Invoke(); }
    public event Action Changed;
    public float TotalWeight { get { float w = 0; foreach (var i in slots) if (i != null) w += i.weightKg; return w; } }
    public string LastMessage { get; private set; }
    float notifyAt;
    public bool TryCollect(InspectablePickup pickup)
    {
        if (pickup == null) return false;
        var item = pickup.GetComponent<InventoryItem>();
        if (item == null) { LastMessage = "This item cannot be stored."; return false; }
        if (item.IsStored || Array.IndexOf(slots, item) >= 0) return false;
        int free = Array.FindIndex(slots, i => i == null);
        if (free < 0) { LastMessage = "Backpack is full."; return false; }
        if (TotalWeight + item.weightKg > capacityKg + .00001f) { LastMessage = "Too heavy for your backpack."; return false; }
        slots[free] = item;
        item.IsStored = true;
        item.Extinguish();
        pickup.SetFocused(false);
        pickup.gameObject.SetActive(false);
        int freeQuick = Array.FindIndex(quickbar, slot => slot < 0);
        if (freeQuick >= 0) quickbar[freeQuick] = free;
        LastMessage = pickup.displayName + " added to backpack";
        GetComponent<SurvivalAudio>()?.Backpack();
        Changed?.Invoke();
        return true;
    }
    public void Move(int from, int to)
    {
        if (from < 0 || to < 0 || from >= slots.Length || to >= slots.Length || from == to) return;
        var swap = slots[to]; slots[to] = slots[from]; slots[from] = swap;
        for (int n = 0; n < quickbar.Length; n++)
            if (quickbar[n] == from) quickbar[n] = to; else if (quickbar[n] == to) quickbar[n] = from;
        Changed?.Invoke();
    }
    public void Assign(int slot, int key)
    {
        if (slot < 0 || slot >= slots.Length || slots[slot] == null || key < 0 || key >= quickbar.Length) return;
        for (int n = 0; n < quickbar.Length; n++) if (quickbar[n] == slot) quickbar[n] = -1;
        quickbar[key] = slot; Changed?.Invoke();
    }
    public void Use(int slot)
    {
        if (slot < 0 || slot >= slots.Length || slots[slot] == null) return;
        var item = slots[slot];
        foreach (var other in slots) if (other != null && other != item) other.Extinguish();
        item.ToggleUse();
        LastMessage = item.UseMessage;
        Changed?.Invoke();
    }
    public bool Drop(int slot)
    {
        if (slot < 0 || slot >= slots.Length || slots[slot] == null) return false;
        var item = slots[slot];
        Vector3 candidate = transform.position + transform.forward * .85f;
        float ground = float.NegativeInfinity;
        foreach (var t in Terrain.activeTerrains)
        {
            Vector3 local = candidate - t.transform.position;
            if (local.x >= 0 && local.z >= 0 && local.x <= t.terrainData.size.x && local.z <= t.terrainData.size.z)
                ground = t.SampleHeight(candidate) + t.transform.position.y;
        }
        if (float.IsNegativeInfinity(ground)) { LastMessage = "Move onto clear ground to drop this item."; return false; }
        candidate.y = ground + .12f;
        foreach (var c in Physics.OverlapSphere(candidate + Vector3.up * .15f, .15f, ~0, QueryTriggerInteraction.Ignore))
            if (!(c is TerrainCollider) && !c.transform.IsChildOf(transform) && c.GetComponentInParent<CanopyOccluder>() == null)
            { LastMessage = "Move away from obstacles to drop this item."; return false; }
        item.Extinguish(); item.IsStored = false;
        item.transform.position = candidate;
        item.gameObject.SetActive(true);
        item.Pickup.RestoreWorldPresentation();
        var rs = item.Pickup.PresentationRoot.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0) { var bounds = rs[0].bounds; foreach (var r in rs) bounds.Encapsulate(r.bounds); item.transform.position += Vector3.up * (ground + .025f - bounds.min.y); }
        slots[slot] = null;
        for (int n = 0; n < quickbar.Length; n++) if (quickbar[n] == slot) quickbar[n] = -1;
        LastMessage = item.Pickup.displayName + " dropped"; Changed?.Invoke(); return true;
    }
    void Update()
    {
        bool active=Time.timeScale>0 && !(GetComponent<InventoryUI>()?.IsOpen ?? false) && !(GetComponent<OpeningSequence>()?.BlocksGameplay ?? false);
        foreach(var item in slots) if(item!=null)
        {
            if(item!=EquippedItem)item.Extinguish();
            else if(active)item.Burn(Time.deltaTime);
        }
        if (Time.unscaledTime >= notifyAt) { notifyAt = Time.unscaledTime + .25f; Changed?.Invoke(); }
    }
    void OnDisable() { foreach (var item in slots) if (item != null) item.Extinguish(); }
}

