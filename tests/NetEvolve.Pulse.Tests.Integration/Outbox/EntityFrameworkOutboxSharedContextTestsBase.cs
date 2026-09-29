namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using NetEvolve.Pulse.Tests.Integration.Internals;
using NetEvolve.Pulse.Tests.Integration.Internals.Outbox;

/// <summary>
/// Verifies that publishing an event under the default dispatcher never uses the caller's scoped
/// <see cref="DbContext"/> concurrently from the Entity Framework outbox and a user handler (#812).
/// </summary>
/// <remarks>
/// The user handler occupies the shared context for a while through EF Core's own
/// <see cref="IConcurrencyDetector"/>, exactly like a slow query would, so any outbox write that runs
/// at the same time fails with EF Core's "A second operation was started on this context instance"
/// <see cref="InvalidOperationException"/>. This keeps the test deterministic on fast providers such as
/// SQLite, where a real query would finish before the overlap could be observed.
/// </remarks>
[TestGroup("Outbox")]
[Timeout(300_000)]
public abstract class EntityFrameworkOutboxSharedContextTestsBase(
    IServiceFixture databaseServiceFixture,
    IServiceInitializer databaseInitializer
) : PulseTestsBase(databaseServiceFixture, databaseInitializer)
{
    private const int PublishCount = 3;

    private const string HandlerPayload = "handler";

    /// <summary>How long the user handler occupies the shared context.</summary>
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMilliseconds(250);

    // Test names double as table names and must stay within MySQL's 64-character identifier limit.
    [Test]
    public async Task PublishAsync_WithSharedDbContextHandler_PersistsBoth(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var context = services.GetRequiredService<EntityFrameworkOutboxInitializer.TestDbContext>();

                    var mediator = services.GetRequiredService<IMediator>();

                    for (var i = 0; i < PublishCount; i++)
                    {
                        await mediator
                            .PublishAsync(new SharedContextEvent { Id = $"Test{i:D3}" }, token)
                            .ConfigureAwait(false);
                    }

                    var outbox = services.GetRequiredService<IOutboxRepository>();
                    var pending = await outbox.GetPendingCountAsync(token).ConfigureAwait(false);
                    var handled = await context
                        .OutboxMessages.AsNoTracking()
                        .CountAsync(m => m.Payload == HandlerPayload, token)
                        .ConfigureAwait(false);

                    _ = await Assert.That(pending).IsEqualTo(PublishCount);
                    _ = await Assert.That(handled).IsEqualTo(PublishCount);
                },
                cancellationToken,
                configureServices: services =>
                    services
                        .AddScoped<IEventHandler<SharedContextEvent>, SharedContextHandler>()
                        .Configure<OutboxProcessorOptions>(options => options.DisableProcessing = true)
            )
            .ConfigureAwait(false);

    [SuppressMessage(
        "Major Code Smell",
        "S1144:Unused private types or members should be removed",
        Justification = "Resolved through dependency injection."
    )]
    private sealed class SharedContextHandler(
        EntityFrameworkOutboxInitializer.TestDbContext context,
        TimeProvider timeProvider
    ) : IEventHandler<SharedContextEvent>
    {
        public async Task HandleAsync(SharedContextEvent message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Occupy the shared context the way an in-flight query does.
            using (context.GetService<IConcurrencyDetector>().EnterCriticalSection())
            {
                await Task.Delay(HoldDuration, cancellationToken).ConfigureAwait(false);
            }

            // The handler's own change: a row it writes through the same context.
            var now = timeProvider.GetUtcNow();
            _ = await context
                .OutboxMessages.AddAsync(
                    new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        EventType = typeof(SharedContextEvent),
                        Payload = HandlerPayload,
                        CreatedAt = now,
                        UpdatedAt = now,
                        Status = OutboxMessageStatus.Completed,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            _ = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class SharedContextEvent : IEvent
    {
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }

        public required string Id { get; init; }

        public DateTimeOffset? PublishedAt { get; set; }
    }
}
