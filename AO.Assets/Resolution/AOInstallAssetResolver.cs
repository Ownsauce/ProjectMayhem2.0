using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AO.Assets.Cache;
using AO.Assets.ResourceDatabase;

namespace AO.Assets.Resolution
{
    public sealed class AOInstallAssetResolver : IAOAssetResolver
    {
        private readonly AOInstallValidation _install;
        private readonly AOAssetCache _cache;
        private readonly IAOAssetConverter _converter;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
            new ConcurrentDictionary<string, SemaphoreSlim>();

        public AOInstallAssetResolver(AOInstallValidation install, string cacheRoot,
            IAOAssetConverter converter)
        {
            _install = install ?? throw new ArgumentNullException(nameof(install));
            _converter = converter ?? throw new ArgumentNullException(nameof(converter));
            _cache = new AOAssetCache(cacheRoot, install, converter.ConverterVersion);
        }

        public async Task<AOResolvedAsset> ResolveAsync(AOAssetRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null) return AOResolvedAsset.Failure("An asset request is required.");
            if (!_install.IsValid) return AOResolvedAsset.Failure("The configured AO installation is invalid.");
            if (_cache.TryGet(request, out string cached))
                return AOResolvedAsset.Success(cached, true);

            string destination = _cache.GetPath(request);
            SemaphoreSlim gate = _locks.GetOrAdd(destination, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_cache.TryGet(request, out cached))
                    return AOResolvedAsset.Success(cached, true);
                string directory = Path.GetDirectoryName(destination);
                Directory.CreateDirectory(directory);
                string temporary = destination + ".partial";
                try
                {
                    await _converter.ConvertAsync(_install.RootPath, request, temporary,
                        cancellationToken).ConfigureAwait(false);
                    if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                        return AOResolvedAsset.Failure("The converter produced no output.");
                    File.Move(temporary, destination);
                    return AOResolvedAsset.Success(destination, false);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                return AOResolvedAsset.Failure(exception.Message);
            }
            finally { gate.Release(); }
        }
    }
}
