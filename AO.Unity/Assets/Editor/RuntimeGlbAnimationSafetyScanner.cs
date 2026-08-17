using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace AO.Unity.EditorTools
{
    public static class RuntimeGlbAnimationSafetyScanner
    {
        private const uint GlbMagic = 0x46546C67; // glTF
        private const uint JsonChunkType = 0x4E4F534A; // JSON
        private const uint BinChunkType = 0x004E4942; // BIN

        [Serializable]
        private sealed class SafetyCacheFile
        {
            public int Version = 1;
            public List<string> DisableAnimationsPaths = new();
        }

        [Serializable]
        private sealed class SanitizedMapFile
        {
            public int Version = 1;
            public List<SanitizedEntry> Entries = new();
        }

        [Serializable]
        private sealed class SanitizedEntry
        {
            public string OriginalPath;
            public string SanitizedPath;
        }

        private sealed class ParsedGlb
        {
            public GlbRoot Root;
            public byte[] Bin;
        }

        private sealed class GlbRoot
        {
            public List<Accessor> accessors;
            public List<BufferView> bufferViews;
            public List<Animation> animations;
        }

        private sealed class Accessor
        {
            public int bufferView = -1;
            public int byteOffset = 0;
            public int componentType = 0;
            public int count = 0;
            public string type;
        }

        private sealed class BufferView
        {
            public int buffer = 0;
            public int byteOffset = 0;
            public int byteLength = 0;
            public int byteStride = 0;
        }

        private sealed class Animation
        {
            public List<Sampler> samplers;
        }

        private sealed class Sampler
        {
            public int input = -1;
        }

        [MenuItem("Tools/AO/Runtime GLB/Build Animation Safety Cache (Offline)")]
        public static void BuildAnimationSafetyCacheOffline()
        {
            string aoDataRoot = Path.Combine(Application.dataPath, "StreamingAssets", "AOData");
            if (!Directory.Exists(aoDataRoot))
            {
                // Fallback for mixed launch contexts where CWD differs from Unity project root.
                string cwd = Directory.GetCurrentDirectory();
                string fallback = Path.Combine(cwd, "AO.Unity", "Assets", "StreamingAssets", "AOData");
                if (Directory.Exists(fallback))
                    aoDataRoot = fallback;
            }
            if (!Directory.Exists(aoDataRoot))
            {
                Debug.LogError($"AOData folder not found: {aoDataRoot}");
                return;
            }

            string[] meshFolders =
            {
                Path.Combine(aoDataRoot, "CharacterMeshes"),
                Path.Combine(aoDataRoot, "ItemMeshes"),
                Path.Combine(aoDataRoot, "Meshes")
            };

            var glbPaths = new List<string>();
            for (int i = 0; i < meshFolders.Length; i++)
            {
                string folder = meshFolders[i];
                if (!Directory.Exists(folder))
                    continue;
                glbPaths.AddRange(Directory.GetFiles(folder, "*.glb", SearchOption.AllDirectories));
            }

            glbPaths = glbPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (glbPaths.Count == 0)
            {
                Debug.LogWarning("No GLB files found for offline animation safety scan.");
                return;
            }

            var flagged = new List<string>();
            var sanitizedEntries = new List<SanitizedEntry>();
            int scanned = 0;
            int parseErrors = 0;
            int sanitizedCount = 0;

            try
            {
                for (int i = 0; i < glbPaths.Count; i++)
                {
                    string path = glbPaths[i];
                    EditorUtility.DisplayProgressBar(
                        "AO Runtime GLB Safety Scan",
                        $"Scanning {i + 1}/{glbPaths.Count}: {Path.GetFileName(path)}",
                        (i + 1f) / glbPaths.Count);

                    scanned++;
                    try
                    {
                        if (HasNonIncreasingAnimationInput(path))
                        {
                            flagged.Add(path);
                            if (TryWriteSanitizedGlbCopy(path, aoDataRoot, out string sanitizedPath))
                            {
                                sanitizedEntries.Add(new SanitizedEntry
                                {
                                    OriginalPath = path,
                                    SanitizedPath = sanitizedPath
                                });
                                sanitizedCount++;
                            }
                        }
                    }
                    catch
                    {
                        parseErrors++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string outPath = Path.Combine(aoDataRoot, "runtime_glb_animation_safety_cache.json");
            var cache = new SafetyCacheFile
            {
                DisableAnimationsPaths = flagged
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
            File.WriteAllText(outPath, JsonConvert.SerializeObject(cache, Formatting.Indented));

            string mapPath = Path.Combine(aoDataRoot, "runtime_glb_sanitized_map.json");
            var map = new SanitizedMapFile
            {
                Entries = sanitizedEntries
                    .GroupBy(e => e.OriginalPath, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.Last())
                    .OrderBy(e => e.OriginalPath, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
            File.WriteAllText(mapPath, JsonConvert.SerializeObject(map, Formatting.Indented));
            AssetDatabase.Refresh();

            Debug.Log(
                $"AO runtime GLB animation safety cache built.\n" +
                $"Scanned: {scanned}\n" +
                $"Flagged (disable animations): {cache.DisableAnimationsPaths.Count}\n" +
                $"Sanitized copies: {sanitizedCount}\n" +
                $"Parse errors: {parseErrors}\n" +
                $"Safety cache: {outPath}\n" +
                $"Sanitized map: {mapPath}");
        }

        private static bool HasNonIncreasingAnimationInput(string glbPath)
        {
            var parsed = ParseGlb(glbPath);
            if (parsed?.Root?.animations == null || parsed.Root.animations.Count == 0)
                return false;

            for (int a = 0; a < parsed.Root.animations.Count; a++)
            {
                var anim = parsed.Root.animations[a];
                if (anim?.samplers == null)
                    continue;

                for (int s = 0; s < anim.samplers.Count; s++)
                {
                    var sampler = anim.samplers[s];
                    if (sampler == null || sampler.input < 0)
                        continue;

                    if (ReadAccessorFloats(parsed, sampler.input, out var times) && times != null && times.Length > 1)
                    {
                        float prev = times[0];
                        for (int i = 1; i < times.Length; i++)
                        {
                            float current = times[i];
                            if (!(current > prev))
                                return true;
                            prev = current;
                        }
                    }
                }
            }

            return false;
        }

        private static ParsedGlb ParseGlb(string path)
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);

            uint magic = br.ReadUInt32();
            uint version = br.ReadUInt32();
            uint length = br.ReadUInt32();
            if (magic != GlbMagic || version != 2 || length < 20)
                throw new InvalidDataException($"Invalid GLB header: {path}");

            string json = null;
            byte[] bin = Array.Empty<byte>();

            while (fs.Position + 8 <= fs.Length)
            {
                uint chunkLength = br.ReadUInt32();
                uint chunkType = br.ReadUInt32();
                if (chunkLength > int.MaxValue || fs.Position + chunkLength > fs.Length)
                    throw new InvalidDataException($"Invalid chunk size in {path}");

                byte[] chunk = br.ReadBytes((int)chunkLength);
                if (chunkType == JsonChunkType)
                    json = Encoding.UTF8.GetString(chunk);
                else if (chunkType == BinChunkType)
                    bin = chunk;
            }

            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException($"Missing JSON chunk: {path}");

            var root = JsonConvert.DeserializeObject<GlbRoot>(json);
            return new ParsedGlb { Root = root, Bin = bin ?? Array.Empty<byte>() };
        }

        private static bool ReadAccessorFloats(ParsedGlb parsed, int accessorIndex, out float[] values)
        {
            values = null;
            var accessors = parsed?.Root?.accessors;
            var bufferViews = parsed?.Root?.bufferViews;
            if (accessors == null || bufferViews == null)
                return false;
            if (accessorIndex < 0 || accessorIndex >= accessors.Count)
                return false;

            var accessor = accessors[accessorIndex];
            if (accessor == null || accessor.bufferView < 0 || accessor.bufferView >= bufferViews.Count)
                return false;
            if (!string.Equals(accessor.type, "SCALAR", StringComparison.OrdinalIgnoreCase))
                return false;
            if (accessor.componentType != 5126) // FLOAT
                return false;
            if (accessor.count <= 0)
                return false;

            var view = bufferViews[accessor.bufferView];
            if (view == null)
                return false;

            int stride = view.byteStride > 0 ? view.byteStride : 4;
            int baseOffset = view.byteOffset + accessor.byteOffset;
            int count = accessor.count;
            int required = baseOffset + ((count - 1) * stride) + 4;
            if (required > parsed.Bin.Length || baseOffset < 0)
                return false;

            values = new float[count];
            for (int i = 0; i < count; i++)
            {
                int offset = baseOffset + (i * stride);
                values[i] = BitConverter.ToSingle(parsed.Bin, offset);
            }
            return true;
        }

        private static bool TryWriteSanitizedGlbCopy(string sourcePath, string aoDataRoot, out string sanitizedPath)
        {
            sanitizedPath = null;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return false;

            byte[] bytes = File.ReadAllBytes(sourcePath);
            if (bytes.Length < 20)
                return false;

            // Parse original first (for accessor walking and quick validity checks).
            var parsed = ParseGlb(sourcePath);
            if (parsed?.Root?.animations == null || parsed.Root.animations.Count == 0)
                return false;

            // Mutate a copy of BIN bytes and write them back into the GLB.
            byte[] mutatedBin = parsed.Bin != null ? (byte[])parsed.Bin.Clone() : Array.Empty<byte>();
            bool changed = false;
            var touchedAccessors = new HashSet<int>();

            for (int a = 0; a < parsed.Root.animations.Count; a++)
            {
                var anim = parsed.Root.animations[a];
                if (anim?.samplers == null)
                    continue;

                for (int s = 0; s < anim.samplers.Count; s++)
                {
                    var sampler = anim.samplers[s];
                    if (sampler == null || sampler.input < 0)
                        continue;
                    if (!touchedAccessors.Add(sampler.input))
                        continue;

                    if (!TryGetAccessorBufferLayout(parsed, sampler.input, out int baseOffset, out int stride, out int count))
                        continue;
                    if (count <= 1)
                        continue;

                    float prev = BitConverter.ToSingle(mutatedBin, baseOffset);
                    for (int i = 1; i < count; i++)
                    {
                        int offset = baseOffset + (i * stride);
                        float current = BitConverter.ToSingle(mutatedBin, offset);
                        if (!(current > prev))
                        {
                            current = prev + 0.0001f;
                            WriteFloat(mutatedBin, offset, current);
                            changed = true;
                        }
                        prev = current;
                    }
                }
            }

            if (!changed)
                return false;

            if (!TryReplaceGlbBinChunk(bytes, mutatedBin, out var updatedBytes))
                return false;

            string rel = Path.GetRelativePath(aoDataRoot, sourcePath);
            string outPath = Path.Combine(aoDataRoot, "RuntimeGlbSanitized", rel);
            string outDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrWhiteSpace(outDir))
                Directory.CreateDirectory(outDir);
            File.WriteAllBytes(outPath, updatedBytes);
            sanitizedPath = outPath;
            return true;
        }

        private static bool TryGetAccessorBufferLayout(ParsedGlb parsed, int accessorIndex, out int baseOffset, out int stride, out int count)
        {
            baseOffset = 0;
            stride = 0;
            count = 0;

            var accessors = parsed?.Root?.accessors;
            var views = parsed?.Root?.bufferViews;
            if (accessors == null || views == null)
                return false;
            if (accessorIndex < 0 || accessorIndex >= accessors.Count)
                return false;

            var accessor = accessors[accessorIndex];
            if (accessor == null || accessor.bufferView < 0 || accessor.bufferView >= views.Count)
                return false;
            if (!string.Equals(accessor.type, "SCALAR", StringComparison.OrdinalIgnoreCase))
                return false;
            if (accessor.componentType != 5126)
                return false;
            if (accessor.count <= 0)
                return false;

            var view = views[accessor.bufferView];
            if (view == null)
                return false;

            stride = view.byteStride > 0 ? view.byteStride : 4;
            baseOffset = view.byteOffset + accessor.byteOffset;
            count = accessor.count;

            int required = baseOffset + ((count - 1) * stride) + 4;
            if (parsed.Bin == null || baseOffset < 0 || required > parsed.Bin.Length)
                return false;

            return true;
        }

        private static void WriteFloat(byte[] buffer, int offset, float value)
        {
            var bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, buffer, offset, 4);
        }

        private static bool TryReplaceGlbBinChunk(byte[] originalGlb, byte[] updatedBin, out byte[] outputGlb)
        {
            outputGlb = null;
            if (originalGlb == null || originalGlb.Length < 20)
                return false;

            uint magic = BitConverter.ToUInt32(originalGlb, 0);
            if (magic != GlbMagic)
                return false;

            int offset = 12;
            while (offset + 8 <= originalGlb.Length)
            {
                uint chunkLength = BitConverter.ToUInt32(originalGlb, offset);
                uint chunkType = BitConverter.ToUInt32(originalGlb, offset + 4);
                int chunkDataOffset = offset + 8;
                int next = chunkDataOffset + (int)chunkLength;
                if (next > originalGlb.Length)
                    return false;

                if (chunkType == BinChunkType)
                {
                    if ((int)chunkLength != updatedBin.Length)
                        return false;
                    outputGlb = (byte[])originalGlb.Clone();
                    Buffer.BlockCopy(updatedBin, 0, outputGlb, chunkDataOffset, updatedBin.Length);
                    return true;
                }

                offset = next;
            }

            return false;
        }
    }
}
