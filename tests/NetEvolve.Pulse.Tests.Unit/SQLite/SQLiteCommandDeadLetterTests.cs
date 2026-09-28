namespace NetEvolve.Pulse.Tests.Unit.SQLite;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("SQLite")]
public sealed class SQLiteCommandDeadLetterTests
{
    private const string ValidConnectionString = "Data Source=:memory:";

    [Test]
    public async Task Store_Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() => new SQLiteCommandDeadLetterStore(null!, TimeProvider.System))
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithNullTimeProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new SQLiteCommandDeadLetterStore(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = ValidConnectionString }),
                    null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new SQLiteCommandDeadLetterStore(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = null }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SQLiteCommandDeadLetterStore(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = string.Empty }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SQLiteCommandDeadLetterStore(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = "   " }),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithValidConnectionString_CreatesInstance()
    {
        var options = new CommandDeadLetterOptions { ConnectionString = ValidConnectionString };

        var store = new SQLiteCommandDeadLetterStore(Options.Create(options), TimeProvider.System);

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Store_Constructor_WithCustomTableName_CreatesInstance()
    {
        var options = new CommandDeadLetterOptions
        {
            ConnectionString = ValidConnectionString,
            TableName = "CustomCommandDeadLetter",
        };

        var store = new SQLiteCommandDeadLetterStore(Options.Create(options), TimeProvider.System);

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Store_Constructor_WithWalModeDisabled_CreatesInstance()
    {
        var options = new CommandDeadLetterOptions { ConnectionString = ValidConnectionString, EnableWalMode = false };

        var store = new SQLiteCommandDeadLetterStore(Options.Create(options), TimeProvider.System);

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Store_Constructor_WithInvalidTableName_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SQLiteCommandDeadLetterStore(
                    Options.Create(
                        new CommandDeadLetterOptions
                        {
                            ConnectionString = ValidConnectionString,
                            TableName = "1invalid",
                        }
                    ),
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Management_Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new SQLiteCommandDeadLetterManagement(
                    null!,
                    Mock.Of<IMediatorSendOnly>().Object,
                    Mock.Of<IPayloadSerializer>().Object,
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Management_Constructor_WithNullMediator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new SQLiteCommandDeadLetterManagement(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = ValidConnectionString }),
                    null!,
                    Mock.Of<IPayloadSerializer>().Object,
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Management_Constructor_WithNullPayloadSerializer_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new SQLiteCommandDeadLetterManagement(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = ValidConnectionString }),
                    Mock.Of<IMediatorSendOnly>().Object,
                    null!,
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Management_Constructor_WithNullTimeProvider_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new SQLiteCommandDeadLetterManagement(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = ValidConnectionString }),
                    Mock.Of<IMediatorSendOnly>().Object,
                    Mock.Of<IPayloadSerializer>().Object,
                    null!
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Management_Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new SQLiteCommandDeadLetterManagement(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = null }),
                    Mock.Of<IMediatorSendOnly>().Object,
                    Mock.Of<IPayloadSerializer>().Object,
                    TimeProvider.System
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Management_Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SQLiteCommandDeadLetterManagement(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = string.Empty }),
                    Mock.Of<IMediatorSendOnly>().Object,
                    Mock.Of<IPayloadSerializer>().Object,
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Management_Constructor_WithValidConnectionString_CreatesInstance()
    {
        var options = new CommandDeadLetterOptions { ConnectionString = ValidConnectionString };

        var management = new SQLiteCommandDeadLetterManagement(
            Options.Create(options),
            Mock.Of<IMediatorSendOnly>().Object,
            Mock.Of<IPayloadSerializer>().Object,
            TimeProvider.System
        );

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task Management_Constructor_WithInvalidTableName_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SQLiteCommandDeadLetterManagement(
                    Options.Create(
                        new CommandDeadLetterOptions
                        {
                            ConnectionString = ValidConnectionString,
                            TableName = "1invalid",
                        }
                    ),
                    Mock.Of<IMediatorSendOnly>().Object,
                    Mock.Of<IPayloadSerializer>().Object,
                    TimeProvider.System
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task AddSQLiteCommandDeadLetterStore_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SQLiteCommandDeadLetterMediatorBuilderExtensions.AddSQLiteCommandDeadLetterStore(
                    null!,
                    opts => opts.ConnectionString = ValidConnectionString
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSQLiteCommandDeadLetterStore_WithNullConfigureOptions_ThrowsArgumentNullException()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        _ = await Assert.That(() => mock.Object.AddSQLiteCommandDeadLetterStore(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddSQLiteCommandDeadLetterStore_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSQLiteCommandDeadLetterStore(opts => opts.ConnectionString = ValidConnectionString);

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSQLiteCommandDeadLetterStore_RegistersCommandDeadLetterStoreAsScoped()
    {
        var services = new ServiceCollection();
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(services);

        _ = mock.Object.AddSQLiteCommandDeadLetterStore(opts => opts.ConnectionString = ValidConnectionString);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICommandDeadLetterStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(SQLiteCommandDeadLetterStore));
        }
    }

    [Test]
    public async Task AddSQLiteCommandDeadLetterStore_RegistersCommandDeadLetterManagementAsScoped()
    {
        var services = new ServiceCollection();
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(services);

        _ = mock.Object.AddSQLiteCommandDeadLetterStore(opts => opts.ConnectionString = ValidConnectionString);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICommandDeadLetterManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(SQLiteCommandDeadLetterManagement));
        }
    }

    [Test]
    public async Task AddSQLiteCommandDeadLetterStore_AppliesOptions()
    {
        var services = new ServiceCollection();
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(services);

        _ = mock.Object.AddSQLiteCommandDeadLetterStore(opts =>
        {
            opts.ConnectionString = ValidConnectionString;
            opts.TableName = "CustomCommandDeadLetter";
            opts.EnableWalMode = false;
        });

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<CommandDeadLetterOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.ConnectionString).IsEqualTo(ValidConnectionString);
                _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomCommandDeadLetter");
                _ = await Assert.That(options.Value.EnableWalMode).IsFalse();
            }
        }
    }
}
