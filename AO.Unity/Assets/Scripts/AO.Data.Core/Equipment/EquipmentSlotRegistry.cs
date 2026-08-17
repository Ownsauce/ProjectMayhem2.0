using System.Collections.Generic;

public class EquipmentSlotRegistry
{
    public List<EquipmentSlotDefinition> WeaponSlots = new();
    public List<EquipmentSlotDefinition> ArmorSlots = new();
    public List<EquipmentSlotDefinition> ImplantSlots = new();
    public List<EquipmentSlotDefinition> SocialSlots = new();

    public int TotalSlots =>
        WeaponSlots.Count +
        ArmorSlots.Count +
        ImplantSlots.Count +
        SocialSlots.Count;
}
