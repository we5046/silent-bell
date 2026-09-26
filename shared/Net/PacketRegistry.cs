using System;
using System.Collections.Generic;
using Google.Protobuf;
using SilentBell.Protocol;

namespace SilentBell.Shared.Net
{
    // 패킷 ID와 메시지 타입을 잇는 표. 새 패킷은 PacketId와 여기 두 곳에 추가한다.
    public static class PacketRegistry
    {
        static readonly Dictionary<PacketId, MessageParser> Parsers = new Dictionary<PacketId, MessageParser>();
        static readonly Dictionary<Type, PacketId> Ids = new Dictionary<Type, PacketId>();

        static PacketRegistry()
        {
            Add(PacketId.C_Login, C_Login.Parser);
            Add(PacketId.S_LoginResult, S_LoginResult.Parser);
            Add(PacketId.C_CreateRoom, C_CreateRoom.Parser);
            Add(PacketId.C_JoinRoom, C_JoinRoom.Parser);
            Add(PacketId.S_RoomState, S_RoomState.Parser);
            Add(PacketId.S_Error, S_Error.Parser);
            Add(PacketId.C_PickClass, C_PickClass.Parser);
            Add(PacketId.C_Ready, C_Ready.Parser);
            Add(PacketId.C_StartGame, C_StartGame.Parser);
            Add(PacketId.C_LeaveRoom, C_LeaveRoom.Parser);
            Add(PacketId.S_GameStart, S_GameStart.Parser);
        }

        static void Add<T>(PacketId id, MessageParser<T> parser) where T : IMessage<T>
        {
            Parsers[id] = parser;
            Ids[typeof(T)] = id;
        }

        public static PacketId IdOf(IMessage message)
        {
            return Ids[message.GetType()];
        }

        // 모르는 ID이거나 본문이 깨졌으면 false
        public static bool TryParse(PacketId id, byte[] body, out IMessage message)
        {
            message = null;
            if (!Parsers.TryGetValue(id, out var parser)) return false;
            try
            {
                message = parser.ParseFrom(body);
                return true;
            }
            catch (InvalidProtocolBufferException)
            {
                return false;
            }
        }
    }
}
