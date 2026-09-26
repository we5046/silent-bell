namespace SilentBell.Shared.Net
{
    // 헤더의 패킷 ID. 번호는 한 번 정하면 바꾸지 않는다 (배포된 클라이언트와의 호환).
    public enum PacketId : ushort
    {
        C_Login = 1,
        S_LoginResult = 2,
        C_CreateRoom = 3,
        C_JoinRoom = 4,
        S_RoomState = 5,
        S_Error = 6,
        C_PickClass = 7,
        C_Ready = 8,
        C_StartGame = 9,
        C_LeaveRoom = 10,
        S_GameStart = 11,
    }
}
