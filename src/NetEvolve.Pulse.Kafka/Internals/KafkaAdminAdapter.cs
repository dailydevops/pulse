namespace NetEvolve.Pulse.Internals;

using Confluent.Kafka;

internal sealed class KafkaAdminAdapter : IKafkaAdminAdapter, IDisposable
{
    private readonly Func<TimeSpan, Metadata> _getMetadata;
    private readonly Action _dispose;

    public KafkaAdminAdapter(IAdminClient adminClient)
    {
        ArgumentNullException.ThrowIfNull(adminClient);

        _getMetadata = adminClient.GetMetadata;
        _dispose = adminClient.Dispose;
    }

    internal KafkaAdminAdapter(Func<TimeSpan, Metadata> getMetadata, Action dispose)
    {
        ArgumentNullException.ThrowIfNull(getMetadata);
        ArgumentNullException.ThrowIfNull(dispose);

        _getMetadata = getMetadata;
        _dispose = dispose;
    }

    /// <inheritdoc />
    public Metadata GetMetadata(TimeSpan timeout) => _getMetadata(timeout);

    /// <inheritdoc />
    public void Dispose() => _dispose();
}
