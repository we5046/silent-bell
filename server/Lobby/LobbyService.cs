using Google.Protobuf;
using SilentBell.Protocol;

namespace SilentBell.Server.Lobby;

// 로비와 대기실 규칙. 로비 잡 큐 한 흐름에서만 호출되므로 락이 없다.
public sealed class LobbyService
{
    public const int MaxRoomSize = 4;
    public const int MinStartPlayers = 2;
    const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 0/O/1/I 제외

    readonly Action<int, IMessage> _send;
    readonly Random _rng;
    readonly Dictionary<int, Player> _players = new();
    readonly Dictionary<string, Room> _rooms = new();

    public LobbyService(Action<int, IMessage> send, Random rng)
    {
        _send = send;
        _rng = rng;
    }

    public void Handle(int sessionId, IMessage message)
    {
        if (message is C_Login login)
        {
            Login(sessionId, login.Nickname);
            return;
        }
        if (!_players.TryGetValue(sessionId, out var player))
        {
            Error(sessionId, ErrorCode.NotLoggedIn);
            return;
        }
        switch (message)
        {
            case C_CreateRoom: CreateRoom(player); break;
            case C_JoinRoom join: JoinRoom(player, join.Code); break;
            case C_LeaveRoom: LeaveRoom(player); break;
        }
    }

    public void OnDisconnect(int sessionId)
    {
        if (!_players.Remove(sessionId, out var player)) return;
        if (player.Room != null) RemoveFromRoom(player);
    }

    void Login(int sessionId, string rawNickname)
    {
        if (_players.ContainsKey(sessionId))
        {
            Error(sessionId, ErrorCode.AlreadyLoggedIn);
            return;
        }
        var nickname = rawNickname.Trim();
        if (nickname.Length < 2 || nickname.Length > 12)
        {
            _send(sessionId, new S_LoginResult { Ok = false, Error = ErrorCode.NicknameInvalid });
            return;
        }
        // ponytail: 접속자 전체 선형 탐색. 동시 접속이 수천 명이 되면 HashSet으로 바꾼다.
        if (_players.Values.Any(p => p.Nickname == nickname))
        {
            _send(sessionId, new S_LoginResult { Ok = false, Error = ErrorCode.NicknameTaken });
            return;
        }
        _players[sessionId] = new Player { Id = sessionId, Nickname = nickname };
        _send(sessionId, new S_LoginResult { Ok = true, PlayerId = sessionId });
    }

    void CreateRoom(Player player)
    {
        if (player.Room != null)
        {
            Error(player.Id, ErrorCode.AlreadyInRoom);
            return;
        }
        var room = new Room { Code = NewCode(), HostId = player.Id };
        _rooms[room.Code] = room;
        AddMember(room, player);
    }

    void JoinRoom(Player player, string rawCode)
    {
        if (player.Room != null)
        {
            Error(player.Id, ErrorCode.AlreadyInRoom);
            return;
        }
        if (!_rooms.TryGetValue(rawCode.Trim().ToUpperInvariant(), out var room))
        {
            Error(player.Id, ErrorCode.RoomNotFound);
            return;
        }
        if (room.InGame)
        {
            Error(player.Id, ErrorCode.RoomInGame);
            return;
        }
        if (room.Members.Count >= MaxRoomSize)
        {
            Error(player.Id, ErrorCode.RoomFull);
            return;
        }
        AddMember(room, player);
    }

    void LeaveRoom(Player player)
    {
        if (player.Room == null)
        {
            Error(player.Id, ErrorCode.NotInRoom);
            return;
        }
        RemoveFromRoom(player);
        _send(player.Id, new S_RoomState()); // 빈 코드 = 방에 없음
    }

    void AddMember(Room room, Player player)
    {
        room.Members.Add(new Member { PlayerId = player.Id, Nickname = player.Nickname });
        player.Room = room;
        Broadcast(room);
    }

    void RemoveFromRoom(Player player)
    {
        var room = player.Room!;
        room.Members.RemoveAll(m => m.PlayerId == player.Id);
        player.Room = null;
        if (room.Members.Count == 0)
        {
            _rooms.Remove(room.Code);
            return;
        }
        if (room.HostId == player.Id) room.HostId = room.Members[0].PlayerId;
        Broadcast(room);
    }

    string NewCode()
    {
        while (true)
        {
            var chars = new char[4];
            for (int i = 0; i < chars.Length; i++) chars[i] = CodeChars[_rng.Next(CodeChars.Length)];
            var code = new string(chars);
            if (!_rooms.ContainsKey(code)) return code;
        }
    }

    void Broadcast(Room room)
    {
        var state = new S_RoomState { Code = room.Code, HostId = room.HostId };
        foreach (var m in room.Members)
        {
            state.Slots.Add(new Slot
            {
                PlayerId = m.PlayerId,
                Nickname = m.Nickname,
                ClassType = m.ClassType,
                Gender = m.Gender,
                Ready = m.Ready,
            });
        }
        // 같은 인스턴스를 여러 번 보내도 된다. Session.Send가 즉시 바이트로 인코딩한다.
        foreach (var m in room.Members) _send(m.PlayerId, state);
    }

    void Error(int to, ErrorCode code) => _send(to, new S_Error { Code = code });
}
