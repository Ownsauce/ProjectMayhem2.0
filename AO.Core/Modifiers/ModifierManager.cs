//using AO.Core.Characters;
//using AO.Core.Items;

//namespace AO.Core.Modifiers
//{
//    public static class ModifierManager
//    {
//        // Apply all modifiers from an item
//        public static void ApplyModifiers(ItemInstance item, Character character)
//        {
//            if (item == null) return;
//            foreach (var mod in item.Modifiers)
//            {
//                character.Stats.ApplyModifier(mod);
//            }
//        }

//        // Remove all modifiers from an item
//        public static void RemoveModifiers(ItemInstance item, Character character)
//        {
//            if (item == null) return;
//            foreach (var mod in item.Modifiers)
//            {
//                character.Stats.RemoveModifier(mod);
//            }
//        }
//    }
//}
