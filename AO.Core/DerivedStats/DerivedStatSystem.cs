//using AO.Core.Characters;
//using System.Linq;

//namespace AO.Core.DerivedStats
//{
//    public static class DerivedStatSystem
//    {
//        public static int Calculate(Character character, CharacterDerivedStat derived)
//        {
//            float total = 0f;

//            foreach (var influence in derived.Definition.Influences)
//            {
//                var name = influence.Key;
//                var weight = influence.Value;

//                // Try skills first
//                if (character.Skills.TryGetValue(name, out var skill))
//                {
//                    total += skill.FinalValue * weight;
//                }
//                // If skill not found, try raw stats
//                else if (character.Stats.TryGetValue(name, out var stat))
//                {
//                    total += stat.Value * weight;
//                }
//            }

//            return derived.BaseValue + (int)total;
//        }
//    }
//}
