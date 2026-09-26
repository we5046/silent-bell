using SilentBell.Bot;
using SilentBell.Protocol;
using SilentBell.Server.Net;

namespace SilentBell.Server.Tests;

public class GameServerTests : IAsyncLifetime
{
    readonly GameServer _server = new(0);
    readonly CancellationTokenSource _cts = new();
    Task _run = Task.CompletedTask;

    public Task InitializeAsync()
    {
        _server.Start();
        _run = _server.RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        await _run;
    }

    [Fact]
    public async Task Clients_login_and_share_a_room_over_tcp()
    {
        using var host = await BotConnection.ConnectAsync("127.0.0.1", _server.Port);
        using var guest = await BotConnection.ConnectAsync("127.0.0.1", _server.Port);

        await host.SendAsync(new C_Login { Nickname = "host" });
        await host.ReceiveAsync<S_LoginResult>();
        await guest.SendAsync(new C_Login { Nickname = "guest" });
        await guest.ReceiveAsync<S_LoginResult>();

        await host.SendAsync(new C_CreateRoom());
        var code = (await host.ReceiveAsync<S_RoomState>()).Code;
        await guest.SendAsync(new C_JoinRoom { Code = code });

        var state = await host.ReceiveAsync<S_RoomState>(s => s.Slots.Count == 2);
        Assert.Equal(new[] { "host", "guest" }, state.Slots.Select(s => s.Nickname));
    }

    [Fact]
    public async Task Invalid_length_header_closes_connection()
    {
        using var bot = await BotConnection.ConnectAsync("127.0.0.1", _server.Port);
        await bot.SendRawAsync(new byte[] { 0x02, 0x00, 0x01, 0x00 }); // 길이 2 < 최소 4
        await Assert.ThrowsAsync<IOException>(() => bot.ReceiveAsync<S_Error>());
    }

    [Fact]
    public async Task RunAsync_surfaces_unexpected_accept_failure_instead_of_hanging()
    {
        var notStarted = new GameServer(0);
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => notStarted.RunAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(3)));
    }
}
