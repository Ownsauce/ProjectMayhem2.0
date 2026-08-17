using AO.Core.Characters;
using AO.Core.Items;
using AO.Core.Modifiers;
using AO.Core.Stats;
using AO.Data.Unity;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AO.Unity
{
    public class CharacterTest : MonoBehaviour
    {
        [SerializeField] private bool runOnStart = false;
        private Character testChar;

        void Start()
        {
            if (!runOnStart)
                return;

            Debug.Log("=== AO Character Test ===");

            AODataManager.EnsureInstance();
            var data = AODataManager.Instance;

            Debug.Log($"Loaded {data.ItemInstances.Count} instances");

            Profession prof = new Profession { Name = "DummyProfession" };
            testChar = new Character("TestChar", prof, 100);

            var weaponInstance = data.ItemInstances.FirstOrDefault(i =>
                i.Definition != null &&
                i.Definition.StatModifiers != null &&
                i.Definition.StatModifiers.Any(s => s.StatId == 76 && s.Value == 1));

            if (weaponInstance == null)
            {
                Debug.LogWarning("No weapon instance found in loaded data.");
                return;
            }

            Debug.Log($"Equipping JSON item: {weaponInstance.Definition.Name}");

            bool equipped = testChar.EquipItem(6, weaponInstance.InstanceId);
            Debug.Log($"Equip success: {equipped}");

            foreach (var kvp in testChar.Equipment.GetAllEquipped())
            {
                var inst = data.GetItemInstance(kvp.Value);
                if (inst?.Definition != null)
                    Debug.Log($"Equipped -> {inst.Definition.Name}");
            }

            foreach (var mod in testChar.StatsContainer.DebugGetAllStats())
                Debug.Log($"Stat {mod.Key} = {mod.Value}");
        }

        void Update()
        {
            if (Keyboard.current == null || testChar == null) return;

            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                testChar.AddIp(10);
                Debug.Log($"Added 10 IP. New total: {testChar.AvailableIp}");
            }

            if (Keyboard.current.sKey.wasPressedThisFrame)
                CharacterSaveManager.SaveCharacter(testChar);

            if (Keyboard.current.lKey.wasPressedThisFrame)
            {
                var loadedChar = CharacterSaveManager.LoadCharacter("TestChar");
                if (loadedChar == null) return;

                testChar = loadedChar;
                Debug.Log($"Loaded character: {testChar.Name}, IP: {testChar.AvailableIp}");

                // Rebuild inventory item references from AODataManager data models.
                var rebuilt = testChar.Inventory.Items
                    .Select(coreItem => AODataManager.Instance.GetItemInstance(coreItem.Definition.AOID))
                    .Where(dataItem => dataItem?.Definition != null)
                    .Select(dataItem =>
                    {
                        var def = new ItemDefinition(
                            dataItem.Definition.Name,
                            dataItem.Definition.Id,
                            dataItem.Definition.SlotType);

                        if (dataItem.Definition.StatModifiers != null)
                        {
                            foreach (var sm in dataItem.Definition.StatModifiers)
                                def.AddModifier(new StatModifier(sm.StatId, sm.Value));
                        }

                        return new ItemInstance(def, dataItem.Quantity);
                    })
                    .ToList();

                foreach (var existing in testChar.Inventory.Items.ToList())
                    testChar.Inventory.RemoveItem(existing);

                foreach (var item in rebuilt)
                    testChar.Inventory.AddItem(item);

                Debug.Log("Inventory reloaded and items reconnected.");
            }
        }
    }
}
