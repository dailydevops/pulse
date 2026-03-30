namespace NetEvolve.Pulse.Internals;

using Confluent.Kafka;

/// <summary>
/// Internal abstraction over <see cref="IAdminClient" /> to enable unit testing
/// without a live Kafka broker.
/// </summary>
internal interface IKafkaAdminAdapter
{
    /// <summary>
    /// Retrieves Kafka cluster metadata within the specified <paramref name="timeout" />.
    /// </summary>
    Metadata GetMetadata(TimeSpan timeout);
}
