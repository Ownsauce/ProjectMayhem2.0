using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AO.Assets.Resolution;
using AO.Assets.ResourceDatabase;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace AO.Unity.Assets
{
    public static class AOCharacterMeshResolver
    {
        private const string ConverterVersion = "aogltf-cir-v2-attractors";
        private const int ConverterTimeoutMilliseconds = 120000;
        private static readonly Dictionary<string, int> MeshIds =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "solitus_male", 5907 },
                { "solitus_female", 5927 },
                { "opifex_male", 5914 },
                { "opifex_female", 5934 },
                { "nanomage_male", 5921 },
                { "nanomage_female", 5941 },
                { "athrox_male", 5900 }
            };
        private static readonly Dictionary<uint, int> FallbackBodyByAppearance =
            new Dictionary<uint, int>
            {
                { 1320, 5921 }, { 1321, 5921 },
                { 1416, 5900 }, { 1576, 5907 }, { 1608, 5914 },
                { 1832, 5927 }, { 1864, 5934 }, { 1896, 5941 }
            };
        private static readonly Dictionary<uint, int> FallbackHeadByAppearance =
            new Dictionary<uint, int>
            {
                { 1320, 40173 }, { 1321, 40173 },
                { 1416, 40111 }, { 1576, 40694 }, { 1608, 40249 },
                { 1832, 40629 }, { 1864, 40209 }, { 1896, 40151 }
            };

        private static readonly object Sync = new object();
        private static readonly HashSet<int> CirMeshIds = new HashSet<int>(MeshIds.Values);
        private static readonly Dictionary<uint, Task<int>> MonsterCirLookups =
            new Dictionary<uint, Task<int>>();
        private static readonly Dictionary<uint, Task<int>> MonsterHeadLookups =
            new Dictionary<uint, Task<int>>();
        private static AOInstallAssetResolver _resolver;
        private static string _resolverInstallKey = string.Empty;

        public static async Task<string> ResolveAsync(string meshName,
            CancellationToken cancellationToken = default)
        {
            AOInstallValidation install = AOInstallConfiguration.GetConfiguredInstall();
            if (install == null || !install.IsValid)
            {
                UnityEngine.Debug.LogWarning("Character CIR extraction requires a valid AO installation in F10 > AO Assets.");
                return string.Empty;
            }

            if (!TryResolveMeshId(install, meshName, out int meshId))
            {
                UnityEngine.Debug.LogWarning($"AO character mesh '{meshName}' was not found in the configured installation.");
                return string.Empty;
            }

            AOInstallAssetResolver resolver = GetResolver(install);
            AOResolvedAsset result = await resolver.ResolveAsync(
                new AOAssetRequest(AOAssetKind.Mesh, meshId, "glb"),
                cancellationToken);
            if (!result.Succeeded)
            {
                UnityEngine.Debug.LogWarning($"Could not resolve AO character mesh '{meshName}' ({meshId}): {result.Error}");
                return string.Empty;
            }

            UnityEngine.Debug.Log(result.CacheHit
                ? $"Loaded AO character mesh '{meshName}' from the local cache."
                : $"Extracted AO character mesh '{meshName}' from the configured AO installation.");
            return result.Path;
        }

        public static async Task<string> ResolveMonsterAsync(uint monsterData,
            uint appearance = 0,
            CancellationToken cancellationToken = default)
        {
            if (monsterData == 0 || monsterData > int.MaxValue)
                return string.Empty;

            AOInstallValidation install = AOInstallConfiguration.GetConfiguredInstall();
            if (install == null || !install.IsValid)
            {
                UnityEngine.Debug.LogWarning("NPC mesh extraction requires a valid AO installation in F10 > AO Assets.");
                return string.Empty;
            }

            Task<int> lookup;
            lock (Sync)
            {
                if (!MonsterCirLookups.TryGetValue(monsterData, out lookup))
                {
                    lookup = ResolveMonsterCirIdAsync(
                        install.RootPath, (int)monsterData, CancellationToken.None);
                    MonsterCirLookups[monsterData] = lookup;
                }
            }
            int cirId = await lookup;
            cancellationToken.ThrowIfCancellationRequested();
            if (cirId <= 0 && !FallbackBodyByAppearance.TryGetValue(appearance, out cirId))
            {
                lock (Sync)
                    MonsterCirLookups.Remove(monsterData);
                UnityEngine.Debug.LogWarning($"MonsterData {monsterData} did not resolve to an AO CIR mesh.");
                return string.Empty;
            }

            lock (Sync)
                CirMeshIds.Add(cirId);

            AOResolvedAsset result = await GetResolver(install).ResolveAsync(
                new AOAssetRequest(AOAssetKind.Mesh, cirId, "glb"), cancellationToken);
            if (!result.Succeeded)
            {
                UnityEngine.Debug.LogWarning(
                    $"Could not resolve MonsterData {monsterData} CIR {cirId}: {result.Error}");
                return string.Empty;
            }

            UnityEngine.Debug.Log(result.CacheHit
                ? $"Loaded MonsterData {monsterData} CIR {cirId} from the local cache."
                : $"Extracted MonsterData {monsterData} CIR {cirId} from the configured AO installation.");
            return result.Path;
        }

        public static async Task<string> ResolveMonsterHeadAsync(uint monsterData,
            uint appearance = 0,
            CancellationToken cancellationToken = default)
        {
            if (monsterData == 0 || monsterData > int.MaxValue)
                return string.Empty;

            AOInstallValidation install = AOInstallConfiguration.GetConfiguredInstall();
            if (install == null || !install.IsValid)
                return string.Empty;

            Task<int> lookup;
            lock (Sync)
            {
                if (!MonsterHeadLookups.TryGetValue(monsterData, out lookup))
                {
                    lookup = ResolveMonsterHeadIdAsync(
                        install.RootPath, (int)monsterData, CancellationToken.None);
                    MonsterHeadLookups[monsterData] = lookup;
                }
            }

            int headId = await lookup;
            cancellationToken.ThrowIfCancellationRequested();
            if (headId <= 0 && !FallbackHeadByAppearance.TryGetValue(appearance, out headId))
                return string.Empty;

            AOResolvedAsset result = await GetResolver(install).ResolveAsync(
                new AOAssetRequest(AOAssetKind.Mesh, headId, "glb"), cancellationToken);
            if (!result.Succeeded)
            {
                UnityEngine.Debug.LogWarning(
                    $"Could not resolve MonsterData {monsterData} head mesh {headId}: {result.Error}");
                return string.Empty;
            }
            return result.Path;
        }

        private static Task<int> ResolveMonsterHeadIdAsync(string aoInstallationRoot,
            int monsterData, CancellationToken cancellationToken)
        {
            return RunMonsterLookupAsync(aoInstallationRoot, monsterData, output =>
            {
                using (var reader = new StringReader(output ?? string.Empty))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] columns = line.Split('\t');
                        if (columns.Length >= 3 && columns[0] == "64"
                            && int.TryParse(columns[2], out int headId))
                            return headId;
                    }
                }
                return 0;
            }, cancellationToken);
        }

        private static Task<int> RunMonsterLookupAsync(string aoInstallationRoot,
            int monsterData, Func<string, int> parseOutput,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                string executable = AOGLTFCirProcessConverter.ResolveExecutablePath();
                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                    return 0;
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--monster " + AOGLTFCirProcessConverter.Quote(aoInstallationRoot)
                        + " " + monsterData,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return 0;
                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();
                    int elapsedMilliseconds = 0;
                    while (!process.WaitForExit(100))
                    {
                        elapsedMilliseconds += 100;
                        if (cancellationToken.IsCancellationRequested
                            || elapsedMilliseconds >= ConverterTimeoutMilliseconds)
                        {
                            try { process.Kill(); } catch { }
                            cancellationToken.ThrowIfCancellationRequested();
                            return 0;
                        }
                    }
                    Task.WaitAll(outputTask, errorTask);
                    return process.ExitCode == 0 ? parseOutput(outputTask.Result) : 0;
                }
            }, cancellationToken);
        }

        private static Task<int> ResolveMonsterCirIdAsync(string aoInstallationRoot,
            int monsterData, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                string executable = AOGLTFCirProcessConverter.ResolveExecutablePath();
                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                    return 0;
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--monster-mesh " + AOGLTFCirProcessConverter.Quote(aoInstallationRoot)
                        + " " + monsterData,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return 0;
                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();
                    int elapsedMilliseconds = 0;
                    while (!process.WaitForExit(100))
                    {
                        elapsedMilliseconds += 100;
                        if (cancellationToken.IsCancellationRequested
                            || elapsedMilliseconds >= ConverterTimeoutMilliseconds)
                        {
                            try { process.Kill(); } catch { }
                            cancellationToken.ThrowIfCancellationRequested();
                            return 0;
                        }
                    }
                    Task.WaitAll(outputTask, errorTask);
                    string output = outputTask.Result.Trim();
                    return process.ExitCode == 0 && int.TryParse(output, out int id) ? id : 0;
                }
            }, cancellationToken);
        }

        private static bool TryResolveMeshId(AOInstallValidation install,
            string meshName, out int meshId)
        {
            string normalized = Path.GetFileNameWithoutExtension(meshName ?? string.Empty);
            if (MeshIds.TryGetValue(normalized, out meshId))
                return true;

            using (var database = new AOResourceDatabase(install.RootPath))
            {
                AOResourceCatalog catalog = AOResourceCatalog.Load(database);
                foreach (var pair in catalog.GetResources(AOResourceTypes.Mesh))
                {
                    string candidate = Path.GetFileNameWithoutExtension(pair.Value ?? string.Empty);
                    if (!string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                        continue;
                    meshId = pair.Key;
                    return true;
                }
            }

            meshId = 0;
            return false;
        }

        private static AOInstallAssetResolver GetResolver(AOInstallValidation install)
        {
            string key = install.RootPath + "|" + install.DatabaseFingerprint;
            lock (Sync)
            {
                if (_resolver == null
                    || !string.Equals(_resolverInstallKey, key, StringComparison.Ordinal))
                {
                    _resolver = new AOInstallAssetResolver(
                        install,
                        AOInstallConfiguration.CacheRoot,
                        new AOGLTFCirProcessConverter());
                    _resolverInstallKey = key;
                }
                return _resolver;
            }
        }

        private sealed class AOGLTFCirProcessConverter : IAOAssetConverter
        {
            public string ConverterVersion => AOCharacterMeshResolver.ConverterVersion;

            public Task ConvertAsync(string aoInstallationRoot, AOAssetRequest request,
                string destinationPath, CancellationToken cancellationToken)
            {
                return Task.Run(() => Convert(
                    aoInstallationRoot, request, destinationPath, cancellationToken),
                    cancellationToken);
            }

            private static void Convert(string aoInstallationRoot, AOAssetRequest request,
                string destinationPath, CancellationToken cancellationToken)
            {
                if (request.Kind != AOAssetKind.Mesh
                    || !string.Equals(request.OutputExtension, "glb", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("The CIR converter only supports GLB character meshes.");

                string executable = ResolveExecutablePath();
                if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                    throw new FileNotFoundException(
                        "The bundled AOGLTF CIR converter is unavailable for this platform.", executable);

                string outputFolder = destinationPath + ".work";
                if (Directory.Exists(outputFolder))
                    Directory.Delete(outputFolder, true);
                Directory.CreateDirectory(outputFolder);
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = (IsCirMesh(request.ResourceId) ? "--cir " : "--mesh ")
                            + Quote(aoInstallationRoot) + " "
                            + request.ResourceId + " " + Quote(outputFolder),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (var process = Process.Start(startInfo))
                    {
                        if (process == null)
                            throw new InvalidOperationException("The AOGLTF CIR converter did not start.");
                        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                        Task<string> errorTask = process.StandardError.ReadToEndAsync();
                        int elapsedMilliseconds = 0;
                        while (!process.WaitForExit(100))
                        {
                            elapsedMilliseconds += 100;
                            if (cancellationToken.IsCancellationRequested
                                || elapsedMilliseconds >= ConverterTimeoutMilliseconds)
                            {
                                try { process.Kill(); } catch { }
                                cancellationToken.ThrowIfCancellationRequested();
                                throw new TimeoutException(
                                    "AOGLTF CIR conversion exceeded the two-minute timeout.");
                            }
                        }

                        Task.WaitAll(outputTask, errorTask);
                        string standardOutput = outputTask.Result;
                        string standardError = errorTask.Result;
                        if (process.ExitCode != 0)
                            throw new InvalidOperationException(
                                "AOGLTF CIR conversion failed: "
                                + (string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError));
                    }

                    string generated = Directory.EnumerateFiles(outputFolder, "*.glb")
                        .SingleOrDefault();
                    if (string.IsNullOrWhiteSpace(generated))
                        throw new InvalidDataException("AOGLTF did not produce a character GLB.");
                    File.Move(generated, destinationPath);
                }
                finally
                {
                    if (Directory.Exists(outputFolder))
                        Directory.Delete(outputFolder, true);
                }
            }

            internal static string ResolveExecutablePath()
            {
                if (Application.platform == RuntimePlatform.LinuxEditor
                    || Application.platform == RuntimePlatform.LinuxPlayer)
                {
                    return Path.Combine(Application.streamingAssetsPath,
                        "Tools", "AOGLTF", "linux-x64", "aogltf.cli");
                }
                return string.Empty;
            }

            internal static string Quote(string value)
            {
                return "\"" + (value ?? string.Empty)
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"") + "\"";
            }
        }

        private static bool IsCirMesh(int resourceId)
        {
            lock (Sync)
                return CirMeshIds.Contains(resourceId);
        }
    }
}
