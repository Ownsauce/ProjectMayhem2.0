using System.Collections.Generic;
using Newtonsoft.Json;

namespace AO.Unity.World
{
    [System.Serializable]
    public sealed class PlayfieldTerrainFile
    {
        [JsonProperty("PlayfieldId")]
        public int PlayfieldId { get; set; }

        [JsonProperty("AtlasFile")]
        public string AtlasFile { get; set; }

        [JsonProperty("Chunks")]
        public List<PlayfieldTerrainChunk> Chunks { get; set; } = new();
    }

    [System.Serializable]
    public sealed class PlayfieldTerrainChunk
    {
        [JsonProperty("Vertices")]
        public List<Vec3f> Vertices { get; set; } = new();

        [JsonProperty("Normals")]
        public List<Vec3f> Normals { get; set; } = new();

        [JsonProperty("UVs")]
        public List<Vec2f> UVs { get; set; } = new();

        [JsonProperty("Triangles")]
        public List<int> Triangles { get; set; } = new();
    }

    [System.Serializable]
    public sealed class Vec3f
    {
        [JsonProperty("X")] public float X { get; set; }
        [JsonProperty("Y")] public float Y { get; set; }
        [JsonProperty("Z")] public float Z { get; set; }
    }

    [System.Serializable]
    public sealed class Vec2f
    {
        [JsonProperty("X")] public float X { get; set; }
        [JsonProperty("Y")] public float Y { get; set; }
    }

    [System.Serializable]
    public sealed class PlayfieldWaterFile
    {
        [JsonProperty("PlayfieldId")]
        public int PlayfieldId { get; set; }

        [JsonProperty("Water")]
        public List<PlayfieldWaterMesh> Water { get; set; } = new();
    }

    [System.Serializable]
    public sealed class PlayfieldWaterMesh
    {
        [JsonProperty("Vertices")]
        public List<Vec3f> Vertices { get; set; } = new();

        [JsonProperty("Triangles")]
        public List<int> Triangles { get; set; } = new();
    }
}
