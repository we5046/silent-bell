using Google.Protobuf;
using SilentBell.Protocol;

namespace SilentBell.Server.Tests;

public class ProtocolTests
{
    [Fact]
    public void Generated_message_round_trips()
    {
        var bytes = new C_Login { Nickname = "로엔" }.ToByteArray();
        Assert.Equal("로엔", C_Login.Parser.ParseFrom(bytes).Nickname);
    }

    [Fact]
    public void Enum_prefix_is_stripped()
    {
        Assert.Equal(1, (int)ClassType.Warrior);
        Assert.Equal(14, (int)ErrorCode.StartConditionNotMet);
    }
}
