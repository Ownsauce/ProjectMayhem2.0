using AO.Core.Characters;
using UnityEngine;

namespace AO.Unity.World
{
    public class CharacterRuntimeBridge : MonoBehaviour
    {
        public enum CharacterSex
        {
            Male = 0,
            Female = 1,
            Uni = 2
        }

        public Character Character { get; private set; }
        [SerializeField] private CharacterSex sex = CharacterSex.Male;
        [SerializeField] private int debugHeadVisualAoid;
        [SerializeField] private string displayNameOverride = string.Empty;
        public CharacterSex Sex
        {
            get => IsAtroxBreed() ? CharacterSex.Uni : sex;
            set => sex = IsAtroxBreed() ? CharacterSex.Uni : (value == CharacterSex.Uni ? CharacterSex.Male : value);
        }

        public int DebugHeadVisualAoid
        {
            get => debugHeadVisualAoid;
            set => debugHeadVisualAoid = Mathf.Max(0, value);
        }

        [SerializeField] private string debugHeadMeshKey = string.Empty;
        public string DebugHeadMeshKey
        {
            get => debugHeadMeshKey ?? string.Empty;
            set => debugHeadMeshKey = value ?? string.Empty;
        }

        public string DisplayNameOverride
        {
            get => displayNameOverride ?? string.Empty;
            set => displayNameOverride = value ?? string.Empty;
        }

        public void SetCharacter(Character character)
        {
            Character = character;
            if (IsAtroxBreed())
                sex = CharacterSex.Uni;
        }

        private bool IsAtroxBreed()
        {
            return Character != null && Character.BreedId == 4;
        }
    }
}
