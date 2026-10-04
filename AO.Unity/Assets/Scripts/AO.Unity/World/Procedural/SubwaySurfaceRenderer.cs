using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Content;
using WorldGen.Dungeons;

namespace AO.Unity.World.Procedural
{
    public static class SubwaySurfaceRenderer
    {
        public static bool TryPlace(SubwayDungeonKit kit, DungeonAssetRole role, string label,
            Transform parent, Vector3 center, Vector3 size, DungeonPortalFacing? facing,
            List<Mesh> ownedMeshes, Action<string, Vector3, Vector3, Transform> backing)
        {
            if (role == DungeonAssetRole.Unspecified) return false;
            string key = role.ToString().ToLowerInvariant();
            if (kit.Resolve(key) == null)
            { Debug.LogWarning("[WorldGen] Missing asset binding for " + role + ": " + label); return false; }
            if (role == DungeonAssetRole.Floor || role == DungeonAssetRole.Wall)
            {
                bool wall = role == DungeonAssetRole.Wall;
                if (wall && !facing.HasValue) throw new InvalidOperationException("Wall placement requires an explicit facing: " + label);
                bool xWall = wall && (facing == DungeonPortalFacing.NegativeX || facing == DungeonPortalFacing.PositiveX);
                Vector3 inward = !wall ? Vector3.up : facing == DungeonPortalFacing.NegativeX ? Vector3.right
                    : facing == DungeonPortalFacing.PositiveX ? Vector3.left
                    : facing == DungeonPortalFacing.NegativeZ ? Vector3.forward : Vector3.back;
                float thickness = wall ? (xWall ? size.x : size.z) : size.y;
                Mesh mesh = kit.PlaceSurface(key, label, parent, center + inward * (thickness * .5f + .001f),
                    wall ? (xWall ? size.z : size.x) : size.x, wall ? size.y : size.z,
                    wall ? Quaternion.LookRotation(inward, Vector3.up) : Quaternion.Euler(-90, 0, 0));
                if (mesh == null) { Debug.LogError("[WorldGen] Missing baked surface: " + label); return false; }
                ownedMeshes.Add(mesh);
                if (wall) backing(label + " Opaque Backing", center, size + Vector3.one * .015f, parent);
                return true;
            }
            if (role == DungeonAssetRole.Ceiling)
            {
                backing(label + " Opaque Backing", center + Vector3.up * (size.y * .5f + .025f),
                    new Vector3(size.x + .1f, .05f, size.z + .1f), parent);
                int columns = Mathf.Max(1, Mathf.CeilToInt(size.x / 4));
                int rows = Mathf.Max(1, Mathf.CeilToInt(size.z / 4));
                for (int row = 0; row < rows; row++) for (int col = 0; col < columns; col++)
                    kit.Place(role, label + " " + row + "/" + col, parent,
                        center + new Vector3(-size.x / 2 + (col + .5f) * size.x / columns, 0,
                            -size.z / 2 + (row + .5f) * size.z / rows),
                        new Vector3(size.x / columns, size.y, size.z / rows), Quaternion.identity);
                return true;
            }
            bool alongZ = size.z > size.x && role != DungeonAssetRole.Pillar;
            Vector3 dimensions = alongZ ? new Vector3(size.z, size.y, size.x) : size;
            Quaternion rotation = Quaternion.Euler(0, alongZ ? 90 : 0, 0);
            if (role != DungeonAssetRole.Safety) return kit.Place(role, label, parent, center, dimensions, rotation);
            int count = Mathf.Max(1, Mathf.CeilToInt(dimensions.x));
            for (int i = 0; i < count; i++)
                kit.Place(role, label + " Module " + i, parent,
                    center + (alongZ ? Vector3.forward : Vector3.right) * (-dimensions.x / 2 + (i + .5f) * dimensions.x / count),
                    new Vector3(dimensions.x / count, dimensions.y, dimensions.z), rotation);
            return true;
        }
    }
}
