using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

public static class EquipmentSlotLoader
{
    private sealed class SlotLookupFile
    {
        public Dictionary<string, string> namesById { get; set; } = new();
    }

    public static void LoadAll(string basePath, EquipmentSlotRegistry registry)
    {
        LoadSlotFile(Path.Combine(basePath, "weapon_slots.json"), registry.WeaponSlots, "Weapon");
        LoadSlotFile(Path.Combine(basePath, "armor_slots.json"), registry.ArmorSlots, "Armor");
        LoadSlotFile(Path.Combine(basePath, "implant_slots.json"), registry.ImplantSlots, "Implant");
        LoadSlotFile(Path.Combine(basePath, "social_slots.json"), registry.SocialSlots, "Social");
    }

    private static void LoadSlotFile(string path, List<EquipmentSlotDefinition> target, string category)
    {
        if (!File.Exists(path))
        {
            Debug.LogWarning($"{category} slot file missing: {path}");
            return;
        }

        string json = File.ReadAllText(path);

        List<EquipmentSlotDefinition> slots = null;
        try
        {
            slots = JsonConvert.DeserializeObject<List<EquipmentSlotDefinition>>(json);
        }
        catch
        {
            // Ignore and try lookup format below.
        }

        if (slots == null || slots.Count == 0)
        {
            var lookup = JsonConvert.DeserializeObject<SlotLookupFile>(json);
            if (lookup?.namesById != null && lookup.namesById.Count > 0)
            {
                slots = new List<EquipmentSlotDefinition>();
                foreach (var pair in lookup.namesById)
                {
                    if (!int.TryParse(pair.Key, out int id))
                        continue;

                    slots.Add(new EquipmentSlotDefinition
                    {
                        Id = id,
                        Name = pair.Value
                    });
                }
            }
        }

        if (slots == null)
            slots = new List<EquipmentSlotDefinition>();

        foreach (var slot in slots)
        {
            slot.Category = category;
            target.Add(slot);
        }

        Debug.Log($"Loaded {target.Count} {category} slots");
    }
}
