using AO.Core.Characters;
using AO.Core.Items;
using AO.Core.Stats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

class Program
{
    static void Main()
    {
        // --- Load professions ---
        string ipPath = Path.Combine(AppContext.BaseDirectory, "Data", "ipdist.xml");
        var professions = ProfessionLoader.LoadFromXml(ipPath);

        var adventurer = professions.FirstOrDefault(p => p.Name == "Adventurer");
        if (adventurer == null)
        {
            Console.WriteLine("Adventurer profession not found!");
            return;
        }

        // --- Create test character ---
        var character = new Character("TestChar", adventurer, startingIp: 100);
        Console.WriteLine($"Created {character.Name} with {character.AvailableIp} IP");

        // --- Load items ---
        // Find solution root (go up from bin folder)
        var baseDir = AppContext.BaseDirectory;
        var solutionRoot = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\"));

        var itemsJsonPath = Path.Combine(solutionRoot, "AO.Core", "Data", "items.json");

        Console.WriteLine($"Loading items from: {itemsJsonPath}");
        var itemDefinitions = ItemLoader.LoadFromJson(itemsJsonPath);


        // --- Give items to character ---
        var ammoDefinition = itemDefinitions.FirstOrDefault(i => i.Name.Contains("Bullets") || i.Name.Contains("Ammunition"));
        var weaponDefinition = itemDefinitions.FirstOrDefault(i => i.Name.Contains("Blaster") || i.Name.Contains("Pistol"));

        if (ammoDefinition != null)
        {
            character.Inventory.AddItem(new ItemInstance(ammoDefinition, 100));
            Console.WriteLine($"Added {ammoDefinition.Name} x100");
        }

        if (weaponDefinition != null)
        {
            character.Inventory.AddItem(new ItemInstance(weaponDefinition, 1));
            Console.WriteLine($"Added {weaponDefinition.Name} x1");
        }

        // --- Display inventory ---
        Console.WriteLine("\nInventory:");
        foreach (var item in character.Inventory.Items)
        {
            Console.WriteLine($"{item.Definition.Name} x{item.Quantity}");
        }
    }
}
