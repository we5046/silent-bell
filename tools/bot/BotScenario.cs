using Google.Protobuf;
using SilentBell.Protocol;

namespace SilentBell.Bot;

public static class BotScenario
{
    static readonly ClassType[] Classes = { ClassType.Warrior, ClassType.Mage, ClassType.Archer, ClassType.Bard };

    // 봇 count명이 방을 만들고 참가해 서로 다른 클래스를 고르고 게임을 시작한다. 방 코드를 반환한다.
    public static async Task<string> RunAsync(string host, int port, int count)
    {
        if (count < 2 || count > Classes.Length) throw new ArgumentOutOfRangeException(nameof(count), "2~4");

        var bots = new List<BotConnection>();
        try
        {
            int tag = Random.Shared.Next(1000, 10000); // 여러 번 실행해도 닉네임이 겹치지 않게
            for (int i = 0; i < count; i++)
            {
                var bot = await BotConnection.ConnectAsync(host, port);
                bots.Add(bot);
                await bot.SendAsync(new C_Login { Nickname = $"bot{i}_{tag}" });
                var reply = await bot.ReceiveAsync<IMessage>(m => m is S_LoginResult || m is S_Error);
                if (reply is S_Error error) throw new InvalidOperationException($"bot{i} login failed: {error.Code}");
            }

            await bots[0].SendAsync(new C_CreateRoom());
            var code = (await bots[0].ReceiveAsync<S_RoomState>()).Code;
            for (int i = 1; i < count; i++)
            {
                await bots[i].SendAsync(new C_JoinRoom { Code = code });
                await bots[i].ReceiveAsync<S_RoomState>(s => s.Code == code);
            }

            for (int i = 0; i < count; i++)
            {
                await bots[i].SendAsync(new C_PickClass { ClassType = Classes[i], Gender = i % 2 == 0 ? Gender.Male : Gender.Female });
                await bots[i].SendAsync(new C_Ready { Ready = true });
            }
            await bots[0].ReceiveAsync<S_RoomState>(s => s.Slots.Count == count && s.Slots.All(slot => slot.Ready));

            await bots[0].SendAsync(new C_StartGame());
            foreach (var bot in bots) await bot.ReceiveAsync<S_GameStart>();
            return code;
        }
        finally
        {
            foreach (var bot in bots) bot.Dispose();
        }
    }
}
