using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace AO.Unity.World
{
    internal static class GlbDataUriLoadPathResolver
    {
        private const uint GlbMagic = 0x46546C67; // glTF
        private const uint JsonChunkType = 0x4E4F534A; // JSON
        private static readonly Regex DataUriRegex = new(
            "\"uri\"\\s*:\\s*\"(?<uri>data:[^\"]+)\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Dictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> SanitizedByOriginal = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> LoggedResolutions = new(StringComparer.OrdinalIgnoreCase);
        private static bool _sanitizedMapLoaded;

        [Serializable]
        private sealed class SanitizedMapFile
        {
            public int Version = 1;
            public System.Collections.Generic.List<SanitizedMapEntry> Entries = new();
        }

        [Serializable]
        private sealed class SanitizedMapEntry
        {
            public string OriginalPath;
            public string SanitizedPath;
        }

        public static string Resolve(string sourcePath, bool enabled)
        {
            if (!enabled || string.IsNullOrWhiteSpace(sourcePath))
                return sourcePath;

            string resolvedPath = sourcePath;
            if (Uri.TryCreate(sourcePath, UriKind.Absolute, out var sourceUri) && sourceUri.IsFile)
            {
                resolvedPath = sourceUri.LocalPath;
            }

            bool isGlb = resolvedPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
            bool isGltf = resolvedPath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase);
            if (!isGlb && !isGltf)
                return sourcePath;

            resolvedPath = NormalizePathSafe(resolvedPath);
            if (!File.Exists(resolvedPath))
                return sourcePath;

            try
            {
                string originalNormalized = resolvedPath;
                if (isGlb && TryResolveSanitizedPath(resolvedPath, out string mappedSanitized))
                    resolvedPath = mappedSanitized;

                string cacheKey = BuildCacheKey(resolvedPath);
                if (Cache.TryGetValue(cacheKey, out string cachedPath)
                    && !string.IsNullOrWhiteSpace(cachedPath)
                    && File.Exists(cachedPath))
                {
                    LogResolutionOnce(originalNormalized, cachedPath);
                    return cachedPath;
                }

                string patchedPath;
                bool patched = isGlb
                    ? TryWriteSanitizedGlbCopy(resolvedPath, cacheKey, out patchedPath)
                    : TryWriteSanitizedGltfCopy(resolvedPath, cacheKey, out patchedPath);
                if (!patched)
                {
                    Cache[cacheKey] = resolvedPath;
                    LogResolutionOnce(originalNormalized, resolvedPath);
                    return resolvedPath;
                }

                Cache[cacheKey] = patchedPath;
                LogResolutionOnce(originalNormalized, patchedPath);
                return patchedPath;
            }
            catch
            {
                // Preserve best-known resolved path (including sanitized-map swap) on fallback.
                return resolvedPath;
            }
        }

        private static void LogResolutionOnce(string originalPath, string resolvedPath)
        {
            try
            {
                string original = NormalizePathSafe(originalPath);
                string resolved = NormalizePathSafe(resolvedPath);
                if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(resolved))
                    return;

                string key = $"{original}|{resolved}";
                if (!LoggedResolutions.Add(key))
                    return;

                string kind = string.Equals(original, resolved, StringComparison.OrdinalIgnoreCase)
                    ? "original"
                    : "resolved";
                UnityEngine.Debug.Log($"[GLB Resolve] {kind}: {original} -> {resolved}");
            }
            catch
            {
            }
        }

        private static bool TryResolveSanitizedPath(string originalPath, out string sanitizedPath)
        {
            sanitizedPath = null;
            LoadSanitizedMapIfNeeded();

            if (string.IsNullOrWhiteSpace(originalPath) || SanitizedByOriginal.Count == 0)
                return false;

            string normalizedOriginal = NormalizePathSafe(originalPath);
            if (SanitizedByOriginal.TryGetValue(normalizedOriginal, out var mapped)
                && !string.IsNullOrWhiteSpace(mapped))
            {
                string normalizedMapped = NormalizePathSafe(mapped);
                if (File.Exists(normalizedMapped))
                {
                    sanitizedPath = normalizedMapped;
                    return true;
                }
            }

            return false;
        }

        private static void LoadSanitizedMapIfNeeded()
        {
            if (_sanitizedMapLoaded)
                return;
            _sanitizedMapLoaded = true;

            try
            {
                // Application.dataPath == <Project>/AO.Unity/Assets
                string aoDataRoot = Path.Combine(UnityEngine.Application.dataPath, "StreamingAssets", "AOData");
                string mapPath = Path.Combine(aoDataRoot, "runtime_glb_sanitized_map.json");
                if (!File.Exists(mapPath))
                    return;

                var parsed = JsonConvert.DeserializeObject<SanitizedMapFile>(File.ReadAllText(mapPath));
                if (parsed?.Entries == null)
                    return;

                for (int i = 0; i < parsed.Entries.Count; i++)
                {
                    var e = parsed.Entries[i];
                    if (e == null || string.IsNullOrWhiteSpace(e.OriginalPath) || string.IsNullOrWhiteSpace(e.SanitizedPath))
                        continue;

                    string original = NormalizePathSafe(e.OriginalPath);
                    string sanitized = NormalizePathSafe(e.SanitizedPath);
                    if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(sanitized))
                        continue;
                    SanitizedByOriginal[original] = sanitized;
                }
            }
            catch
            {
            }
        }

        private static string NormalizePathSafe(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            try
            {
                return Path.GetFullPath(path).Replace('\\', '/');
            }
            catch
            {
                return path.Replace('\\', '/');
            }
        }

        private static string BuildCacheKey(string sourcePath)
        {
            var info = new FileInfo(sourcePath);
            string stamp = $"{sourcePath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            using var sha1 = SHA1.Create();
            byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(stamp));
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        private static bool TryWriteSanitizedGlbCopy(string sourcePath, string cacheKey, out string patchedPath)
        {
            patchedPath = null;
            byte[] sourceBytes;
            try
            {
                sourceBytes = File.ReadAllBytes(sourcePath);
            }
            catch
            {
                return false;
            }

            if (!TryGetJsonChunk(sourceBytes, out int jsonOffset, out int jsonLength, out int jsonChunkLength))
                return false;

            string json = Encoding.UTF8.GetString(sourceBytes, jsonOffset, jsonLength).TrimEnd('\0', ' ', '\t', '\r', '\n');
            if (string.IsNullOrWhiteSpace(json) || json.IndexOf("data:", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            string cacheRoot = Path.Combine(Path.GetTempPath(), "AOUnity", "PatchedGlbDataUri", cacheKey);
            Directory.CreateDirectory(cacheRoot);

            int fileIndex = 0;
            string patchedJson = DataUriRegex.Replace(json, match =>
            {
                string uri = match.Groups["uri"].Value;
                if (!TryDecodeDataUri(uri, out byte[] data, out string extension))
                    return match.Value;

                string fileName = $"datauri_{fileIndex++}{extension}";
                string outPath = Path.Combine(cacheRoot, fileName);
                try
                {
                    File.WriteAllBytes(outPath, data);
                    string absoluteUri = new Uri(outPath).AbsoluteUri;
                    return $"\"uri\":\"{absoluteUri}\"";
                }
                catch
                {
                    return match.Value;
                }
            });

            if (fileIndex == 0)
                return false;

            byte[] patchedJsonBytes = Encoding.UTF8.GetBytes(patchedJson);
            int patchedJsonChunkLength = Align4(patchedJsonBytes.Length);
            byte[] paddedJsonBytes = new byte[patchedJsonChunkLength];
            Buffer.BlockCopy(patchedJsonBytes, 0, paddedJsonBytes, 0, patchedJsonBytes.Length);
            for (int i = patchedJsonBytes.Length; i < paddedJsonBytes.Length; i++)
                paddedJsonBytes[i] = 0x20; // JSON chunk padding uses spaces.

            int jsonChunkHeaderOffset = jsonOffset - 8;
            int nextChunkOffset = jsonOffset + jsonChunkLength;
            if (jsonChunkHeaderOffset < 12 || nextChunkOffset > sourceBytes.Length)
                return false;

            int newLength = sourceBytes.Length - jsonChunkLength + patchedJsonChunkLength;
            byte[] output = new byte[newLength];

            // GLB header
            Buffer.BlockCopy(sourceBytes, 0, output, 0, 12);
            WriteUInt32(output, 8, (uint)newLength);

            // JSON chunk header
            WriteUInt32(output, jsonChunkHeaderOffset, (uint)patchedJsonChunkLength);
            WriteUInt32(output, jsonChunkHeaderOffset + 4, JsonChunkType);

            // JSON chunk body
            Buffer.BlockCopy(paddedJsonBytes, 0, output, jsonOffset, paddedJsonBytes.Length);

            // Remaining chunks
            int outputTailOffset = jsonOffset + patchedJsonChunkLength;
            int remainingLength = sourceBytes.Length - nextChunkOffset;
            if (remainingLength > 0)
                Buffer.BlockCopy(sourceBytes, nextChunkOffset, output, outputTailOffset, remainingLength);

            patchedPath = Path.Combine(cacheRoot, $"{Path.GetFileNameWithoutExtension(sourcePath)}_patched.glb");
            File.WriteAllBytes(patchedPath, output);
            return true;
        }

        private static bool TryWriteSanitizedGltfCopy(string sourcePath, string cacheKey, out string patchedPath)
        {
            patchedPath = null;
            string sourceText;
            try
            {
                sourceText = File.ReadAllText(sourcePath);
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(sourceText) || sourceText.IndexOf("data:", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            string cacheRoot = Path.Combine(Path.GetTempPath(), "AOUnity", "PatchedGltfDataUri", cacheKey);
            Directory.CreateDirectory(cacheRoot);

            int fileIndex = 0;
            string patchedText = DataUriRegex.Replace(sourceText, match =>
            {
                string uri = match.Groups["uri"].Value;
                if (!TryDecodeDataUri(uri, out byte[] data, out string extension))
                    return match.Value;

                string fileName = $"datauri_{fileIndex++}{extension}";
                string outPath = Path.Combine(cacheRoot, fileName);
                try
                {
                    File.WriteAllBytes(outPath, data);
                    string absoluteUri = new Uri(outPath).AbsoluteUri;
                    return $"\"uri\":\"{absoluteUri}\"";
                }
                catch
                {
                    return match.Value;
                }
            });

            if (fileIndex == 0)
                return false;

            patchedPath = Path.Combine(cacheRoot, $"{Path.GetFileNameWithoutExtension(sourcePath)}_patched.gltf");
            File.WriteAllText(patchedPath, patchedText);
            return true;
        }

        private static bool TryGetJsonChunk(byte[] bytes, out int jsonOffset, out int jsonLength, out int jsonChunkLength)
        {
            jsonOffset = 0;
            jsonLength = 0;
            jsonChunkLength = 0;

            if (bytes == null || bytes.Length < 20)
                return false;
            if (BitConverter.ToUInt32(bytes, 0) != GlbMagic)
                return false;

            int offset = 12;
            while (offset + 8 <= bytes.Length)
            {
                int chunkLen = (int)BitConverter.ToUInt32(bytes, offset);
                uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);
                int chunkDataOffset = offset + 8;
                if (chunkLen < 0 || chunkDataOffset + chunkLen > bytes.Length)
                    return false;

                if (chunkType == JsonChunkType)
                {
                    jsonOffset = chunkDataOffset;
                    jsonLength = chunkLen;
                    jsonChunkLength = chunkLen;
                    return true;
                }

                offset = chunkDataOffset + chunkLen;
            }

            return false;
        }

        private static int Align4(int value)
        {
            int rem = value & 3;
            return rem == 0 ? value : value + (4 - rem);
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
            bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
            bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static bool TryDecodeDataUri(string uri, out byte[] data, out string extension)
        {
            data = null;
            extension = ".bin";
            if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return false;

            int comma = uri.IndexOf(',');
            if (comma <= 5 || comma >= uri.Length - 1)
                return false;

            string meta = uri.Substring(5, comma - 5);
            string payload = uri[(comma + 1)..];
            bool isBase64 = meta.IndexOf(";base64", StringComparison.OrdinalIgnoreCase) >= 0;
            string mime = meta.Split(';')[0].Trim();

            extension = mime switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/jpg" => ".jpg",
                "image/webp" => ".webp",
                "application/octet-stream" => ".bin",
                _ => ".bin"
            };

            try
            {
                data = isBase64
                    ? Convert.FromBase64String(payload)
                    : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
                return data != null && data.Length > 0;
            }
            catch
            {
                data = null;
                return false;
            }
        }

    }
}
