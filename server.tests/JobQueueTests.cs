using SilentBell.Server.Net;

namespace SilentBell.Server.Tests;

public class JobQueueTests
{
    [Fact]
    public async Task Runs_jobs_in_order_and_survives_exceptions()
    {
        var queue = new JobQueue();
        var log = new List<int>();
        var done = new TaskCompletionSource();
        queue.Push(() => log.Add(1));
        queue.Push(() => throw new InvalidOperationException("boom"));
        queue.Push(() => log.Add(2));
        queue.Push(() => done.SetResult());

        using var cts = new CancellationTokenSource();
        var run = queue.RunAsync(cts.Token);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cts.Cancel();
        await run;

        Assert.Equal(new[] { 1, 2 }, log);
    }
}
