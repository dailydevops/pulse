namespace NetEvolve.Pulse.Tests.Integration.Audit;

using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Audit;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Tests.Integration.Internals;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("Audit")]
[Timeout(300_000)] // Increased timeout to accommodate potential delays in CI environments.
public abstract class AuditTestsBase(IServiceFixture databaseServiceFixture, IServiceInitializer databaseInitializer)
{
    protected IServiceFixture DatabaseServiceFixture { get; } = databaseServiceFixture;
    protected IServiceInitializer DatabaseInitializer { get; } = databaseInitializer;

    protected async ValueTask RunAndVerify(
        Func<IServiceProvider, CancellationToken, Task> testableCode,
        CancellationToken cancellationToken,
        Action<IServiceCollection>? configureServices = null,
        [CallerMemberName] string tableName = null!
    )
    {
        ArgumentNullException.ThrowIfNull(testableCode);

        using var host = new HostBuilder()
            .ConfigureAppConfiguration((hostContext, configBuilder) => { })
            .ConfigureServices(services =>
            {
                DatabaseInitializer.Initialize(services, DatabaseServiceFixture);
                configureServices?.Invoke(services);
                _ = services
                    .AddPulse(mediatorBuilder => DatabaseInitializer.Configure(mediatorBuilder, DatabaseServiceFixture))
                    .Configure<AuditStoreOptions>(options =>
                    {
                        options.TableName = tableName;
                        options.Schema = TestHelper.TargetFramework;
                    });
            })
            .ConfigureWebHost(webBuilder => _ = webBuilder.UseTestServer().Configure(applicationBuilder => { }))
            .Build();

        await DatabaseInitializer.CreateDatabaseAsync(host.Services, cancellationToken).ConfigureAwait(false);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        using var server = host.GetTestServer();

        using (Assert.Multiple())
        {
            var scope = server.Services.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                await testableCode.Invoke(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        await host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AuditRecord CreateRecord(
        string commandType = "Test.Command",
        string? userId = "user-1",
        string? correlationId = "corr-1",
        DateTimeOffset? occurredAt = null,
        double durationMs = 12.5,
        AuditResult result = AuditResult.Success,
        string? payload = null,
        string? exceptionMessage = null
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommandType = commandType,
            UserId = userId,
            CorrelationId = correlationId,
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
            DurationMs = durationMs,
            Result = result,
            Payload = payload,
            ExceptionMessage = exceptionMessage,
        };

    [Test]
    public async Task RecordAsync_Then_QueryAsync_Returns_record(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IAuditStore>();
                    var management = services.GetRequiredService<IAuditManagement>();

                    var record = CreateRecord(
                        commandType: "Test.CreateOrderCommand",
                        userId: "alice",
                        correlationId: "corr-xyz",
                        payload: """{"orderId":1}"""
                    );
                    await store.RecordAsync(record, token).ConfigureAwait(false);

                    var results = await management.QueryAsync(new AuditFilter(), token).ConfigureAwait(false);

                    _ = await Assert.That(results).HasSingleItem();
                    var stored = results[0];

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(stored.CommandType).IsEqualTo(record.CommandType);
                        _ = await Assert.That(stored.UserId).IsEqualTo(record.UserId);
                        _ = await Assert.That(stored.CorrelationId).IsEqualTo(record.CorrelationId);
                        _ = await Assert.That(stored.Result).IsEqualTo(AuditResult.Success);
                        _ = await Assert.That(stored.Payload).IsEqualTo(record.Payload);
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task QueryAsync_Filters_by_CommandType(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IAuditStore>();
                    var management = services.GetRequiredService<IAuditManagement>();

                    await store.RecordAsync(CreateRecord(commandType: "Test.CommandA"), token).ConfigureAwait(false);
                    await store.RecordAsync(CreateRecord(commandType: "Test.CommandB"), token).ConfigureAwait(false);

                    var results = await management
                        .QueryAsync(new AuditFilter { CommandType = "Test.CommandA" }, token)
                        .ConfigureAwait(false);

                    _ = await Assert.That(results).HasSingleItem();
                    _ = await Assert.That(results[0].CommandType).IsEqualTo("Test.CommandA");
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task QueryAsync_Filters_by_UserId(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IAuditStore>();
                    var management = services.GetRequiredService<IAuditManagement>();

                    await store.RecordAsync(CreateRecord(userId: "alice"), token).ConfigureAwait(false);
                    await store.RecordAsync(CreateRecord(userId: "bob"), token).ConfigureAwait(false);

                    var results = await management
                        .QueryAsync(new AuditFilter { UserId = "bob" }, token)
                        .ConfigureAwait(false);

                    _ = await Assert.That(results).HasSingleItem();
                    _ = await Assert.That(results[0].UserId).IsEqualTo("bob");
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task QueryAsync_Filters_by_Result(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IAuditStore>();
                    var management = services.GetRequiredService<IAuditManagement>();

                    await store.RecordAsync(CreateRecord(result: AuditResult.Success), token).ConfigureAwait(false);
                    await store
                        .RecordAsync(CreateRecord(result: AuditResult.Failure, exceptionMessage: "boom"), token)
                        .ConfigureAwait(false);

                    var results = await management
                        .QueryAsync(new AuditFilter { Result = AuditResult.Failure }, token)
                        .ConfigureAwait(false);

                    _ = await Assert.That(results).HasSingleItem();
                    _ = await Assert.That(results[0].Result).IsEqualTo(AuditResult.Failure);
                    _ = await Assert.That(results[0].ExceptionMessage).IsEqualTo("boom");
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task QueryAsync_Filters_by_From_and_To(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IAuditStore>();
                    var management = services.GetRequiredService<IAuditManagement>();

                    var old = DateTimeOffset.UtcNow.AddDays(-10);
                    var recent = DateTimeOffset.UtcNow;

                    await store
                        .RecordAsync(CreateRecord(commandType: "Test.Old", occurredAt: old), token)
                        .ConfigureAwait(false);
                    await store
                        .RecordAsync(CreateRecord(commandType: "Test.Recent", occurredAt: recent), token)
                        .ConfigureAwait(false);

                    var results = await management
                        .QueryAsync(new AuditFilter { From = DateTimeOffset.UtcNow.AddDays(-1) }, token)
                        .ConfigureAwait(false);

                    _ = await Assert.That(results).HasSingleItem();
                    _ = await Assert.That(results[0].CommandType).IsEqualTo("Test.Recent");
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task QueryAsync_Respects_Take_and_orders_by_OccurredAt_descending(
        CancellationToken cancellationToken
    ) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IAuditStore>();
                    var management = services.GetRequiredService<IAuditManagement>();

                    var baseline = DateTimeOffset.UtcNow.AddMinutes(-10);
                    for (var i = 0; i < 3; i++)
                    {
                        await store
                            .RecordAsync(
                                CreateRecord(commandType: $"Test.Command{i}", occurredAt: baseline.AddMinutes(i)),
                                token
                            )
                            .ConfigureAwait(false);
                    }

                    var results = await management
                        .QueryAsync(new AuditFilter { Take = 2 }, token)
                        .ConfigureAwait(false);

                    _ = await Assert.That(results.Count).IsEqualTo(2);
                    // Most recent (Command2) first, then Command1.
                    _ = await Assert.That(results[0].CommandType).IsEqualTo("Test.Command2");
                    _ = await Assert.That(results[1].CommandType).IsEqualTo("Test.Command1");
                },
                cancellationToken
            )
            .ConfigureAwait(false);

    [Test]
    public async Task GetStatisticsAsync_Returns_correct_counts(CancellationToken cancellationToken) =>
        await RunAndVerify(
                async (services, token) =>
                {
                    var store = services.GetRequiredService<IAuditStore>();
                    var management = services.GetRequiredService<IAuditManagement>();

                    await store.RecordAsync(CreateRecord(result: AuditResult.Success), token).ConfigureAwait(false);
                    await store.RecordAsync(CreateRecord(result: AuditResult.Success), token).ConfigureAwait(false);
                    await store.RecordAsync(CreateRecord(result: AuditResult.Failure), token).ConfigureAwait(false);

                    var stats = await management.GetStatisticsAsync(token).ConfigureAwait(false);

                    using (Assert.Multiple())
                    {
                        _ = await Assert.That(stats.SuccessCount).IsEqualTo(2);
                        _ = await Assert.That(stats.FailureCount).IsEqualTo(1);
                        _ = await Assert.That(stats.TotalCount).IsEqualTo(3);
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);
}
