using System;

namespace AO.Client
{
    public sealed class ServerEndpoint
    {
        public ServerEndpoint(string name, Uri address)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A server name is required.", nameof(name));

            Name = name;
            Address = address ?? throw new ArgumentNullException(nameof(address));
        }

        public string Name { get; }

        public Uri Address { get; }
    }
}
