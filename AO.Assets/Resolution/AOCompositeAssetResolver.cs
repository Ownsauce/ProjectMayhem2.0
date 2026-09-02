using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AO.Assets.Resolution
{
    public sealed class AOCompositeAssetResolver : IAOAssetResolver
    {
        private readonly IReadOnlyList<IAOAssetResolver> _sources;

        public AOCompositeAssetResolver(params IAOAssetResolver[] sources)
        {
            if (sources == null || sources.Length == 0)
                throw new ArgumentException("At least one asset source is required.",
                    nameof(sources));
            _sources = sources;
        }

        public async Task<AOResolvedAsset> ResolveAsync(AOAssetRequest request,
            CancellationToken cancellationToken = default)
        {
            string lastError = "The asset was not found in any configured source.";
            foreach (IAOAssetResolver source in _sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (source == null) continue;
                AOResolvedAsset result = await source.ResolveAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                if (result.Succeeded) return result;
                if (!string.IsNullOrWhiteSpace(result.Error)) lastError = result.Error;
            }
            return AOResolvedAsset.Failure(lastError);
        }
    }

    public sealed class AODirectoryAssetResolver : IAOAssetResolver
    {
        public AODirectoryAssetResolver(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("An override root is required.", nameof(rootPath));
            RootPath = Path.GetFullPath(rootPath);
        }

        public string RootPath { get; }

        public Task<AOResolvedAsset> ResolveAsync(AOAssetRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null)
                return Task.FromResult(AOResolvedAsset.Failure("An asset request is required."));
            if (!IsSafeExtension(request.OutputExtension))
                return Task.FromResult(AOResolvedAsset.Failure("The asset extension is invalid."));

            string extension = string.IsNullOrEmpty(request.OutputExtension)
                ? "bin" : request.OutputExtension;
            string path = Path.Combine(RootPath, request.Kind.ToString().ToLowerInvariant(),
                request.ResourceId + "." + extension);
            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return Task.FromResult(AOResolvedAsset.Success(path, true));
            return Task.FromResult(AOResolvedAsset.Failure("The override asset was not found."));
        }

        private static bool IsSafeExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return true;
            foreach (char value in extension)
                if (!char.IsLetterOrDigit(value)) return false;
            return extension.Length <= 16;
        }
    }
}
