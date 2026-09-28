namespace NetEvolve.Pulse.Tests.Unit.RabbitMQ;

using global::RabbitMQ.Client;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Internals;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("RabbitMQ")]
public sealed class RabbitMqConnectionAdapterTests
{
    [Test]
    public async Task Constructor_When_connection_null_throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => _ = new RabbitMqConnectionAdapter(null!));

        _ = await Assert.That(exception.ParamName).IsEqualTo("connection");
    }

    [Test]
    public async Task CreateChannelAsync_Enables_publisher_confirmations_with_tracking(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var channel = Mock.Of<IChannel>();
        var connection = Mock.Of<IConnection>();
        _ = connection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(channel.Object);
        var adapter = new RabbitMqConnectionAdapter(connection.Object);

        using var created = await adapter.CreateChannelAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(created).IsNotNull();
        connection
            .CreateChannelAsync(
                Arg.Is<CreateChannelOptions?>(o =>
                    o is not null && o.PublisherConfirmationsEnabled && o.PublisherConfirmationTrackingEnabled
                ),
                Arg.Any<CancellationToken>()
            )
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task CreateChannelAsync_Disables_outstanding_confirmations_rate_limiter(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var channel = Mock.Of<IChannel>();
        var connection = Mock.Of<IConnection>();
        _ = connection
            .CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(channel.Object);
        var adapter = new RabbitMqConnectionAdapter(connection.Object);

        using var created = await adapter.CreateChannelAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(created).IsNotNull();
        connection
            .CreateChannelAsync(
                Arg.Is<CreateChannelOptions?>(o =>
                    o is not null && o.OutstandingPublisherConfirmationsRateLimiter is null
                ),
                Arg.Any<CancellationToken>()
            )
            .WasCalled(Times.Once);
    }
}
