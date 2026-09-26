using Google.Protobuf;
using SilentBell.Protocol;
using SilentBell.Server.Lobby;

namespace SilentBell.Server.Tests;

public class LobbyServiceTests
{
    readonly List<(int To, IMessage Message)> _sent = new();
    readonly LobbyService _lobby;

    public LobbyServiceTests()
    {
        _lobby = new LobbyService((to, message) => _sent.Add((to, message)), new Random(1));
    }

    T Last<T>(int to) where T : IMessage =>
        _sent.Where(s => s.To == to).Select(s => s.Message).OfType<T>().Last();

    void Login(int id, string? nickname = null) =>
        _lobby.Handle(id, new C_Login { Nickname = nickname ?? $"player{id}" });

    string CreateRoom(int hostId)
    {
        Login(hostId);
        _lobby.Handle(hostId, new C_CreateRoom());
        return Last<S_RoomState>(hostId).Code;
    }

    void Join(int id, string code)
    {
        Login(id);
        _lobby.Handle(id, new C_JoinRoom { Code = code });
    }

    [Fact]
    public void Login_succeeds_with_trimmed_nickname()
    {
        Login(1, "  로엔  ");
        var result = Last<S_LoginResult>(1);
        Assert.True(result.Ok);
        Assert.Equal(1, result.PlayerId);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("   ")]
    [InlineData("1234567890123")]
    public void Login_rejects_invalid_nickname_length(string nickname)
    {
        Login(1, nickname);
        var result = Last<S_LoginResult>(1);
        Assert.False(result.Ok);
        Assert.Equal(ErrorCode.NicknameInvalid, result.Error);
    }

    [Fact]
    public void Login_rejects_nickname_in_use()
    {
        Login(1, "로엔");
        Login(2, "로엔");
        Assert.Equal(ErrorCode.NicknameTaken, Last<S_LoginResult>(2).Error);
    }

