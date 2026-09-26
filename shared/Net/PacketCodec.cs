using System;
using System.Buffers.Binary;
using Google.Protobuf;

namespace SilentBell.Shared.Net
{
    // [길이 u16 LE, 헤더 포함][ID u16 LE][Protobuf 본문]
    public static class PacketCodec
    {
        public const int HeaderSize = 4;
        public const int MaxPacketSize = 4096;

        public static byte[] Encode(IMessage message)
        {
            var id = PacketRegistry.IdOf(message);
            var body = message.ToByteArray();
            int size = HeaderSize + body.Length;
            if (size > MaxPacketSize)
                throw new InvalidOperationException($"packet too large: {id} {size} bytes");

            var packet = new byte[size];
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), (ushort)size);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), (ushort)id);
            Buffer.BlockCopy(body, 0, packet, HeaderSize, body.Length);
            return packet;
        }
    }
}
