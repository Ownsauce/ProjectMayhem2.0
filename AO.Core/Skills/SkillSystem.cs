using AO.Core.Characters;
using System.Linq;

namespace AO.Core.Skills
{
    public static class SkillSystem
    {
        public static int CalculateSkill(Character character, CharacterSkill skill)
        {
            float trickle = 0f;

            foreach (var influence in skill.Definition.StatInfluence)
            {
                if (character.Stats.TryGetValue(influence.Key, out var stat))
                {
                    trickle += stat.Value * influence.Value;
                }
            }

            return skill.BaseValue + (int)trickle;
        }
    }
}