    [Fact]
    public void Login_twice_returns_error()
    {
        Login(1);
        Login(1);
        Assert.Equal(ErrorCode.AlreadyLoggedIn, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Request_before_login_returns_error()
    {
        _lobby.Handle(1, new C_CreateRoom());
        Assert.Equal(ErrorCode.NotLoggedIn, Last<S_Error>(1).Code);
    }

    [Fact]
    public void CreateRoom_makes_creator_host_with_readable_code()
    {
        var code = CreateRoom(1);
        var state = Last<S_RoomState>(1);
        Assert.Matches("^[A-HJ-NP-Z2-9]{4}$", code);
        Assert.Equal(1, state.HostId);
        Assert.Single(state.Slots);
    }

    [Fact]
    public void CreateRoom_while_in_room_returns_error()
    {
        CreateRoom(1);
        _lobby.Handle(1, new C_CreateRoom());
        Assert.Equal(ErrorCode.AlreadyInRoom, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Join_is_case_insensitive_and_broadcasts_to_every_member()
    {
        var code = CreateRoom(1);
        Join(2, code.ToLowerInvariant());
        Assert.Equal(2, Last<S_RoomState>(1).Slots.Count);
        Assert.Equal(new[] { 1, 2 }, Last<S_RoomState>(2).Slots.Select(s => s.PlayerId));
    }

    [Fact]
    public void Join_unknown_code_returns_error()
    {
        Join(1, "ZZZZ");
        Assert.Equal(ErrorCode.RoomNotFound, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Join_full_room_returns_error()
    {
        var code = CreateRoom(1);
        for (int id = 2; id <= 4; id++) Join(id, code);
        Join(5, code);
        Assert.Equal(ErrorCode.RoomFull, Last<S_Error>(5).Code);
    }

    [Fact]
    public void Host_leaving_passes_host_to_next_member()
    {
        var code = CreateRoom(1);
        Join(2, code);
        Join(3, code);
        _lobby.Handle(1, new C_LeaveRoom());

        var state = Last<S_RoomState>(2);
        Assert.Equal(2, state.HostId);
        Assert.Equal(new[] { 2, 3 }, state.Slots.Select(s => s.PlayerId));
        Assert.Equal("", Last<S_RoomState>(1).Code);
    }

    [Fact]
    public void Last_member_leaving_deletes_room()
    {
        var code = CreateRoom(1);
        _lobby.Handle(1, new C_LeaveRoom());
        Join(2, code);
        Assert.Equal(ErrorCode.RoomNotFound, Last<S_Error>(2).Code);
    }

    [Fact]
    public void Leave_when_not_in_room_returns_error()
    {
        Login(1);
        _lobby.Handle(1, new C_LeaveRoom());
        Assert.Equal(ErrorCode.NotInRoom, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Disconnect_frees_nickname_and_slot()
    {
        var code = CreateRoom(1);
        Join(2, code);
        _lobby.OnDisconnect(1);

        Assert.Equal(new[] { 2 }, Last<S_RoomState>(2).Slots.Select(s => s.PlayerId));
        Login(3, "player1");
        Assert.True(Last<S_LoginResult>(3).Ok);
    }

    void Pick(int id, ClassType classType, Gender gender = Gender.Male) =>
        _lobby.Handle(id, new C_PickClass { ClassType = classType, Gender = gender });

    void Ready(int id, bool ready = true) =>
        _lobby.Handle(id, new C_Ready { Ready = ready });

    string ReadyRoomOfTwo()
    {
        var code = CreateRoom(1);
        Join(2, code);
        Pick(1, ClassType.Warrior);
        Ready(1);
        Pick(2, ClassType.Bard);
        Ready(2);
        return code;
    }

    [Fact]
    public void PickClass_sets_class_and_gender()
    {
        CreateRoom(1);
        Pick(1, ClassType.Bard, Gender.Female);
        var slot = Last<S_RoomState>(1).Slots.Single();
        Assert.Equal(ClassType.Bard, slot.ClassType);
        Assert.Equal(Gender.Female, slot.Gender);
    }

    [Fact]
    public void PickClass_taken_by_other_member_returns_error()
    {
        var code = CreateRoom(1);
        Join(2, code);
        Pick(1, ClassType.Warrior);
        Pick(2, ClassType.Warrior);
        Assert.Equal(ErrorCode.ClassTaken, Last<S_Error>(2).Code);
    }

    [Fact]
    public void PickClass_none_returns_error()
    {
        CreateRoom(1);
        Pick(1, ClassType.None);
        Assert.Equal(ErrorCode.InvalidRequest, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Changing_class_clears_ready()
    {
        CreateRoom(1);
        Pick(1, ClassType.Mage);
        Ready(1);
        Pick(1, ClassType.Archer);
        Assert.False(Last<S_RoomState>(1).Slots.Single().Ready);
    }

    [Fact]
    public void Ready_without_class_returns_error()
    {
        CreateRoom(1);
        Ready(1);
        Assert.Equal(ErrorCode.ClassNotPicked, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Start_by_non_host_returns_error()
    {
        ReadyRoomOfTwo();
        _lobby.Handle(2, new C_StartGame());
        Assert.Equal(ErrorCode.NotHost, Last<S_Error>(2).Code);
    }

    [Fact]
    public void Start_alone_returns_error()
    {
        CreateRoom(1);
        Pick(1, ClassType.Warrior);
        Ready(1);
        _lobby.Handle(1, new C_StartGame());
        Assert.Equal(ErrorCode.StartConditionNotMet, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Start_with_unready_member_returns_error()
    {
        ReadyRoomOfTwo();
        Ready(2, false);
        _lobby.Handle(1, new C_StartGame());
        Assert.Equal(ErrorCode.StartConditionNotMet, Last<S_Error>(1).Code);
    }

    [Fact]
    public void Start_sends_game_start_to_all_and_locks_room()
    {
        var code = ReadyRoomOfTwo();
        _lobby.Handle(1, new C_StartGame());

        Assert.NotNull(Last<S_GameStart>(1));
        Assert.NotNull(Last<S_GameStart>(2));
        Join(3, code);
        Assert.Equal(ErrorCode.RoomInGame, Last<S_Error>(3).Code);
    }

    [Fact]
    public void Disconnect_releases_class()
    {
        var code = CreateRoom(1);
        Join(2, code);
        Pick(2, ClassType.Archer);
        _lobby.OnDisconnect(2);
        Pick(1, ClassType.Archer);
        Assert.Equal(ClassType.Archer, Last<S_RoomState>(1).Slots.Single().ClassType);
    }
}
