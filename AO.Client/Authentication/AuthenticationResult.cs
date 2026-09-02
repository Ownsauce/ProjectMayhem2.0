namespace AO.Client.Authentication
{
    public sealed class AuthenticationResult
    {
        private AuthenticationResult(bool succeeded, string accountId, string message)
        {
            Succeeded = succeeded;
            AccountId = accountId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool Succeeded { get; }

        public string AccountId { get; }

        public string Message { get; }

        public static AuthenticationResult Success(string accountId = "", string message = "") =>
            new AuthenticationResult(true, accountId, message);

        public static AuthenticationResult Failure(string message) =>
            new AuthenticationResult(false, string.Empty, message);
    }
}
