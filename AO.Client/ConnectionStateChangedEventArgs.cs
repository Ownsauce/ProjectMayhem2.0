using System;

namespace AO.Client
{
    public sealed class ConnectionStateChangedEventArgs : EventArgs
    {
        public ConnectionStateChangedEventArgs(
            ClientConnectionState previousState,
            ClientConnectionState currentState,
            string message = "")
        {
            PreviousState = previousState;
            CurrentState = currentState;
            Message = message ?? string.Empty;
        }

        public ClientConnectionState PreviousState { get; }

        public ClientConnectionState CurrentState { get; }

        public string Message { get; }
    }
}
