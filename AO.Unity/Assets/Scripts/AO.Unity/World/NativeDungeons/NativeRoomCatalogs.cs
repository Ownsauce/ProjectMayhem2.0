using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using WorldGen.Contracts;
using WorldGen.Dungeons;

namespace AO.Unity.World
{
    /// <summary>Local source selection and recorded-revision lookup shared by editor and gameplay.</summary>
    public static class NativeRoomCatalogs
    {
        private sealed class Codec : INativeRoomCatalogCodec
        {
            public NativeRoomCatalog ReadCatalog(string json) => JsonUtility.FromJson<NativeRoomCatalog>(json);
            public NativeRoomSourceRegistry ReadRegistry(string json) => JsonUtility.FromJson<NativeRoomSourceRegistry>(json);
            public string WriteRegistry(NativeRoomSourceRegistry value) => JsonUtility.ToJson(value, true);
            public NativeRoomCatalogPublication ReadPublication(string json) => JsonUtility.FromJson<NativeRoomCatalogPublication>(json);
            public string WritePublication(NativeRoomCatalogPublication value) => JsonUtility.ToJson(value, true);
        }
        public static string Root => Path.Combine(Application.streamingAssetsPath, "NativeCopies");
        private static string storeRoot;
        private static NativeRoomCatalogStore store;
        public static NativeRoomCatalogStore Store
        {
            get { if (store == null || storeRoot != Root) { storeRoot = Root; store = new NativeRoomCatalogStore(Root, new Codec()); } return store; }
        }
        public static NativeRoomCatalog Read(string sourceId, string revision, out string hash)
        {
            var published = Store.Load(sourceId, revision); hash = published.Revision; return published.Catalog;
        }
        public static NativeRoomCatalog Resolve(GenerationManifest manifest, out string hash, out string sourceId)
        {
            int playfield = int.Parse(manifest.Parameters["nativePlayfield"], CultureInfo.InvariantCulture);
            if (File.Exists(Path.Combine(Root, "sources.json")))
            {
                var published = manifest.Parameters.TryGetValue("nativeSource", out sourceId)
                    ? Store.Load(sourceId, manifest.ContentCatalogHash) : Store.LoadPlayfield(playfield, manifest.ContentCatalogHash);
                if (published.Catalog.SourcePlayfield != playfield) throw new InvalidDataException("Manifest source/playfield mismatch.");
                hash = published.Revision; sourceId = published.Source.Id; return published.Catalog;
            }
            sourceId = "ao-pf-" + playfield;
            return NativeRoomDungeonRuntime.ReadLegacyCatalog(playfield, out hash);
        }
    }
}
