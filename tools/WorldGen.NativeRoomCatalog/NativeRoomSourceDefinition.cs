using System.Text.Json;

/// <summary>Editable source/package choices; measured geometry remains derived from the selected installation.</summary>
public sealed class NativeRoomSourceDefinition
{
    public int Version = 1;
    public string SourceId = "", DisplayName = "";
    public int SourcePlayfield, EntranceSourceRoom, EntranceSpawnX, EntranceSpawnZ;
    public NativeRoomPoolDefinition[] Pools = Array.Empty<NativeRoomPoolDefinition>();
    public NativeRoomDoorwayCheck[] DoorwayChecks = Array.Empty<NativeRoomDoorwayCheck>();

    public static NativeRoomSourceDefinition Load(string path)
    {
        var value = JsonSerializer.Deserialize<NativeRoomSourceDefinition>(File.ReadAllText(path), new JsonSerializerOptions { IncludeFields = true })
            ?? throw new InvalidDataException("Missing native source definition.");
        if (value.Version != 1 || value.SourcePlayfield <= 0 || value.EntranceSourceRoom < 0 || value.Pools == null || value.Pools.Length == 0
            || value.Pools.Any(p => p == null || string.IsNullOrWhiteSpace(p.Id) || p.Id == "mixed" || p.Rooms == null || p.Rooms.Length == 0
                || p.Id.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_')))
            || value.Pools.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != value.Pools.Length
            || value.Pools.SelectMany(p => p.Rooms).Any(id => id < 0)
            || value.Pools.SelectMany(p => p.Rooms).Distinct().Count() != value.Pools.Sum(p => p.Rooms.Length)
            || value.DoorwayChecks == null || value.DoorwayChecks.Any(c => c == null || c.FirstRoom < 0 || c.SecondRoom < 0
                || c.FirstSocket < 0 || c.SecondSocket < 0 || c.FloorToleranceMm < 0))
            throw new InvalidDataException("Invalid source/pool definition.");
        return value;
    }

    public string PoolFor(int sourceRoom) => Pools.SingleOrDefault(p => p.Rooms.Contains(sourceRoom))?.Id
        ?? throw new InvalidDataException("Source room " + sourceRoom + " has no authored pool assignment.");

    public void ValidateRoomCount(int count)
    {
        if (EntranceSourceRoom >= count || Pools.SelectMany(p => p.Rooms).Any(id => id >= count)
            || DoorwayChecks.Any(c => c.FirstRoom >= count || c.SecondRoom >= count))
            throw new InvalidDataException("Source definition references a room absent from the selected playfield.");
        for (int i = 0; i < count; i++) PoolFor(i);
    }
}

public sealed class NativeRoomDoorwayCheck
{
    public int FirstRoom, FirstSocket, SecondRoom, SecondSocket, ExpectedFirstY, FloorToleranceMm;
}

public sealed class NativeRoomPoolDefinition
{
    public string Id = "";
    public int[] Rooms = Array.Empty<int>();
}
