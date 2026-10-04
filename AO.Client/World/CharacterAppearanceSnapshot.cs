using System;
using System.Collections.Generic;
using System.Linq;

namespace AO.Client.World
{
    /// <summary>Authoritative visual state, independent of inventory and gameplay modifiers.</summary>
    public sealed class CharacterAppearanceSnapshot
    {
        public CharacterAppearanceSnapshot(IEnumerable<AppearanceTexture> textures,
            IEnumerable<AppearanceMesh> meshes, int visualFlags, int? headMeshId = null)
        {
            Textures = Array.AsReadOnly((textures ?? Array.Empty<AppearanceTexture>()).ToArray());
            Meshes = Array.AsReadOnly((meshes ?? Array.Empty<AppearanceMesh>()).ToArray());
            VisualFlags = visualFlags;
            HeadMeshId = headMeshId;
        }
        public IReadOnlyList<AppearanceTexture> Textures { get; }
        public IReadOnlyList<AppearanceMesh> Meshes { get; }
        public int VisualFlags { get; }
        // AppearanceUpdate omits the base head; null preserves the previous full update's head.
        public int? HeadMeshId { get; }
        public IEnumerable<AppearanceMesh> VisibleMeshes()
        {
            AppearanceMesh? head = null;
            foreach (var mesh in Meshes)
            {
                if (mesh.MeshId == 0 || mesh.Position < 0 || mesh.Position > 14) continue;
                if (mesh.Position == 3 && (VisualFlags & 1) == 0) continue;
                if (mesh.Position == 4 && (VisualFlags & 2) == 0) continue;
                if (mesh.Position == 0)
                {
                    if ((VisualFlags & 4) == 0 && mesh.MeshId != (uint)(HeadMeshId ?? 0)) continue;
                    if (!head.HasValue || mesh.Layer > head.Value.Layer) head = mesh;
                }
                else yield return mesh;
            }
            if (head.HasValue) yield return head.Value;
            else if (HeadMeshId > 0) yield return new AppearanceMesh(0, (uint)HeadMeshId.Value, 0, 4);
        }

        public CharacterAppearanceSnapshot WithHead(int? head) =>
            new CharacterAppearanceSnapshot(Textures, Meshes, VisualFlags, head);
    }
    public readonly struct AppearanceTexture
    {
        public AppearanceTexture(int position, int textureId, int unknown = 0)
        { Position = position; TextureId = textureId; Unknown = unknown; }
        public int Position { get; }
        public int TextureId { get; }
        public int Unknown { get; }
    }
    public readonly struct AppearanceMesh
    {
        public AppearanceMesh(int position, uint meshId, int overrideTextureId, int layer)
        { Position = position; MeshId = meshId; OverrideTextureId = overrideTextureId; Layer = layer; }
        public int Position { get; }
        public uint MeshId { get; }
        public int OverrideTextureId { get; }
        public int Layer { get; }
    }
}
