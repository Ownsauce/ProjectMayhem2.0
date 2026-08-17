using System.IO;
using UnityEngine;
using AO.Core.Characters;

namespace AO.Data.Unity
{
    public static class CharacterSaveManager
    {
        public static void SaveCharacter(Character character)
        {
            string json = JsonUtility.ToJson(character, true);
            string path = Path.Combine(Application.persistentDataPath, $"{character.Name}.json");
            File.WriteAllText(path, json);

            Debug.Log($"Saved character '{character.Name}' to {path}");
        }

        public static Character LoadCharacter(string name)
        {
            string path = Path.Combine(Application.persistentDataPath, $"{name}.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"Character save file not found: {path}");
                return null;
            }

            string json = File.ReadAllText(path);
            var character = JsonUtility.FromJson<Character>(json);

            Debug.Log($"Loaded character '{name}' from {path}");
            return character;
        }
    }
}
