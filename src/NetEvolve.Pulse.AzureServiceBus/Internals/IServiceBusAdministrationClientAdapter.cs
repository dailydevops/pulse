namespace NetEvolve.Pulse.Internals;

internal interface IServiceBusAdministrationClientAdapter
{
    Task<bool> TryGetQueueRuntimePropertiesAsync(string entityPath, CancellationToken cancellationToken);

    Task<bool> TryGetTopicRuntimePropertiesAsync(string entityPath, CancellationToken cancellationToken);
}
