using System;
using System.IO;
using AO.Assets.Resolution;
using AO.Assets.ResourceDatabase;

namespace AO.Assets.Cache
{
    public sealed class AOAssetCache
    {
        public AOAssetCache(string cacheRoot, AOInstallValidation install, string converterVersion)
        {
            if (string.IsNullOrWhiteSpace(cacheRoot))
                throw new ArgumentException("A cache root is required.", nameof(cacheRoot));
            CacheRoot = cacheRoot;
            SourceKey = Sanitize((install?.ClientVersion ?? "unknown") + "_"
                + (install?.DatabaseFingerprint ?? "unknown") + "_"
                + (converterVersion ?? "unknown"));
        }

        public string CacheRoot { get; }
        public string SourceKey { get; }

        public string GetPath(AOAssetRequest request)
        {
            string folder = request.Kind.ToString().ToLowerInvariant();
            string extension = string.IsNullOrEmpty(request.OutputExtension)
                ? "bin" : request.OutputExtension;
            return Path.Combine(CacheRoot, SourceKey, folder,
                request.ResourceId + "." + extension);
        }

        public bool TryGet(AOAssetRequest request, out string path)
        {
            path = GetPath(request);
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }

        private static string Sanitize(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value.Replace(' ', '_');
        }
    }
}
