using SilentBell.Bot;

string host = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 ? int.Parse(args[1]) : 7777;
int count = args.Length > 2 ? int.Parse(args[2]) : 4;

try
{
    var code = await BotScenario.RunAsync(host, port, count);
    Console.WriteLine($"OK: room {code} started with {count} bots");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"FAIL: {e.GetType().Name} {e.Message}");
    return 1;
}
