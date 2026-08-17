namespace AO.Server.Contracts
{
    public abstract record ClientAction;

    public sealed record MoveAction(float X, float Z) : ClientAction;

    public sealed record StopMoveAction() : ClientAction;

    public sealed record EquipItemAction(int SlotId, long InstanceId) : ClientAction;

    public sealed record UnequipSlotAction(int SlotId) : ClientAction;

    public sealed record IncreaseStatAction(string StatName, int Amount) : ClientAction;

    public sealed record GainExperienceAction(long Amount) : ClientAction;

    public sealed record ZoneTransitionAction(string ZoneLinkId, int CurrentPlayfieldId) : ClientAction;

    public sealed record ZoneLoadedAction(int PlayfieldId, float X, float Y, float Z) : ClientAction;

    public sealed record InteractWithRuntimeEntityAction(string EntityId) : ClientAction;

    public sealed record AttackRuntimeEntityAction(string EntityId, float TargetX, float TargetY, float TargetZ, int DamageAmount = 0) : ClientAction;

    public sealed record SetCharacterSettingsAction(int Level, int BreedId, int ProfessionId, int Sex, long Experience) : ClientAction;
}
