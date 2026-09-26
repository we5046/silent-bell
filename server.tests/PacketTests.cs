using System.Buffers.Binary;
using SilentBell.Protocol;
using SilentBell.Shared.Net;

namespace SilentBell.Server.Tests;

public class PacketTests
{
    static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    [Fact]
    public void Encode_writes_total_length_and_id_little_endian()
    {
        var packet = PacketCodec.Encode(new C_Login { Nickname = "로엔" });
        Assert.Equal(packet.Length, BinaryPrimitives.ReadUInt16LittleEndian(packet));
        Assert.Equal((ushort)PacketId.C_Login, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2)));
    }

    [Fact]
    public void Encode_rejects_packet_over_max_size()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PacketCodec.Encode(new C_Login { Nickname = new string('a', PacketCodec.MaxPacketSize) }));
    }

    [Fact]
    public void Assembler_reads_packet_arriving_one_byte_at_a_time()
    {
        var packet = PacketCodec.Encode(new C_Login { Nickname = "로엔" });
        var assembler = new PacketAssembler();
        for (int i = 0; i < packet.Length - 1; i++)
        {
            assembler.Append(packet.AsSpan(i, 1));
            Assert.False(assembler.TryRead(out _, out _));
        }
        assembler.Append(packet.AsSpan(packet.Length - 1, 1));

        Assert.True(assembler.TryRead(out var id, out var body));
        Assert.True(PacketRegistry.TryParse(id, body, out var message));
        Assert.Equal("로엔", ((C_Login)message).Nickname);
    }

    [Fact]
    public void Assembler_reads_several_packets_from_one_chunk()
    {
        var assembler = new PacketAssembler();
        assembler.Append(Concat(
            PacketCodec.Encode(new C_CreateRoom()),
            PacketCodec.Encode(new C_JoinRoom { Code = "AB23" })));

        Assert.True(assembler.TryRead(out var first, out _));
        Assert.True(assembler.TryRead(out var second, out var body));
        Assert.False(assembler.TryRead(out _, out _));
        Assert.Equal(PacketId.C_CreateRoom, first);
        Assert.Equal(PacketId.C_JoinRoom, second);
        Assert.True(PacketRegistry.TryParse(second, body, out var join));
        Assert.Equal("AB23", ((C_JoinRoom)join).Code);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4097)]
    public void Assembler_rejects_invalid_length(int size)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)size);
        var assembler = new PacketAssembler();
        assembler.Append(header);
        Assert.Throws<InvalidDataException>(() => assembler.TryRead(out _, out _));
    }

    [Fact]
    public void Registry_rejects_unknown_id()
    {
        Assert.False(PacketRegistry.TryParse((PacketId)999, Array.Empty<byte>(), out _));
    }

    [Fact]
    public void Every_packet_id_has_a_parser()
    {
        foreach (var id in Enum.GetValues<PacketId>())
            Assert.True(PacketRegistry.TryParse(id, Array.Empty<byte>(), out _), id.ToString());
    }

    [Fact]
    public void IdOf_matches_registered_id()
    {
        Assert.Equal(PacketId.S_RoomState, PacketRegistry.IdOf(new S_RoomState()));
    }
}
