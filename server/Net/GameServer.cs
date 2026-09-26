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
        // 로비는 ct 외에도 서버가 직접 멈출 수 있어야 한다. 그래야 accept 루프가 어떤 이유로 끝나도 finally에서 기다리다 멈추지 않는다.
        using var lobbyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var lobbyTask = _lobbyQueue.RunAsync(lobbyCts.Token);
        try
        {
            while (true)
            {
                Socket socket;
                try
                {
                    socket = await _listener.AcceptSocketAsync(ct);
                }
                catch (SocketException e)
                {
                    // 접속 하나의 실패(연결 도중 끊김, 핸들 고갈 등). 기록하고 다음 접속을 계속 받는다.
                    Log.Warn("accept", $"{e.SocketErrorCode} {e.Message}");
                    await Task.Delay(100, ct); // 계속 실패하는 경우 로그 폭주를 막는다
                    continue;
                }
                try
                {
                    socket.NoDelay = true; // 작은 패킷을 모아 늦게 보내는 Nagle 알고리즘을 끈다
                }
                catch (SocketException e)
                {
                    // 접속 하나의 설정 실패(연결 직후 끊김 등). 이 소켓만 버리고 다음 접속을 받는다.
                    Log.Warn("accept", $"socket setup failed, dropping connection: {e.SocketErrorCode} {e.Message}");
                    socket.Dispose();
                    continue;
                }
                var session = new Session(Interlocked.Increment(ref _nextSessionId), socket);
                _sessions[session.Id] = session;
                Log.Info("session", $"{session.Id} connected {socket.RemoteEndPoint}");
                _ = RunSessionAsync(session, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 정상 종료
        }
        finally
        {
            _listener.Stop();
            lobbyCts.Cancel();
            await lobbyTask;
        }
    }

    async Task RunSessionAsync(Session session, CancellationToken ct)
    {
        try
        {
            await session.RunAsync((s, message) => _lobbyQueue.Push(() => _lobby.Handle(s.Id, message)), ct);
        }
        catch (Exception e)
        {
            // Session.RunAsync는 예상한 예외를 스스로 처리한다. 여기까지 오면 버그이므로 기록만 하고 정리는 반드시 한다.
            Log.Error("session", $"{session.Id} unexpected failure: {e}");
        }
        finally
        {
            _sessions.TryRemove(session.Id, out _);
            _lobbyQueue.Push(() => _lobby.OnDisconnect(session.Id));
            Log.Info("session", $"{session.Id} disconnected");
        }
    }

    void SendTo(int sessionId, IMessage message)
    {
        if (_sessions.TryGetValue(sessionId, out var session)) session.Send(message);
    }
}
