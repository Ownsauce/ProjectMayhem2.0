using System;
using System.IO;

namespace AO.Client.Discovery
{
    public static class DimensionServerUrl
    {
        public static Uri Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A DimensionServer.url path is required.", nameof(path));

            return Parse(File.ReadAllText(path));
        }

        public static Uri Parse(string content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            using (var reader = new StringReader(content))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string candidate = line.Trim();
                    if (candidate.Length == 0 || candidate.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    if (!candidate.Contains("://"))
                        candidate = "http://" + candidate;

                    if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri endpoint))
                        return endpoint;

                    throw new FormatException("DimensionServer.url contains an invalid endpoint.");
                }
            }

            throw new FormatException("DimensionServer.url does not contain an endpoint.");
        }
    }
}
