using System;
using System.Collections.Generic;

namespace AO.Client.Discovery
{
    public sealed class DimensionDefinition
    {
        public DimensionDefinition(
            string description,
            string displayName,
            string host,
            IReadOnlyList<int> ports,
            string url,
            string version)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("A dimension host is required.", nameof(host));
            if (ports == null || ports.Count == 0)
                throw new ArgumentException("At least one dimension port is required.", nameof(ports));

            Description = description ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Description : displayName;
            Host = host;
            Ports = ports;
            Url = url ?? string.Empty;
            Version = version ?? string.Empty;
        }

        public string Description { get; }

        public string DisplayName { get; }

        public string Host { get; }

        public IReadOnlyList<int> Ports { get; }

        public string Url { get; }

        public string Version { get; }

        public ServerEndpoint ToServerEndpoint(int portIndex = 0)
        {
            if (portIndex < 0 || portIndex >= Ports.Count)
                throw new ArgumentOutOfRangeException(nameof(portIndex));

            var builder = new UriBuilder("tcp", Host, Ports[portIndex]);
            return new ServerEndpoint(DisplayName, builder.Uri);
        }
    }
}
