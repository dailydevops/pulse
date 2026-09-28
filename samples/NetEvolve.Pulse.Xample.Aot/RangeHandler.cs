namespace NetEvolve.Pulse.Xample.Aot;

using System.Runtime.CompilerServices;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Attributes;

[PulseHandler]
internal sealed class RangeHandler : IStreamQueryHandler<RangeStreamQuery, int>
{
    public async IAsyncEnumerable<int> HandleAsync(
        RangeStreamQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        for (var i = 1; i <= request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}
