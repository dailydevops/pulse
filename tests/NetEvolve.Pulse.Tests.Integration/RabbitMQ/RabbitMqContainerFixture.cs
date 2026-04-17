namespace NetEvolve.Pulse.Tests.Integration.RabbitMQ;

using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.RabbitMq;
using TUnit.Core.Interfaces;

/// <summary>
/// Provides a shared RabbitMQ container fixture for integration tests.
/// </summary>
public sealed class RabbitMqContainerFixture : IAsyncDisposable, IAsyncInitializer
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder().WithLogger(NullLogger.Instance).Build();

    /// <summary>
    /// Gets the connection string for the running RabbitMQ container.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <inheritdoc />
    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);
}
