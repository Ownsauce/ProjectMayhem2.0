using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AO.Client;
using AO.Client.Authentication;
using AO.Client.Backends.AORebirth;
using AO.Client.Characters;

static class CharacterManagementTests
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static async Task ExpectFailure<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    static void Int(Stream stream, int value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    static int ReadInt(byte[] body, ref int offset)
    {
        int value = BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(offset, 4));
        offset += 4;
        return value;
    }

    static async Task<byte[]> ReadRequest(NetworkStream stream, CancellationToken token)
    {
        byte[] header = new byte[16];
        await stream.ReadExactlyAsync(header, token);
        Require(BinaryPrimitives.ReadInt16BigEndian(header.AsSpan(2)) == 1, "System packet type");
        int size = BinaryPrimitives.ReadInt16BigEndian(header.AsSpan(6));
        byte[] remainder = new byte[((size + 3) & ~3) - 16];
        await stream.ReadExactlyAsync(remainder, token);
        return remainder.Take(size - 16).ToArray();
    }

    static async Task Reply(NetworkStream stream, int opcode, Action<Stream> fields,
        CancellationToken token)
    {
        using var body = new MemoryStream();
        Int(body, opcode);
        fields?.Invoke(body);
        int size = 16 + (int)body.Length;
        byte[] packet = new byte[(size + 3) & ~3];
        BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(2), 1);
        BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(4), 1);
        BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(6), (short)size);
        body.ToArray().CopyTo(packet, 16);
        await stream.WriteAsync(packet, token);
    }

    static void VerifyCreate(byte[] body)
    {
        int offset = 0;
        Require(ReadInt(body, ref offset) == 0x0F, "Create opcode");
        Require(body.AsSpan(offset, 49).ToArray().All(value => value == 0), "49-byte creation prefix");
        offset += 49;
        int length = ReadInt(body, ref offset);
        Require(Encoding.ASCII.GetString(body, offset, length) == "Testhero", "Creation name");
        offset += length;
        int[] expected = { 2, 3, 4, 1, 0, 0, 0, 12345, 108, 2, 0 };
        foreach (int value in expected)
            Require(ReadInt(body, ref offset) == value, "Creation field ordering/value");
        Require(offset == body.Length, "Creation payload length");
    }

    static async Task AuthenticatePeer(NetworkStream stream, CancellationToken token, int capacity = 1)
    {
        byte[] login = await ReadRequest(stream, token);
        Require(BinaryPrimitives.ReadInt32BigEndian(login) == 0x22, "User login");
        await Reply(stream, 0x24, body => body.Write(new byte[32]), token);
        byte[] credentials = await ReadRequest(stream, token);
        Require(BinaryPrimitives.ReadInt32BigEndian(credentials) == 0x25, "Credentials");
        await Reply(stream, 0x0E, body => { Int(body, 0); Int(body, capacity); Int(body, 0); }, token);
    }

    static async Task VerifyCancellation(CharacterCreationRequest request, CancellationToken token)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task peer = Task.Run(async () =>
        {
            using TcpClient socket = await listener.AcceptTcpClientAsync(token);
            NetworkStream stream = socket.GetStream();
            await AuthenticatePeer(stream, token);
            VerifyCreate(await ReadRequest(stream, token));
            received.SetResult(true);
            await finish.Task.WaitAsync(token); // Deliberately never acknowledge creation.
        }, token);
        using var backend = new AORebirthBackend();
        await backend.ConnectAsync(new ServerEndpoint("Test", new Uri($"tcp://127.0.0.1:{port}")), token);
        await backend.AuthenticateAsync(new AuthenticationRequest("test", "secret"), token);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task pending = backend.CreateCharacterAsync(request, operation.Token);
        await received.Task.WaitAsync(token);
        operation.Cancel();
        await ExpectFailure<OperationCanceledException>(() => pending);
        Require(backend.State == ClientConnectionState.Faulted, "Canceled request requires a fresh login");
        await ExpectFailure<InvalidOperationException>(() => backend.CreateCharacterAsync(request, token));
        finish.SetResult(true);
        await peer;
    }

    static async Task VerifyCapacity(int capacity, CancellationToken token)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task peer = Task.Run(async () =>
        {
            using TcpClient socket = await listener.AcceptTcpClientAsync(token);
            await AuthenticatePeer(socket.GetStream(), token, capacity);
            await finish.Task.WaitAsync(token);
        }, token);
        using var backend = new AORebirthBackend();
        Require(backend.CharacterLimit == null, "Capacity unknown before authentication");
        await backend.ConnectAsync(new ServerEndpoint("Test", new Uri($"tcp://127.0.0.1:{port}")), token);
        await backend.AuthenticateAsync(new AuthenticationRequest("test", "secret"), token);
        Require(backend.CharacterLimit == capacity, "Server-specific capacity retained for empty roster");
        Require(backend.ConnectedEndpoint == $"127.0.0.1:{port}", "Diagnostics use the connected endpoint");
        Require(backend.PacketsSent == 2 && backend.PacketsReceived == 2, "Login diagnostics count actual packets");
        Require(backend.LastPacketReceivedUtc.HasValue, "Diagnostics record last received packet time");
        if (capacity == 0)
            await ExpectFailure<InvalidOperationException>(() => backend.CreateCharacterAsync(
                new CharacterCreationRequest("Testhero", 2, 3, 4, 12345), token));
        await backend.DisconnectAsync(token);
        Require(backend.CharacterLimit == null, "Disconnect clears account capacity");
        finish.SetResult(true);
        await peer;
    }

    static async Task Main()
    {
        var request = new CharacterCreationRequest("Testhero", 2, 3, 4, 12345, 108, 2);
        VerifyCreate(AORebirthProtocol.CreateCharacterRequest(request).Skip(16)
            .Take(4 + 49 + 4 + 8 + 11 * 4).ToArray());
        await ExpectFailure<ArgumentException>(() => Task.FromResult(new CharacterCreationRequest("Bad123", 1, 2, 1, 12345)));
        await ExpectFailure<ArgumentException>(() => Task.FromResult(new CharacterCreationRequest("Testhero", 4, 2, 1, 12345)));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken token = timeout.Token;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var createReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleteReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDelete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task peer = Task.Run(async () =>
        {
            using TcpClient socket = await listener.AcceptTcpClientAsync(token);
            NetworkStream stream = socket.GetStream();
            await AuthenticatePeer(stream, token);
            VerifyCreate(await ReadRequest(stream, token));
            await Reply(stream, 0x10, null, token); // Name rejected, no mutation.
            VerifyCreate(await ReadRequest(stream, token));
            createReceived.SetResult(true);
            await allowCreate.Task.WaitAsync(token);
            await Reply(stream, 0x11, body => { Int(body, 1234); Int(body, 0); }, token);
            byte[] deletion = await ReadRequest(stream, token);
            Require(BinaryPrimitives.ReadInt32BigEndian(deletion) == 0x14
                && BinaryPrimitives.ReadInt32BigEndian(deletion.AsSpan(4)) == 1234, "Delete selected ID");
            deleteReceived.SetResult(true);
            await allowDelete.Task.WaitAsync(token);
            await Reply(stream, 0x15, body => Int(body, 1234), token);
            VerifyCreate(await ReadRequest(stream, token));
            await Reply(stream, 0x11, body => { Int(body, 2345); Int(body, 0); }, token);
            await ReadRequest(stream, token);
            await Reply(stream, 0x15, body => Int(body, 2346), token); // Wrong ID must not remove 2345.
        }, token);

        using var backend = new AORebirthBackend();
        await ExpectFailure<InvalidOperationException>(() => backend.CreateCharacterAsync(request, token));
        await backend.ConnectAsync(new ServerEndpoint("Test", new Uri($"tcp://127.0.0.1:{port}")), token);
        Require((await backend.AuthenticateAsync(new AuthenticationRequest("test", "secret"), token)).Succeeded, "Mock authentication");
        await ExpectFailure<InvalidOperationException>(() => backend.CreateCharacterAsync(request, token));
        Require(backend.State == ClientConnectionState.Authenticated && (await backend.GetCharactersAsync(token)).Count == 0,
            "Name rejection preserves session and roster");
        Task<CharacterSummary> creating = backend.CreateCharacterAsync(request, token);
        await createReceived.Task.WaitAsync(token);
        Require((await backend.GetCharactersAsync(token)).Count == 0, "No optimistic creation before acknowledgement");
        allowCreate.SetResult(true);
        CharacterSummary created = await creating;
        Require(created.Id == "1234" && created.Name == "Testhero" && created.GenderId == 3, "Server ID and creation details");
        Require(backend.CharacterLimit == 1, "Creation preserves capacity");
        await ExpectFailure<InvalidOperationException>(() => backend.CreateCharacterAsync(request, token));
        await ExpectFailure<InvalidOperationException>(() => backend.DeleteCharacterAsync("9999", token));
        Task deleting = backend.DeleteCharacterAsync("1234", token);
        await deleteReceived.Task.WaitAsync(token);
        Require((await backend.GetCharactersAsync(token)).Count == 1, "No optimistic removal before acknowledgement");
        allowDelete.SetResult(true);
        await deleting;
        Require((await backend.GetCharactersAsync(token)).Count == 0, "Acknowledged deletion removes character");
        await backend.CreateCharacterAsync(request, token);
        await ExpectFailure<InvalidDataException>(() => backend.DeleteCharacterAsync("2345", token));
        Require(backend.State == ClientConnectionState.Faulted, "Mismatched acknowledgement prevents subsequent operations");
        var roster = (System.Collections.Generic.IReadOnlyList<CharacterSummary>)typeof(AORebirthBackend)
            .GetField("_characters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(backend);
        Require(roster.Count == 1 && roster[0].Id == "2345", "Wrong acknowledgement retains selected character");
        await peer;
        await VerifyCancellation(request, token);
        await VerifyCapacity(0, token);
        await VerifyCapacity(20, token);
        Console.WriteLine("PASS character management: packet fields, validation, server acknowledgements, name rejection, ownership, delayed replies, mismatched IDs, cancellation/recovery, server capacities, full slots, connection telemetry");
    }
}
