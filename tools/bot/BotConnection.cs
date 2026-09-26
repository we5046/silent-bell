using System.Net.Sockets;
using Google.Protobuf;
using SilentBell.Shared.Net;

namespace SilentBell.Bot;

// 테스트와 봇이 쓰는 최소 클라이언트. 원하는 타입의 패킷이 올 때까지 읽고, 그 사이의 다른 패킷은 버린다.
public sealed class BotConnection : IDisposable
{
    readonly TcpClient _client;
    readonly NetworkStream _stream;
    readonly PacketAssembler _assembler = new();
    readonly byte[] _buffer = new byte[8192];

    BotConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public static async Task<BotConnection> ConnectAsync(string host, int port)
    {
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(host, port);
        return new BotConnection(client);
    }

    public Task SendAsync(IMessage message) => SendRawAsync(PacketCodec.Encode(message));

    public Task SendRawAsync(byte[] bytes) => _stream.WriteAsync(bytes).AsTask();

    public async Task<T> ReceiveAsync<T>(Func<T, bool>? match = null, int timeoutMs = 3000) where T : class, IMessage
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (true)
        {
            while (_assembler.TryRead(out var id, out var body))
            {
                if (PacketRegistry.TryParse(id, body, out var message) && message is T typed && (match == null || match(typed)))
                    return typed;
            }
            int n = await _stream.ReadAsync(_buffer, cts.Token);
            if (n == 0) throw new IOException("server closed the connection");
            _assembler.Append(_buffer.AsSpan(0, n));
        }
    }

    public void Dispose() => _client.Dispose();
}
