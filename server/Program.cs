using SilentBell.Server;
using SilentBell.Server.Net;

int port = args.Length > 0 ? int.Parse(args[0]) : 7777;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var server = new GameServer(port);
server.Start();
Log.Info("server", $"listening on {server.Port}");
await server.RunAsync(cts.Token);
