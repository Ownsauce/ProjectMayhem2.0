//using System.Collections.Generic;
//using AO.Core.Characters;
//using AO.Core.Skills;
//using AO.Core.DerivedStats;
//using System.Linq;

//namespace AO.Core.Modifiers
//{
//    public static class ModifierSystem
//    {
//        public static int ApplyModifiers(Character character, string target, int baseValue)
//        {
//            var total = baseValue;

//            // Find active modifiers affecting this target
//            if (character.Modifiers != null)
//            {
//                foreach (var mod in character.Modifiers.Where(m => m.Definition.Target == target && !m.IsExpired))
//                {
//                    total += mod.Definition.Flat;
//                    total += (int)(total * mod.Definition.Percent);
//                }
//            }

//            return total;
//        }
//    }
//}
