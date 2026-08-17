using System.Collections.Generic;
using Newtonsoft.Json;

namespace AO.Unity.World
{
    [System.Serializable]
    public sealed class ZoneLinksFile
    {
        [JsonProperty("Links")]
        public List<ZoneLinkRow> Links { get; set; } = new();
    }

    [System.Serializable]
    public sealed class ZoneLinkRow
    {
        [JsonProperty("Id")]
        public string Id { get; set; }

        [JsonProperty("FromPlayfieldId")]
        public int FromPlayfieldId { get; set; }

        [JsonProperty("ToPlayfieldId")]
        public int ToPlayfieldId { get; set; }

        [JsonProperty("SourceStatelId")]
        public int SourceStatelId { get; set; }

        [JsonProperty("SourceMeshName")]
        public string SourceMeshName { get; set; }

        [JsonProperty("TriggerRadius")]
        public float TriggerRadius { get; set; } = 2.5f;

        [JsonProperty("SourceAOPosition")]
        public Vec3f SourceAOPosition { get; set; }

        [JsonProperty("SourceAORadius")]
        public float SourceAORadius { get; set; } = 25f;

        [JsonProperty("TargetAOSpawn")]
        public Vec3f TargetAOSpawn { get; set; }

        [JsonProperty("TargetYaw")]
        public float? TargetYaw { get; set; }
    }
}
