using System;
using System.Collections.Generic;

namespace AO.Client.Authentication
{
    public sealed class AuthenticationRequest
    {
        public AuthenticationRequest(
            string username,
            string secret,
            IReadOnlyDictionary<string, string> parameters = null)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("A username is required.", nameof(username));

            Username = username;
            Secret = secret ?? throw new ArgumentNullException(nameof(secret));
            Parameters = parameters ?? EmptyParameters;
        }

        private static readonly IReadOnlyDictionary<string, string> EmptyParameters =
            new Dictionary<string, string>();

        public string Username { get; }

        // Adapters must not log or persist this value.
        public string Secret { get; }

        // Supports backend-specific challenges without leaking them into Unity UI contracts.
        public IReadOnlyDictionary<string, string> Parameters { get; }
    }
}
