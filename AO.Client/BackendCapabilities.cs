using System;

namespace AO.Client
{
    [Flags]
    public enum BackendCapabilities
    {
        None = 0,
        Authentication = 1 << 0,
        CharacterList = 1 << 1,
        CharacterSelection = 1 << 2,
        WorldEntry = 1 << 3,
        WorldState = 1 << 4,
        Movement = 1 << 5,
        Chat = 1 << 6,
        Inventory = 1 << 7,
        Combat = 1 << 8,
        Quests = 1 << 9,
        ItemMovement = 1 << 10
    }
}
