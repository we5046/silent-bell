using System.Threading.Channels;

namespace SilentBell.Server.Net;

// 여러 스레드가 Push하고, 한 흐름이 넣은 순서대로 하나씩 실행한다.
// 실행되는 쪽(로비, 방)의 로직은 동시에 돌지 않으므로 락이 필요 없다.
public sealed class JobQueue
{
    readonly Channel<Action> _jobs = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });

    public void Push(Action job) => _jobs.Writer.TryWrite(job);

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var job in _jobs.Reader.ReadAllAsync(ct))
            {
                try
                {
                    job();
                }
                catch (Exception e)
                {
                    Log.Error("job", e.ToString());
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
