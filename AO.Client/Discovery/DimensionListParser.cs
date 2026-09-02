using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AO.Client.Discovery
{
    public static class DimensionListParser
    {
        private static readonly Regex BlockPattern = new Regex(
            @"\bSTARTINFO\b(?<body>.*?)\bENDINFO\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        private static readonly Regex FieldPattern = new Regex(
            @"\b(?<key>description|displayname|connect|ports|url|version)\s*=\s*(?<value>.*?)(?=\b(?:description|displayname|connect|ports|url|version)\s*=|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        public static IReadOnlyList<DimensionDefinition> Parse(string content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            var dimensions = new List<DimensionDefinition>();
            foreach (Match block in BlockPattern.Matches(content))
            {
                var fields = ParseFields(block.Groups["body"].Value);
                string host = Get(fields, "connect");
                IReadOnlyList<int> ports = ParsePorts(Get(fields, "ports"));
                if (string.IsNullOrWhiteSpace(host) || ports.Count == 0)
                    continue;

                dimensions.Add(new DimensionDefinition(
                    Get(fields, "description"),
                    Get(fields, "displayname"),
                    host,
                    ports,
                    Get(fields, "url"),
                    Get(fields, "version")));
            }

            return dimensions;
        }

        private static Dictionary<string, string> ParseFields(string body)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match field in FieldPattern.Matches(body))
            {
                fields[field.Groups["key"].Value] = field.Groups["value"].Value.Trim();
            }

            return fields;
        }

        private static IReadOnlyList<int> ParsePorts(string value)
        {
            var ports = new List<int>();
            foreach (string token in Regex.Split(value ?? string.Empty, @"[\s,;]+"))
            {
                if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int port)
                    && port > 0
                    && port <= 65535)
                {
                    ports.Add(port);
                }
            }

            return ports;
        }

        private static string Get(IReadOnlyDictionary<string, string> fields, string key)
        {
            return fields.TryGetValue(key, out string value) ? value : string.Empty;
        }
    }
}
