namespace NetEvolve.Pulse.Tests.Unit.Redis;

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using StackExchange.Redis;
using TUnit.Core;

/// <summary>
/// Behavioral invariants for <see cref="RedisStreamsMessageTransport"/>.
/// IConnectionMultiplexer/IDatabase are very large interfaces; this file uses
/// <see cref="DispatchProxy"/> to intercept the few methods the transport actually calls
/// (<c>GetDatabase</c>, <c>StreamAddAsync</c>, <c>StreamCreateConsumerGroupAsync</c>) and capture
/// their arguments. Every other call routes to <see cref="NotImplementedException"/> so accidental
/// new dependencies on Redis methods surface immediately.
/// </summary>
[TestGroup("Redis")]
public sealed class RedisStreamsMessageTransportTests
{
#pragma warning disable CA1034 // Test-only nested helper types; not part of any public API.
#pragma warning disable CA1002 // List<T> as accumulator is fine for test fixtures.

    internal sealed record StreamAddCall(RedisKey Key, NameValueEntry[] Fields);

    internal sealed record StreamCreateConsumerGroupCall(RedisKey Key, RedisValue GroupName, RedisValue? Position);

    internal class FakeDatabase : DispatchProxy
    {
        public List<StreamAddCall> StreamAddCalls { get; } = new();
        public List<StreamCreateConsumerGroupCall> StreamCreateConsumerGroupCalls { get; } = new();
        public bool ThrowBusyGroupOnCreateConsumerGroup { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new InvalidOperationException("Null target method");
            }

            switch (targetMethod.Name)
            {
                case nameof(IDatabase.StreamAddAsync):
                {
                    // The transport calls the overload (RedisKey, NameValueEntry[], RedisValue?, int?, bool, CommandFlags)
                    if (args is { Length: >= 2 } && args[0] is RedisKey key && args[1] is NameValueEntry[] fields)
                    {
                        StreamAddCalls.Add(new StreamAddCall(key, fields));
                        return Task.FromResult(RedisValue.Null);
                    }
                    throw new NotSupportedException($"Unexpected StreamAddAsync overload: {targetMethod}");
                }
                case nameof(IDatabase.StreamCreateConsumerGroupAsync):
                {
                    if (args is { Length: >= 3 } && args[0] is RedisKey key && args[1] is RedisValue groupName)
                    {
                        var position = (RedisValue?)args[2];
                        StreamCreateConsumerGroupCalls.Add(new StreamCreateConsumerGroupCall(key, groupName, position));

                        if (ThrowBusyGroupOnCreateConsumerGroup)
                        {
                            throw new RedisServerException("BUSYGROUP Consumer Group name already exists");
                        }

                        return Task.FromResult(true);
                    }
                    throw new NotSupportedException(
                        $"Unexpected StreamCreateConsumerGroupAsync overload: {targetMethod}"
                    );
                }
                default:
                    throw new NotImplementedException($"FakeDatabase has no behavior for {targetMethod}");
            }
        }
    }

    internal class FakeMultiplexer : DispatchProxy
    {
        public IDatabase? Database { get; set; }
        public bool IsConnected { get; set; } = true;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new InvalidOperationException("Null target method");
            }

            if (targetMethod.Name == nameof(IConnectionMultiplexer.GetDatabase))
            {
                return Database!;
            }

            if (targetMethod.Name == $"get_{nameof(IConnectionMultiplexer.IsConnected)}")
            {
                return IsConnected;
            }

            throw new NotImplementedException($"FakeMultiplexer has no behavior for {targetMethod}");
        }
    }

    private static (IConnectionMultiplexer Mux, FakeMultiplexer MuxFake, FakeDatabase Capture) BuildFakes()
    {
        var dbProxy = DispatchProxy.Create<IDatabase, FakeDatabase>();
        var muxProxy = DispatchProxy.Create<IConnectionMultiplexer, FakeMultiplexer>();
        var muxFake = (FakeMultiplexer)(object)muxProxy;
        muxFake.Database = dbProxy;
        return (muxProxy, muxFake, (FakeDatabase)(object)dbProxy);
    }

    private static OutboxMessage CreateOutboxMessage() =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TestRedisEvent),
            Payload = """{"event":"sample"}""",
            CorrelationId = "corr-123",
            CausationId = "cause-456",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RetryCount = 2,
        };

    private sealed record TestRedisEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    [Test]
    public async Task SendAsync_Publishes_one_entry_with_expected_fields(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, _, capture) = BuildFakes();
        var options = Options.Create(new RedisStreamsTransportOptions { StreamKey = "test-stream" });
        using var transport = new RedisStreamsMessageTransport(mux, options);
        var message = CreateOutboxMessage();

        await transport.SendAsync(message, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StreamAddCalls).HasCount(1);
        var call = capture.StreamAddCalls[0];
#pragma warning disable S8969 // RedisKey's implicit string conversion is annotated nullable; the value is never null here
        _ = await Assert.That((string)call.Key!).IsEqualTo("test-stream");
#pragma warning restore S8969

#pragma warning disable S8969 // RedisValue's implicit string conversion is annotated nullable; the value is never null here
        var byName = call.Fields.ToDictionary(f => ((string)f.Name)!, f => ((string)f.Value)!, StringComparer.Ordinal);
