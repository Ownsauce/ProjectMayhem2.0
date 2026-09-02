using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AO.Assets.ResourceDatabase
{
    public sealed class AOInstallValidation
    {
        internal AOInstallValidation(string rootPath, bool isValid, string clientVersion,
            string resourceDatabasePath, string databaseFingerprint,
            IReadOnlyList<string> errors)
        {
            RootPath = rootPath;
            IsValid = isValid;
            ClientVersion = clientVersion;
            ResourceDatabasePath = resourceDatabasePath;
            DatabaseFingerprint = databaseFingerprint;
            Errors = errors;
        }

        public string RootPath { get; }
        public bool IsValid { get; }
        public string ClientVersion { get; }
        public string ResourceDatabasePath { get; }
        public string DatabaseFingerprint { get; }
        public string Fingerprint => DatabaseFingerprint;
        public IReadOnlyList<string> Errors { get; }
    }

    public static class AOInstallLocator
    {
        public static AOInstallValidation Validate(string candidatePath)
        {
            var errors = new List<string>();
            string root = Normalize(candidatePath);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                errors.Add("The selected folder does not exist.");
                return new AOInstallValidation(root, false, string.Empty, string.Empty,
                    string.Empty, errors);
            }

            if (!File.Exists(Path.Combine(root, "Anarchy.exe"))
                && !File.Exists(Path.Combine(root, "AnarchyOnline.exe")))
                errors.Add("Anarchy.exe or AnarchyOnline.exe was not found.");

            string database = Path.Combine(root, "cd_image", "data", "db",
                "ResourceDatabase.dat");
            string index = Path.Combine(root, "cd_image", "data", "db",
                "ResourceDatabase.idx");
            string fingerprint = string.Empty;
            if (!File.Exists(database) || new FileInfo(database).Length == 0)
                errors.Add("cd_image/data/db/ResourceDatabase.dat was not found or was empty.");
            else
            {
                var info = new FileInfo(database);
                fingerprint = info.Length.ToString("X") + "-"
                    + info.LastWriteTimeUtc.Ticks.ToString("X");
            }
            if (!File.Exists(index) || new FileInfo(index).Length == 0)
                errors.Add("cd_image/data/db/ResourceDatabase.idx was not found or was empty.");
            else
            {
                var info = new FileInfo(index);
                fingerprint += "-" + info.Length.ToString("X") + "-"
                    + info.LastWriteTimeUtc.Ticks.ToString("X");
            }

            string version = ReadVersion(Path.Combine(root, "version.id"));
            if (string.IsNullOrEmpty(version))
                errors.Add("version.id was not found or was empty.");

            return new AOInstallValidation(root, errors.Count == 0, version, database,
                fingerprint, errors);
        }

        public static AOInstallValidation FindFirstValid(IEnumerable<string> additionalCandidates = null)
        {
            IEnumerable<string> candidates = (additionalCandidates ?? Array.Empty<string>())
                .Concat(GetCommonCandidates())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in candidates)
            {
                AOInstallValidation result = Validate(candidate);
                if (result.IsValid)
                    return result;
            }
            return new AOInstallValidation(string.Empty, false, string.Empty, string.Empty,
                string.Empty, new[] { "No valid Anarchy Online installation was discovered." });
        }

        public static IEnumerable<string> GetCommonCandidates()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(programFiles, "Funcom", "Anarchy Online");
            yield return Path.Combine(programFilesX86, "Funcom", "Anarchy Online");
            yield return Path.Combine(home, "Anarchy Online");
            yield return Path.Combine(home, ".wine", "drive_c", "Funcom", "Anarchy Online");
            yield return Path.Combine(home, ".wine", "drive_c", "Program Files (x86)",
                "Funcom", "Anarchy Online");
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try { return Path.GetFullPath(path.Trim().Trim('"')); }
            catch (Exception) { return path.Trim().Trim('"'); }
        }

        private static string ReadVersion(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty; }
            catch (IOException) { return string.Empty; }
            catch (UnauthorizedAccessException) { return string.Empty; }
        }
    }
}
