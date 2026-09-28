namespace NetEvolve.Pulse.Tests.Unit.AspNetCore.Grpc;

using System.Threading;
using System.Threading.Tasks;
using global::Grpc.Core;

// Simulates a transport that stops draining the response: every write stays pending forever.
internal sealed class PendingStreamWriter : IServerStreamWriter<string>
{
    private readonly TaskCompletionSource _writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WriteStarted => _writeStarted.Task;

    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(string message)
    {
        _ = _writeStarted.TrySetResult();
        return Task.Delay(Timeout.Infinite);
    }
}
