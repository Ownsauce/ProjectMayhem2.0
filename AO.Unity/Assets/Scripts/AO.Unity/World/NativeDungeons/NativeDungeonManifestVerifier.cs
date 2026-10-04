using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AO.Assets.ResourceDatabase;
using AO.Unity.Assets;
using UnityEngine;
using WorldGen.Contracts;
using WorldGen.Dungeons;

namespace AO.Unity.World
{
    /// <summary>
    /// Verifies local AO resources and native recipes before the session starts a transfer.
    /// Has no network-session, GLB-kit or player state.
    /// </summary>
    internal static class NativeDungeonManifestVerifier
    {
        public static NativeRoomRecipe VerifyRooms(DungeonManifestEnvelope envelope, out DungeonGenerationResult result)
        {
            var manifest = envelope.Manifest;
            var catalog = NativeRoomCatalogs.Resolve(manifest, out string catalogHash, out string sourceId);
            if (manifest.GeneratorVersion != NativeRoomDungeon.GeneratorVersion || manifest.ParameterSchemaVersion != 1
                || manifest.ContentCatalogHash != catalogHash || manifest.Parameters["nativePlayfield"] != catalog.SourcePlayfield.ToString(CultureInfo.InvariantCulture)
                || manifest.Parameters["sourceSha256"] != catalog.SourceSha256
                || manifest.Parameters["surfaceSha256"] != catalog.SurfaceSha256)
                throw new InvalidDataException("Native room catalog differs from the server.");

            ReadLocalHashes(catalog.SourcePlayfield, out string source, out string surfaces);
            if (source != catalog.SourceSha256 || surfaces != catalog.SurfaceSha256)
                throw new InvalidDataException("Local source resources differ from the server catalog.");
            if (!manifest.Parameters.TryGetValue("nativeRecipe", out string snapshot) || snapshot.Length > 1024 * 1024)
                throw new InvalidDataException("Missing resolved native recipe.");
            var recipe = JsonUtility.FromJson<NativeRoomRecipeData>(snapshot).Restore(catalog);
            if (recipe.Rooms.Count != int.Parse(manifest.Parameters["roomCount"], CultureInfo.InvariantCulture))
                throw new InvalidDataException("Native room count differs from recipe.");
            if (NativeRoomDungeon.Hash(manifest, recipe) != envelope.ExpectedLayoutHash)
                throw new InvalidDataException("Native room recipe hash differs from the server.");
            result = DungeonGenerationResults.FromRooms(manifest, recipe,
                new DungeonResourceCatalog("ao-install", sourceId, catalogHash),
                new DungeonResourceCatalog("native-collision", sourceId, catalogHash));
            return recipe;
        }

        public static DungeonLayout VerifyCopy(DungeonManifestEnvelope envelope)
        {
            var manifest = envelope.Manifest;
            if (manifest.GeneratorVersion != "1.0.0" || manifest.ParameterSchemaVersion != 1
                || manifest.Parameters["nativePlayfield"] != "127")
                throw new InvalidDataException("Unsupported native copy manifest.");

            ReadLocalHashes(127, out string source, out string surfaces);
            string content = NativeRoomDungeon.Sha(Encoding.UTF8.GetBytes(source + surfaces));
            if (source != manifest.Parameters["sourceSha256"]
                || surfaces != manifest.Parameters["surfaceSha256"] || content != envelope.ExpectedLayoutHash)
                throw new InvalidDataException("PF 127 resources differ from the server copy; entry blocked.");
            return new DungeonLayout(manifest, Array.Empty<DungeonRoom>(), Array.Empty<DungeonConnection>(),
                Array.Empty<DungeonCorridor>(), Array.Empty<DungeonDoorPortal>(), Array.Empty<DungeonSpawnPoint>());
        }

        private static void ReadLocalHashes(int playfield, out string source, out string surfaces)
        {
            var install = AOInstallConfiguration.GetConfiguredInstall();
            using var db = new AOResourceDatabase(install.RootPath);
            if (!db.TryReadRaw(AOResourceTypes.Playfield, playfield, out byte[] raw))
                throw new InvalidDataException("Configured installation has no source PF " + playfield);
            string fingerprint = new string(install.DatabaseFingerprint.Select(c =>
                char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
            string cache = Path.Combine(AOInstallConfiguration.CacheRoot, "IndoorSurfaces", fingerprint, playfield + "_v2.aois");
            source = NativeRoomDungeon.Sha(raw);
            surfaces = NativeRoomDungeon.Sha(File.ReadAllBytes(cache));
        }
    }
}
