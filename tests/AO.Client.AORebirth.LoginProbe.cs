using System;
using System.IO;
using System.Threading.Tasks;
using AO.Client;
using AO.Client.Authentication;
using AO.Client.Backends.AORebirth;

internal static class AORebirthLoginProbe
{
    private static int Main(string[] args)
    {
        return RunAsync(args).GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 1 || args.Length > 2)
        {
            Console.Error.WriteLine("Usage: AORebirthLoginProbe <credentials-file> [character-id]");
            return 2;
        }

        string[] credentials = File.ReadAllLines(args[0]);
        if (credentials.Length < 2)
        {
            Console.Error.WriteLine("Credentials file must contain username and password lines.");
            return 2;
        }

        using (var backend = new AORebirthBackend("18.8.62"))
        {
            await backend.ConnectAsync(new ServerEndpoint(
                "AORebirth Local",
                new Uri("tcp://127.0.0.1:7500")));
            var authentication = await backend.AuthenticateAsync(
                new AuthenticationRequest(credentials[0].Trim(), credentials[1]));
            if (!authentication.Succeeded)
            {
                Console.Error.WriteLine("FAIL authentication rejected");
                return 1;
            }

            var characters = await backend.GetCharactersAsync();
            Console.WriteLine("PASS AORebirth authentication");
            Console.WriteLine("CHARACTERS=" + characters.Count);
            foreach (var character in characters)
            {
                Console.WriteLine(
                    $"CHARACTER id={character.Id} name={character.Name} level={character.Level} " +
                    $"profession={character.ProfessionId} breed={character.BreedId} gender={character.GenderId} " +
                    $"playfield={character.PlayfieldId} area={character.AreaName} status={character.Status}");
            }

            if (characters.Count == 0)
            {
                Console.Error.WriteLine("FAIL no character is available for zone handoff");
                return 1;
            }

            string selectedId = args.Length == 2 ? args[1] : characters[0].Id;
            var worldEntry = await backend.EnterWorldAsync(selectedId);
            if (!worldEntry.Succeeded)
            {
                Console.Error.WriteLine("FAIL zone handoff: " + worldEntry.Message);
                return 1;
            }
            Console.WriteLine(
                $"PASS zone handoff character={worldEntry.CharacterId} playfield={worldEntry.PlayfieldId} " +
                $"message={worldEntry.Message}");

            var bootstrap = await backend.ReceiveWorldBootstrapAsync();
            Console.WriteLine(
                $"PASS world bootstrap character={bootstrap.CharacterId} playfield={bootstrap.PlayfieldId} " +
                $"position=({bootstrap.X:0.###},{bootstrap.Y:0.###},{bootstrap.Z:0.###}) " +
                $"packetsObserved={bootstrap.PacketsObserved}");

            var nearby = await backend.ReceiveNearbyEntitiesAsync(512);
            if (nearby.Entities.Count == 0)
            {
                Console.Error.WriteLine("FAIL no SimpleCharFullUpdate entities were decoded");
                return 1;
            }
            Console.WriteLine(
                $"PASS nearby entities count={nearby.Entities.Count} packetsObserved={nearby.PacketsObserved}");
            foreach (var entity in nearby.Entities)
            {
                Console.WriteLine(
                    $"ENTITY identity={entity.IdentityType}:{entity.IdentityInstance} kind={entity.Kind} " +
                    $"name={entity.Name} level={entity.Level} health={entity.Health - entity.HealthDamage}/{entity.Health} " +
                    $"playfield={entity.PlayfieldId} position=({entity.X:0.###},{entity.Y:0.###},{entity.Z:0.###})");
            }

            var worldObjects = await backend.GetWorldObjectsAsync();
            if (worldObjects.Count == 0)
            {
                Console.Error.WriteLine("FAIL no static world objects were decoded");
                return 1;
            }
            Console.WriteLine($"PASS world objects count={worldObjects.Count}");
            foreach (var worldObject in worldObjects)
            {
                string position = worldObject.HasPosition
                    ? $"({worldObject.X:0.###},{worldObject.Y:0.###},{worldObject.Z:0.###})"
                    : "unknown";
                Console.WriteLine(
                    $"OBJECT identity={worldObject.IdentityType}:{worldObject.IdentityInstance} " +
                    $"kind={worldObject.Kind} name={worldObject.Name} playfield={worldObject.PlayfieldId} " +
                    $"position={position} linked={worldObject.LinkedIdentityType}:{worldObject.LinkedIdentityInstance}");
            }

            var deltaBatch = await backend.ReceiveWorldDeltasAsync(512);
            int upserts = 0, movements = 0, stats = 0, removals = 0;
            foreach (var delta in deltaBatch.Deltas)
            {
                switch (delta.Kind)
                {
                    case AO.Client.World.WorldEntityDeltaKind.Upsert: upserts++; break;
                    case AO.Client.World.WorldEntityDeltaKind.Movement: movements++; break;
                    case AO.Client.World.WorldEntityDeltaKind.Stats: stats++; break;
                    case AO.Client.World.WorldEntityDeltaKind.Remove: removals++; break;
                }
            }
            Console.WriteLine(
                $"PASS world deltas packetsObserved={deltaBatch.PacketsObserved} deltas={deltaBatch.Deltas.Count} " +
                $"upserts={upserts} movements={movements} stats={stats} removals={removals} " +
                $"snapshot={deltaBatch.Snapshot.Count}");

            await backend.DisconnectAsync();
            return 0;
        }
    }
}
