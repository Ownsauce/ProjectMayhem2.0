using System;
using System.Diagnostics;
using System.IO;
using AO.Assets.ResourceDatabase;

namespace AO.Assets.Conversion
{
    public static class AOIndoorSurfaceExtractor
    {
        public static bool Extract(
            AOInstallValidation install,
            int playfieldId,
            int tilemapId,
            int roomCount,
            string helperExecutable,
            string outputPath,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            if (install == null || !install.IsValid)
            { diagnostic = "The AO installation is invalid."; return false; }
            if (playfieldId <= 0 || tilemapId <= 0 || roomCount <= 0)
            { diagnostic = "The indoor playfield identity is invalid."; return false; }
            if (string.IsNullOrWhiteSpace(helperExecutable) || !File.Exists(helperExecutable))
            { diagnostic = "The AO indoor extractor executable was not found."; return false; }

            string fullOutput = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(fullOutput);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = fullOutput + ".tmp";
            if (File.Exists(temporary)) File.Delete(temporary);

            bool useWine = Path.DirectorySeparatorChar == '/';
            string executable = useWine ? "wine" : helperExecutable;
            string arguments = useWine
                ? Quote(ToWinePath(helperExecutable)) + " "
                    + Quote(ToWinePath(install.RootPath)) + " "
                    + playfieldId + " " + tilemapId + " " + roomCount + " " + Quote(ToWinePath(temporary))
                : Quote(install.RootPath) + " " + playfieldId + " " + tilemapId + " " + roomCount
                    + " " + Quote(temporary);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                if (useWine) startInfo.EnvironmentVariables["WINEDEBUG"] = "-all";
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    { diagnostic = "The AO indoor extractor did not start."; return false; }
                    string standardOutput = process.StandardOutput.ReadToEnd();
                    string standardError = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(120000) || process.ExitCode != 0)
                    {
                        diagnostic = string.IsNullOrWhiteSpace(standardError)
                            ? standardOutput.Trim() : standardError.Trim();
                        return false;
                    }
                    diagnostic = standardOutput.Trim();
                }

                if (!File.Exists(temporary) || new FileInfo(temporary).Length < 16)
                { diagnostic = "The AO indoor extractor produced no geometry."; return false; }
                if (File.Exists(fullOutput)) File.Delete(fullOutput);
                File.Move(temporary, fullOutput);
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = exception.Message;
                return false;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static string ToWinePath(string path)
        {
            string full = Path.GetFullPath(path).Replace('/', '\\');
            return "Z:" + full;
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
