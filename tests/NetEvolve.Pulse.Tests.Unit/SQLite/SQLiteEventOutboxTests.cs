namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Core;

[TestGroup("SQLite")]
public sealed class SQLiteEventOutboxTests
{
    [Test]
    public async Task Constructor_WithNullConnection_ThrowsArgumentNullException(CancellationToken cancellationToken) =>
        _ = await Assert
            .That(() => new SQLiteEventOutbox(null!, Options.Create(new OutboxOptions()), TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Constructor_WithNullOptions_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");

        _ = await Assert
            .That(() => new SQLiteEventOutbox(connection, null!, TimeProvider.System))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithNullTimeProvider_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");

        _ = await Assert
            .That(() => new SQLiteEventOutbox(connection, Options.Create(new OutboxOptions()), null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_WithValidArguments_CreatesInstance(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");

        var outbox = new SQLiteEventOutbox(connection, Options.Create(new OutboxOptions()), TimeProvider.System);

        _ = await Assert.That(outbox).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithTransaction_CreatesInstance(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");

        var outbox = new SQLiteEventOutbox(
            connection,
            Options.Create(new OutboxOptions()),
            TimeProvider.System,
            transaction: null
        );

        _ = await Assert.That(outbox).IsNotNull();
    }

    [Test]
    public async Task StoreAsync_WithNullMessage_ThrowsArgumentNullException(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var outbox = new SQLiteEventOutbox(connection, Options.Create(new OutboxOptions()), TimeProvider.System);

        _ = await Assert
            .That(async () => await outbox.StoreAsync<TestEvent>(null!).ConfigureAwait(false))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StoreAsync_WithLongCorrelationId_ThrowsInvalidOperationException(
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var outbox = new SQLiteEventOutbox(connection, Options.Create(new OutboxOptions()), TimeProvider.System);
        var message = new TestEvent
        {
            CorrelationId = new string('x', OutboxMessageSchema.MaxLengths.CorrelationId + 1),
        };

        _ = await Assert
            .That(async () => await outbox.StoreAsync(message, cancellationToken).ConfigureAwait(false))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StoreAsync_WithValidEvent_PersistsRow(CancellationToken cancellationToken)
    {
        var connectionString = $"Data Source=store_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (
            var create = new SqliteCommand(
                """
                CREATE TABLE "OutboxMessage"(
                    "Id" TEXT NOT NULL,
                    "EventType" TEXT NOT NULL,
                    "Payload" TEXT NOT NULL,
                    "CorrelationId" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL,
                    "ProcessedAt" TEXT NULL,
                    "NextRetryAt" TEXT NULL,
                    "RetryCount" INTEGER NOT NULL DEFAULT 0,
                    "Error" TEXT NULL,
                    "Status" INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT "PK_OutboxMessage" PRIMARY KEY ("Id")
                );
                """,
                keepAlive
            )
        )
        {
            _ = await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var outbox = new SQLiteEventOutbox(
            connection,
            Options.Create(new OutboxOptions { ConnectionString = connectionString }),
            TimeProvider.System
        );

        var evt = new TestEvent { CorrelationId = "corr" };

        await outbox.StoreAsync(evt, cancellationToken).ConfigureAwait(false);

        await using var cmd = new SqliteCommand(
            "SELECT \"EventType\",\"CorrelationId\",\"Status\",\"Payload\" FROM \"OutboxMessage\" WHERE \"Id\" = @Id",
            keepAlive
        );
        _ = cmd.Parameters.AddWithValue("@Id", evt.Id);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        _ = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        using (Assert.Multiple())
        {
            _ = await Assert.That(reader.GetString(0)).IsEqualTo(evt.GetType().AssemblyQualifiedName);
            _ = await Assert.That(reader.GetString(1)).IsEqualTo("corr");
            _ = await Assert.That(reader.GetInt64(2)).IsEqualTo((long)OutboxMessageStatus.Pending);
            _ = await Assert.That(reader.GetString(3)).IsNotNullOrWhiteSpace();
        }
    }

    [Test]
    public async Task StoreAsync_WithOversizedEventType_ThrowsInvalidOperationException(
        CancellationToken cancellationToken
    )
    {
        var connectionString = $"Data Source=type_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (
            var create = new SqliteCommand(
                """
                CREATE TABLE "OutboxMessage"(
                    "Id" TEXT NOT NULL,
                    "EventType" TEXT NOT NULL,
                    "Payload" TEXT NOT NULL,
                    "CorrelationId" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL,
                    "ProcessedAt" TEXT NULL,
                    "NextRetryAt" TEXT NULL,
                    "RetryCount" INTEGER NOT NULL DEFAULT 0,
                    "Error" TEXT NULL,
                    "Status" INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT "PK_OutboxMessage" PRIMARY KEY ("Id")
                );
                """,
                keepAlive
            )
        )
        {
            _ = await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var outbox = new SQLiteEventOutbox(
            connection,
            Options.Create(new OutboxOptions { ConnectionString = connectionString }),
            TimeProvider.System
        );

        var longEvent = CreateLongTypeEvent();

        _ = await Assert
            .That(async () => await outbox.StoreAsync(longEvent, cancellationToken).ConfigureAwait(false))
            .Throws<InvalidOperationException>();
    }

    private sealed record TestEvent : IEvent
    {
        public string? CorrelationId { get; set; }
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public DateTimeOffset? PublishedAt { get; set; }
    }

    private static IEvent CreateLongTypeEvent()
    {
        var assemblyName = new AssemblyName($"DynEvt_{Guid.NewGuid():N}");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("dyn");
        var typeName = new string('E', OutboxMessageSchema.MaxLengths.EventType + 10);
        var typeBuilder = moduleBuilder.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
            typeof(object),
            [typeof(IEvent)]
        );

        var idField = typeBuilder.DefineField("_id", typeof(string), FieldAttributes.Private);
        var corrField = typeBuilder.DefineField("_corr", typeof(string), FieldAttributes.Private);
        var publishedField = typeBuilder.DefineField("_pub", typeof(DateTimeOffset?), FieldAttributes.Private);

        DefineAutoProperty(typeBuilder, idField, nameof(IEvent.Id), typeof(string));
        DefineAutoProperty(typeBuilder, corrField, nameof(IEvent.CorrelationId), typeof(string));
        DefineAutoProperty(typeBuilder, publishedField, nameof(IEvent.PublishedAt), typeof(DateTimeOffset?));

        var ctor = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, Guid.NewGuid().ToString());
        il.Emit(OpCodes.Stfld, idField);
        il.Emit(OpCodes.Ret);

        var eventType = typeBuilder.CreateType()!;
        return (IEvent)Activator.CreateInstance(eventType)!;
    }

    private static void DefineAutoProperty(
        TypeBuilder typeBuilder,
        FieldBuilder backingField,
        string propertyName,
        Type propertyType
    )
    {
        var propertyBuilder = typeBuilder.DefineProperty(
            propertyName,
            PropertyAttributes.None,
            propertyType,
            Type.EmptyTypes
        );

        var getMethod = typeBuilder.DefineMethod(
            $"get_{propertyName}",
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            propertyType,
            Type.EmptyTypes
        );
        var getIl = getMethod.GetILGenerator();
        getIl.Emit(OpCodes.Ldarg_0);
        getIl.Emit(OpCodes.Ldfld, backingField);
        getIl.Emit(OpCodes.Ret);
        propertyBuilder.SetGetMethod(getMethod);

        var setMethod = typeBuilder.DefineMethod(
            $"set_{propertyName}",
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            null,
            [propertyType]
        );
        var setIl = setMethod.GetILGenerator();
        setIl.Emit(OpCodes.Ldarg_0);
        setIl.Emit(OpCodes.Ldarg_1);
        setIl.Emit(OpCodes.Stfld, backingField);
        setIl.Emit(OpCodes.Ret);
        propertyBuilder.SetSetMethod(setMethod);
    }
}
