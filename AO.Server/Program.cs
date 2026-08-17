using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AO.Server.Transport;

namespace AO.Server
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            ServerDataPaths paths = ResolvePaths(args);
            var data = AuthoritativeGameData.Load(paths);
            var server = new AuthoritativeGameServer(data);
            int port = ResolvePort(args);
            string host = GetArg(args, "--host") ?? "127.0.0.1";
            var ipAddress = IPAddress.Parse(host);

            Console.WriteLine($"[AO.Server] Loaded authoritative data from {paths.AoDataRoot}");
            Console.WriteLine($"[AO.Server] Server-owned authoritative AOData from {paths.ServerAoDataRoot}");
            Console.WriteLine($"[AO.Server] Server-owned playfield runtime data from {paths.ServerPlayfieldsRoot}");
            Console.WriteLine($"[AO.Server] Professions={data.Professions.Count}, Items={data.Items.Count}, Nanos={data.Nanos.Count}");
            Console.WriteLine($"[AO.Server] Starting TCP host on {host}:{port}");

            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };

            await using var hostServer = new TcpJsonGameServerHost(server, ipAddress, port);
            await hostServer.StartAsync(shutdown.Token);

            Console.WriteLine("[AO.Server] Authoritative transport online. Press Ctrl+C to stop.");

            try
            {
                await Task.Delay(Timeout.Infinite, shutdown.Token);
            }
            catch (OperationCanceledException)
            {
            }

            Console.WriteLine("[AO.Server] Shutdown complete.");
        }

        private static ServerDataPaths ResolvePaths(string[] args)
        {
            string? repoRoot = GetArg(args, "--repo-root");
            string? dataRoot = GetArg(args, "--data-root");
            string? professionXml = GetArg(args, "--profession-xml");
            string? serverDataRoot = GetArg(args, "--server-data-root");

            if (repoRoot == null && dataRoot == null && professionXml == null && serverDataRoot == null)
                return ServerDataPaths.DiscoverDefault(AppContext.BaseDirectory);

            var defaults = ServerDataPaths.DiscoverDefault(AppContext.BaseDirectory);
            string discoveredRepoRoot = repoRoot ?? defaults.RepositoryRoot;
            string resolvedDataRoot = dataRoot
                ?? System.IO.Path.Combine(discoveredRepoRoot, "AO.Unity", "Assets", "StreamingAssets", "AOData");
            string resolvedServerDataRoot = serverDataRoot
                ?? System.IO.Path.Combine(discoveredRepoRoot, "AO.Server", "Data");
            string serverProfessionXml = System.IO.Path.Combine(resolvedServerDataRoot, "ipdist.xml");
            string resolvedProfessionXml = professionXml
                ?? (System.IO.File.Exists(serverProfessionXml)
                    ? serverProfessionXml
                    : System.IO.Path.Combine(discoveredRepoRoot, "AO.Core", "Data", "ipdist.xml"));

            return new ServerDataPaths(discoveredRepoRoot, resolvedDataRoot, resolvedProfessionXml, resolvedServerDataRoot);
        }

        private static string? GetArg(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length)
                return null;

            return args[index + 1];
        }

        private static int ResolvePort(string[] args)
        {
            string? raw = GetArg(args, "--port");
            return int.TryParse(raw, out int parsed) && parsed > 0 ? parsed : 4000;
        }
    }
}
