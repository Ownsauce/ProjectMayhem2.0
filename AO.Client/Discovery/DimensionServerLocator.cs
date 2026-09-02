using System;
using System.IO;

namespace AO.Client.Discovery
{
    public static class DimensionServerLocator
    {
        public static string FindConfiguration(string aoInstallationRoot)
        {
            if (string.IsNullOrWhiteSpace(aoInstallationRoot))
                throw new ArgumentException("An AO installation path is required.", nameof(aoInstallationRoot));

            // The launch-data copy is the Project Mayhem-preferred source. It can
            // point to a combined user-managed catalog containing multiple private
            // servers, while the root copy may be replaced by a server-specific patch.
            string launchDataPath = Path.Combine(
                aoInstallationRoot,
                "cd_image",
                "data",
                "launcher",
                "DimensionServer.url");
            if (File.Exists(launchDataPath))
                return launchDataPath;

            string rootPath = Path.Combine(aoInstallationRoot, "DimensionServer.url");
            if (File.Exists(rootPath))
                return rootPath;

            throw new FileNotFoundException(
                "No DimensionServer.url was found in the AO installation.",
                launchDataPath);
        }

        public static Uri ReadPreferredEndpoint(string aoInstallationRoot)
        {
            return DimensionServerUrl.Read(FindConfiguration(aoInstallationRoot));
        }
    }
}
