namespace NetEvolve.Pulse.Tests.Unit.AzureQueueStorage;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("AzureQueueStorage")]
public sealed class AzureQueueStorageMessageTransportTests
{
    // ── Constructor guards ────────────────────────────────────────────────────

    [Test]
    public async Task Constructor_When_options_is_null_throws_ArgumentNullException()
    {
        IOptions<AzureQueueStorageTransportOptions> options = null!;

        _ = await Assert.That(() => new AzureQueueStorageMessageTransport(options)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_With_queueClient_When_options_is_null_throws_ArgumentNullException()
    {
        IOptions<AzureQueueStorageTransportOptions> options = null!;
        var fakeClient = new FakeQueueClient();

        _ = await Assert
            .That(() => new AzureQueueStorageMessageTransport(options, fakeClient))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_With_queueClient_When_queueClient_is_null_throws_ArgumentNullException()
    {
        var options = Options.Create(
            new AzureQueueStorageTransportOptions { ConnectionString = "UseDevelopmentStorage=true" }
        );
        QueueClient nullClient = null!;

        _ = await Assert
            .That(() => new AzureQueueStorageMessageTransport(options, nullClient))
            .Throws<ArgumentNullException>();
    }

    // ── SendAsync null guard ──────────────────────────────────────────────────

    [Test]
    public async Task SendAsync_When_message_is_null_throws_ArgumentNullException(CancellationToken cancellationToken)
    {
        var fakeClient = new FakeQueueClient();
        using var transport = CreateTransport(fakeClient);

        _ = await Assert.That(() => transport.SendAsync(null!, cancellationToken)).Throws<ArgumentNullException>();
    }

    // ── SendAsync happy path ──────────────────────────────────────────────────

    [Test]
    public async Task SendAsync_Sends_base64_encoded_message(CancellationToken cancellationToken)
    {
        var fakeClient = new FakeQueueClient();
        using var transport = CreateTransport(fakeClient);
        var message = CreateOutboxMessage();

        await transport.SendAsync(message, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(fakeClient.SentMessages).HasSingleItem();

        var sentBase64 = fakeClient.SentMessages[0];
        var decodedBytes = Convert.FromBase64String(sentBase64);
        var json = Encoding.UTF8.GetString(decodedBytes);

        _ = await Assert.That(json).IsNotNullOrEmpty();
        using var doc = JsonDocument.Parse(json);
        _ = await Assert.That(doc.RootElement.GetProperty("id").GetGuid()).IsEqualTo(message.Id);
        _ = await Assert
            .That(doc.RootElement.GetProperty("eventType").GetString())
            .IsEqualTo(message.EventType.ToOutboxEventTypeName());
        _ = await Assert.That(doc.RootElement.GetProperty("payload").GetString()).IsEqualTo(message.Payload);
    }

    [Test]
    public async Task SendAsync_Passes_visibility_timeout_when_configured(CancellationToken cancellationToken)
    {
        var fakeClient = new FakeQueueClient();
        var timeout = TimeSpan.FromMinutes(5);
        var options = Options.Create(
            new AzureQueueStorageTransportOptions
            {
                ConnectionString = "UseDevelopmentStorage=true",
                MessageVisibilityTimeout = timeout,
            }
        );
        using var transport = new AzureQueueStorageMessageTransport(options, fakeClient);

        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(fakeClient.LastVisibilityTimeout).IsEqualTo(timeout);
    }

    [Test]
    public async Task SendAsync_Does_not_pass_visibility_timeout_when_not_configured(
        CancellationToken cancellationToken
    )
    {
        var fakeClient = new FakeQueueClient();
        using var transport = CreateTransport(fakeClient);

        await transport.SendAsync(CreateOutboxMessage(), cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(fakeClient.LastVisibilityTimeout).IsNull();
    }

    // ── SendAsync oversized message ───────────────────────────────────────────

    [Test]
    public async Task SendAsync_When_message_exceeds_48KB_throws_InvalidOperationException(
        CancellationToken cancellationToken
    )
    {
        var fakeClient = new FakeQueueClient();
        using var transport = CreateTransport(fakeClient);

        // Create a large payload that exceeds 48 KB raw
        var largePayload = new string('x', 50 * 1024);
        var message = CreateOutboxMessage(payload: largePayload);

        _ = await Assert
            .That(() => transport.SendAsync(message, cancellationToken))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SendAsync_When_message_is_small_does_not_throw(CancellationToken cancellationToken)
    {
        var fakeClient = new FakeQueueClient();
        using var transport = CreateTransport(fakeClient);
        var message = CreateOutboxMessage();

        // This should not throw
        await transport.SendAsync(message, cancellationToken).ConfigureAwait(false);
        _ = await Assert.That(fakeClient.SentMessages).HasSingleItem();
    }

    // ── SendBatchAsync ────────────────────────────────────────────────────────

    [Test]
    public async Task SendBatchAsync_When_messages_is_null_throws_ArgumentNullException(
        CancellationToken cancellationToken
    )
    {
        var fakeClient = new FakeQueueClient();
        using var transport = CreateTransport(fakeClient);

        _ = await Assert.That(() => transport.SendBatchAsync(null!, cancellationToken)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task SendBatchAsync_When_messages_is_empty_does_nothing(CancellationToken cancellationToken)
    {
        var fakeClient = new FakeQueueClient();
        using var transport = CreateTransport(fakeClient);

        await transport.SendBatchAsync([], cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(fakeClient.SentMessages).IsEmpty();
    }

    [Test]
    public async Task SendBatchAsync_Sends_each_message_sequentially(CancellationToken cancellationToken)
    {
        var fakeClient = new FakeQueueClient();
        using var transport = CreateTransport(fakeClient);
        var messages = new[] { CreateOutboxMessage(), CreateOutboxMessage(), CreateOutboxMessage() };

        await transport.SendBatchAsync(messages, cancellationToken).ConfigureAwait(false);

        _ = await Assert.That(fakeClient.SentMessages.Count).IsEqualTo(3);
    }

    [Test]
    public async Task SendBatchAsync_When_one_message_is_oversized_throws_InvalidOperationException(
        CancellationToken cancellationToken
    )
    {
        var fakeClient = new FakeQueueClient();
        using var transport = CreateTransport(fakeClient);
        var largePayload = new string('x', 50 * 1024);
        var messages = new[]
        {
            CreateOutboxMessage(),
            CreateOutboxMessage(payload: largePayload),
            CreateOutboxMessage(),
        };

        _ = await Assert
            .That(() => transport.SendBatchAsync(messages, cancellationToken))
            .Throws<InvalidOperationException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AzureQueueStorageMessageTransport CreateTransport(FakeQueueClient fakeClient)
    {
        var options = Options.Create(
            new AzureQueueStorageTransportOptions { ConnectionString = "UseDevelopmentStorage=true" }
        );
        return new AzureQueueStorageMessageTransport(options, fakeClient);
    }

    private static OutboxMessage CreateOutboxMessage(string? payload = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TestOutboxEvent),
            Payload = payload ?? """{"data":"test"}""",
            CorrelationId = "corr-123",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RetryCount = 0,
        };

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeQueueClient : QueueClient
    {
        public List<string> SentMessages { get; } = [];
        public TimeSpan? LastVisibilityTimeout { get; private set; }
        public int CreateIfNotExistsCallCount { get; private set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "FakeAzureResponse.Dispose is a no-op; suppressed for test code."
        )]
        public override Task<Response<SendReceipt>> SendMessageAsync(
            string messageText,
            TimeSpan? visibilityTimeout = null,
            TimeSpan? timeToLive = null,
            CancellationToken cancellationToken = default
        )
        {
            SentMessages.Add(messageText);
            LastVisibilityTimeout = visibilityTimeout;
            var receipt = QueuesModelFactory.SendReceipt(
                messageId: Guid.NewGuid().ToString(),
                insertionTime: DateTimeOffset.UtcNow,
                expirationTime: DateTimeOffset.UtcNow.AddDays(7),
                popReceipt: "pop-receipt",
                timeNextVisible: DateTimeOffset.UtcNow
            );
            return Task.FromResult(Response.FromValue(receipt, new FakeAzureResponse()));
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "FakeAzureResponse.Dispose is a no-op; suppressed for test code."
        )]
        public override Task<Response> CreateIfNotExistsAsync(
            IDictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default
        )
        {
            CreateIfNotExistsCallCount++;
            return Task.FromResult<Response>(new FakeAzureResponse());
        }
    }

    private sealed class FakeAzureResponse : Response
    {
        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream
        {
            get => null;
            set { }
        }
        public override string ClientRequestId
        {
            get => string.Empty;
            set { }
        }

        public override void Dispose() { }

        protected override bool ContainsHeader(string name) => false;

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];

        protected override bool TryGetHeader(
            string name,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value
        )
        {
            value = null;
            return false;
        }

        protected override bool TryGetHeaderValues(
            string name,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEnumerable<string>? values
        )
        {
            values = null;
            return false;
        }
    }

    private sealed record TestOutboxEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
