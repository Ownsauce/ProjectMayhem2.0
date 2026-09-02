using System.Threading;
using System.Threading.Tasks;

namespace AO.Assets.Resolution
{
    public enum AOAssetKind { Mesh, Texture, Animation, Audio, Playfield }

    public sealed class AOAssetRequest
    {
        public AOAssetRequest(AOAssetKind kind, int resourceId, string outputExtension)
        {
            Kind = kind;
            ResourceId = resourceId;
            OutputExtension = outputExtension?.TrimStart('.') ?? string.Empty;
        }
        public AOAssetKind Kind { get; }
        public int ResourceId { get; }
        public string OutputExtension { get; }
    }

    public sealed class AOResolvedAsset
    {
        private AOResolvedAsset(bool succeeded, string path, bool cacheHit, string error)
        { Succeeded = succeeded; Path = path; CacheHit = cacheHit; Error = error; }
        public bool Succeeded { get; }
        public string Path { get; }
        public bool CacheHit { get; }
        public string Error { get; }
        public static AOResolvedAsset Success(string path, bool cacheHit) =>
            new AOResolvedAsset(true, path, cacheHit, string.Empty);
        public static AOResolvedAsset Failure(string error) =>
            new AOResolvedAsset(false, string.Empty, false, error);
    }

    public interface IAOAssetResolver
    {
        Task<AOResolvedAsset> ResolveAsync(AOAssetRequest request,
            CancellationToken cancellationToken = default);
    }

    public interface IAOAssetConverter
    {
        string ConverterVersion { get; }
        Task ConvertAsync(string aoInstallationRoot, AOAssetRequest request,
            string destinationPath, CancellationToken cancellationToken);
    }
}
