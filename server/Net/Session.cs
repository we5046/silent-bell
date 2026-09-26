using System.Net.Sockets;
using System.Threading.Channels;
using Google.Protobuf;
using SilentBell.Shared.Net;

namespace SilentBell.Server.Net;

// 연결 하나. 수신 루프는 패킷을 조립해 onPacket으로 넘기고,
// 송신은 큐를 거쳐 한 번에 하나씩만 진행한다 (여러 스레드가 동시에 Send해도 바이트가 섞이지 않음).
public sealed class Session
{
    const int ReceiveChunkSize = 8192; // PacketAssembler 한 번 Append 상한과 같음

    readonly Socket _socket;
    readonly Channel<byte[]> _sendQueue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    public int Id { get; }

    public Session(int id, Socket socket)
    {
        Id = id;
        _socket = socket;
    }

    // 호출한 스레드에서 바로 인코딩하므로, 같은 메시지 인스턴스를 여러 세션에 보내도 안전하다.
    public void Send(IMessage message) => _sendQueue.Writer.TryWrite(PacketCodec.Encode(message));

    // 연결이 끊기거나 규약 위반이 생기면 반환한다.
    public async Task RunAsync(Action<Session, IMessage> onPacket, CancellationToken ct)
    {
        var sendTask = SendLoopAsync(ct);
        try
        {
            await ReceiveLoopAsync(onPacket, ct);
        }
        catch (Exception e)
        {
            Log.Warn("session", $"{Id} closed by error: {e.GetType().Name} {e.Message}");
        }
        finally
        {
            _sendQueue.Writer.TryComplete();
            _socket.Close();
            try
            {
                await sendTask;
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or OperationCanceledException)
            {
                // 소켓을 방금 닫았으므로 남아 있던 송신이 이 예외들로 끝나는 것은 정상이다
            }
        }
    }

    async Task ReceiveLoopAsync(Action<Session, IMessage> onPacket, CancellationToken ct)
    {
        var assembler = new PacketAssembler();
        var buffer = new byte[ReceiveChunkSize];
        while (true)
        {
            int n = await _socket.ReceiveAsync(buffer, SocketFlags.None, ct);
            if (n == 0) return; // 상대가 정상 종료
            assembler.Append(buffer.AsSpan(0, n));
            while (assembler.TryRead(out var id, out var body))
            {
                if (!PacketRegistry.TryParse(id, body, out var message))
                    throw new InvalidDataException($"unknown or broken packet: {id}");
                onPacket(this, message);
            }
        }
    }

    async Task SendLoopAsync(CancellationToken ct)
    {
        await foreach (var packet in _sendQueue.Reader.ReadAllAsync(ct))
        {
            // TCP 송신은 일부만 나갈 수 있으므로 다 나갈 때까지 반복한다.
            var remaining = packet.AsMemory();
            while (!remaining.IsEmpty)
            {
                int sent = await _socket.SendAsync(remaining, SocketFlags.None, ct);
                remaining = remaining[sent..];
            }
        }
    }
}
