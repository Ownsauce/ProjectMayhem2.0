using System;
using System.Collections.Generic;

[Serializable]
public class EquipmentSlotDefinition
{
    public int Id;
    public string Name;
    public string Category; // Weapon, Armor, Implant, Social
    public List<string> AllowedItemTypes = new();
}
