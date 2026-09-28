namespace NetEvolve.Pulse.Tests.Unit.AspNetCore.Grpc;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using global::Grpc.Core;

// Implements only WriteAsync(T); the token overload keeps its default implementation, like many custom writers.
internal sealed class CollectingStreamWriter(Action? onWrite = null) : IServerStreamWriter<string>
{
    public List<string> Items { get; } = [];

    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(string message)
    {
        Items.Add(message);
        onWrite?.Invoke();
        return Task.CompletedTask;
    }
}
