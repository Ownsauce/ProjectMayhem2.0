namespace AO.Client
{
    public enum ClientConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Authenticating,
        Authenticated,
        EnteringWorld,
        InWorld,
        Disconnecting,
        Faulted
    }
}
