namespace NetEvolve.Pulse.Xample.Aot;

using System.Globalization;
using System.Runtime.CompilerServices;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Attributes;

[PulseHandler]
internal sealed class CountdownHandler(InvocationRecorder recorder) : IStreamQueryHandler<CountdownStreamQuery, string>
{
    public async IAsyncEnumerable<string> HandleAsync(
        CountdownStreamQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        recorder.Invocations.Enqueue(nameof(CountdownHandler));
        for (var i = request.From; i > 0; i--)
        {
            await Task.Yield();
            yield return i.ToString(CultureInfo.InvariantCulture);
        }
    }
}
