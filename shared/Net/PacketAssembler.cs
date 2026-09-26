using System;
using System.Buffers.Binary;
using System.IO;

namespace SilentBell.Shared.Net
{
    // TCP는 경계가 없는 스트림이라, 받은 바이트를 모았다가 길이 헤더 기준으로 패킷을 잘라 낸다.
    // 한 번에 Append하는 양은 8192바이트 이하여야 한다 (남은 조각 최대 4095 + 8192 < 16384).
    public sealed class PacketAssembler
    {
        readonly byte[] _buffer = new byte[PacketCodec.MaxPacketSize * 4];
        int _length;

        public void Append(ReadOnlySpan<byte> data)
        {
            if (_length + data.Length > _buffer.Length)
                throw new InvalidDataException("receive buffer overflow");
            data.CopyTo(_buffer.AsSpan(_length));
            _length += data.Length;
        }

        // 완성된 패킷이 있으면 꺼내고 true. 길이 헤더가 규약 밖이면 InvalidDataException.
        public bool TryRead(out PacketId id, out byte[] body)
        {
            id = default;
            body = null;
            if (_length < 2) return false;

            int size = BinaryPrimitives.ReadUInt16LittleEndian(_buffer);
            if (size < PacketCodec.HeaderSize || size > PacketCodec.MaxPacketSize)
                throw new InvalidDataException($"invalid packet size: {size}");
            if (_length < size) return false;

            id = (PacketId)BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(2));
            body = _buffer.AsSpan(PacketCodec.HeaderSize, size - PacketCodec.HeaderSize).ToArray();
            Buffer.BlockCopy(_buffer, size, _buffer, 0, _length - size);
            _length -= size;
            return true;
        }
    }
}
