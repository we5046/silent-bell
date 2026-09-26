using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Google.Protobuf;
using SilentBell.Server.Lobby;

namespace SilentBell.Server.Net;

// Accept 루프와 세션 목록. 받은 패킷은 전부 로비 잡 큐로 보낸다.
public sealed class GameServer
{
    readonly TcpListener _listener;
    readonly ConcurrentDictionary<int, Session> _sessions = new();
    readonly JobQueue _lobbyQueue = new();
    readonly LobbyService _lobby;
    int _nextSessionId;

    public GameServer(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _lobby = new LobbyService(SendTo, new Random());
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start() => _listener.Start();

    public async Task RunAsync(CancellationToken ct)
    {
        var lobbyTask = _lobbyQueue.RunAsync(ct);
        try
        {
            while (true)
            {
                var socket = await _listener.AcceptSocketAsync(ct);
                socket.NoDelay = true; // 작은 패킷을 모아 늦게 보내는 Nagle 알고리즘을 끈다
                var session = new Session(Interlocked.Increment(ref _nextSessionId), socket);
                _sessions[session.Id] = session;
                Console.WriteLine($"[session {session.Id}] connected {socket.RemoteEndPoint}");
                _ = RunSessionAsync(session, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _listener.Stop();
            await lobbyTask;
        }
    }

    async Task RunSessionAsync(Session session, CancellationToken ct)
    {
        await session.RunAsync((s, message) => _lobbyQueue.Push(() => _lobby.Handle(s.Id, message)), ct);
        _sessions.TryRemove(session.Id, out _);
        _lobbyQueue.Push(() => _lobby.OnDisconnect(session.Id));
    }

    void SendTo(int sessionId, IMessage message)
    {
        if (_sessions.TryGetValue(sessionId, out var session)) session.Send(message);
    }
}