#pragma warning restore S8969

        using (Assert.Multiple())
        {
            _ = await Assert.That(byName["id"]).IsEqualTo(message.Id.ToString());
            _ = await Assert.That(byName["eventType"]).IsEqualTo(message.EventType.ToOutboxEventTypeName());
            _ = await Assert.That(byName["payload"]).IsEqualTo(message.Payload);
            _ = await Assert.That(byName["correlationId"]).IsEqualTo(message.CorrelationId);
            _ = await Assert.That(byName["causationId"]).IsEqualTo(message.CausationId);
            _ = await Assert
                .That(byName["retryCount"])
                .IsEqualTo(message.RetryCount.ToString(CultureInfo.InvariantCulture));
            _ = await Assert
                .That(byName["createdAt"])
                .IsEqualTo(message.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        }
    }

    [Test]
    public async Task SendAsync_Creates_consumer_group_once_on_first_use(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, _, capture) = BuildFakes();
        var options = Options.Create(
            new RedisStreamsTransportOptions { CreateStreamIfNotExists = true, ConsumerGroupName = "group-1" }
        );
        using var transport = new RedisStreamsMessageTransport(mux, options);

        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);
        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StreamCreateConsumerGroupCalls).HasCount(1);
        _ = await Assert.That(capture.StreamAddCalls).HasCount(2);
#pragma warning disable S8969 // RedisValue's implicit string conversion is annotated nullable; the value is never null here
        _ = await Assert.That((string)capture.StreamCreateConsumerGroupCalls[0].GroupName!).IsEqualTo("group-1");
#pragma warning restore S8969
    }

    [Test]
    public async Task SendAsync_Tolerates_BUSYGROUP_error_and_still_adds_entry(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, _, capture) = BuildFakes();
        capture.ThrowBusyGroupOnCreateConsumerGroup = true;
        var options = Options.Create(new RedisStreamsTransportOptions { CreateStreamIfNotExists = true });
        using var transport = new RedisStreamsMessageTransport(mux, options);

        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StreamCreateConsumerGroupCalls).HasCount(1);
        _ = await Assert.That(capture.StreamAddCalls).HasCount(1);
    }

    [Test]
    public async Task SendAsync_When_CreateStreamIfNotExists_false_does_not_create_consumer_group(
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, _, capture) = BuildFakes();
        var options = Options.Create(new RedisStreamsTransportOptions { CreateStreamIfNotExists = false });
        using var transport = new RedisStreamsMessageTransport(mux, options);

        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StreamCreateConsumerGroupCalls).HasCount(0);
        _ = await Assert.That(capture.StreamAddCalls).HasCount(1);
    }

    [Test]
    public async Task SendBatchAsync_Sends_all_messages_in_order(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, _, capture) = BuildFakes();
        var options = Options.Create(new RedisStreamsTransportOptions());
        using var transport = new RedisStreamsMessageTransport(mux, options);

        var messages = Enumerable.Range(0, 3).Select(_ => CreateOutboxMessage()).ToArray();

        await transport.SendBatchAsync(messages, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(capture.StreamAddCalls).HasCount(3);

        for (var i = 0; i < messages.Length; i++)
        {
            var byName = capture
                .StreamAddCalls[i]
#pragma warning disable S8969 // RedisValue's implicit string conversion is annotated nullable; the value is never null here
                .Fields.ToDictionary(f => ((string)f.Name)!, f => ((string)f.Value)!, StringComparer.Ordinal);
#pragma warning restore S8969
            _ = await Assert.That(byName["id"]).IsEqualTo(messages[i].Id.ToString());
        }
    }

    [Test]
    public async Task IsHealthyAsync_When_multiplexer_connected_returns_true(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, muxFake, _) = BuildFakes();
        muxFake.IsConnected = true;
        var options = Options.Create(new RedisStreamsTransportOptions());
        using var transport = new RedisStreamsMessageTransport(mux, options);

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsTrue();
    }

    [Test]
    public async Task IsHealthyAsync_When_multiplexer_disconnected_returns_false(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (mux, muxFake, _) = BuildFakes();
        muxFake.IsConnected = false;
        var options = Options.Create(new RedisStreamsTransportOptions());
        using var transport = new RedisStreamsMessageTransport(mux, options);

        var healthy = await transport.IsHealthyAsync(cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(healthy).IsFalse();
    }

    [Test]
    public async Task Constructor_When_multiplexer_null_throws()
    {
        IConnectionMultiplexer multiplexer = null!;
        var options = Options.Create(new RedisStreamsTransportOptions());

        var exception = Assert.Throws<ArgumentNullException>(() =>
            _ = new RedisStreamsMessageTransport(multiplexer, options)
        );

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception.ParamName).IsEqualTo("multiplexer");
    }

    [Test]
    public async Task Constructor_When_options_null_throws()
    {
        var (mux, _, _) = BuildFakes();
        IOptions<RedisStreamsTransportOptions> options = null!;

        var exception = Assert.Throws<ArgumentNullException>(() => _ = new RedisStreamsMessageTransport(mux, options));

        _ = await Assert.That(exception).IsNotNull();
        _ = await Assert.That(exception.ParamName).IsEqualTo("options");
    }
}
