using System;
using System.IO;

namespace AO.Server
{
    public sealed class ServerDataPaths
    {
        public string RepositoryRoot { get; }
        public string AoDataRoot { get; }
        public string ProfessionXmlPath { get; }
        public string PlayfieldsRoot { get; }
        public string ServerDataRoot { get; }
        public string ServerPlayfieldsRoot { get; }
        public string ServerAoDataRoot { get; }
        public string ServerQuestsRoot { get; }

        public ServerDataPaths(string repositoryRoot, string aoDataRoot, string professionXmlPath, string serverDataRoot)
        {
            RepositoryRoot = repositoryRoot ?? throw new ArgumentNullException(nameof(repositoryRoot));
            AoDataRoot = aoDataRoot ?? throw new ArgumentNullException(nameof(aoDataRoot));
            ProfessionXmlPath = professionXmlPath ?? throw new ArgumentNullException(nameof(professionXmlPath));
            ServerDataRoot = serverDataRoot ?? throw new ArgumentNullException(nameof(serverDataRoot));
            PlayfieldsRoot = Path.Combine(AoDataRoot, "Playfields");
            ServerPlayfieldsRoot = Path.Combine(ServerDataRoot, "Playfields");
            ServerAoDataRoot = Path.Combine(ServerDataRoot, "AOData");
            ServerQuestsRoot = Path.Combine(ServerAoDataRoot, "Quests");
        }

        public static ServerDataPaths DiscoverDefault(string startPath)
        {
            string repoRoot = FindRepositoryRoot(startPath);
            string aoDataRoot = Path.Combine(repoRoot, "AO.Unity", "Assets", "StreamingAssets", "AOData");
            string serverDataRoot = Path.Combine(repoRoot, "AO.Server", "Data");
            string serverProfessionXmlPath = Path.Combine(serverDataRoot, "ipdist.xml");
            string professionXmlPath = File.Exists(serverProfessionXmlPath)
                ? serverProfessionXmlPath
                : Path.Combine(repoRoot, "AO.Core", "Data", "ipdist.xml");
            return new ServerDataPaths(repoRoot, aoDataRoot, professionXmlPath, serverDataRoot);
        }

        public string ResolveAoDataFile(string fileName)
        {
            return Path.Combine(AoDataRoot, fileName);
        }

        public string ResolveServerAoDataFile(string fileName)
        {
            return Path.Combine(ServerAoDataRoot, fileName);
        }

        public string ResolveAuthoritativeAoDataFile(string fileName)
        {
            string serverPath = ResolveServerAoDataFile(fileName);
            if (File.Exists(serverPath))
                return serverPath;
            return ResolveAoDataFile(fileName);
        }

        private static string FindRepositoryRoot(string startPath)
        {
            var current = new DirectoryInfo(Path.GetFullPath(startPath));
            while (current != null)
            {
                bool hasCore = Directory.Exists(Path.Combine(current.FullName, "AO.Core"));
                bool hasUnity = Directory.Exists(Path.Combine(current.FullName, "AO.Unity"));
                if (hasCore && hasUnity)
                    return current.FullName;

                current = current.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Unable to locate the ProjectMayhem repository root from '{startPath}'.");
        }
    }
}
