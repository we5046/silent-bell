using SilentBell.Protocol;

namespace SilentBell.Server.Lobby;

sealed class Player
{
    public required int Id { get; init; }
    public required string Nickname { get; init; }
    public Room? Room { get; set; }
}

sealed class Room
{
    public required string Code { get; init; }
    public int HostId { get; set; }
    public bool InGame { get; set; }
    public List<Member> Members { get; } = new(); // 입장 순서. 방장 승계에 쓴다.
}

sealed class Member
{
    public required int PlayerId { get; init; }
    public required string Nickname { get; init; }
    public ClassType ClassType { get; set; }
    public Gender Gender { get; set; }
    public bool Ready { get; set; }
}
