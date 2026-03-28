using System.Threading.Channels;

namespace Company.Function.BackgroundServices;

public class PackagingJobQueue
{
    private readonly Channel<PackagingJob> _channel =
        Channel.CreateBounded<PackagingJob>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

    public async ValueTask EnqueueAsync(PackagingJob job, CancellationToken ct = default)
        => await _channel.Writer.WriteAsync(job, ct);

    public async ValueTask<PackagingJob> DequeueAsync(CancellationToken ct)
        => await _channel.Reader.ReadAsync(ct);
}
