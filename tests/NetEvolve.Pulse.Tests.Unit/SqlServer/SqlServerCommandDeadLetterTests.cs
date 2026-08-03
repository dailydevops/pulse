namespace NetEvolve.Pulse.Tests.Unit.SqlServer;

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("SqlServer")]
public sealed class SqlServerCommandDeadLetterTests
{
    private const string ValidConnectionString = "Server=.;Database=Test;Integrated Security=true;";

    [Test]
    public async Task Store_Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new SqlServerCommandDeadLetterStore(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new SqlServerCommandDeadLetterStore(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = null })
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerCommandDeadLetterStore(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = string.Empty })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerCommandDeadLetterStore(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = "   " })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithValidConnectionString_CreatesInstance()
    {
        var store = new SqlServerCommandDeadLetterStore(
            Options.Create(new CommandDeadLetterOptions { ConnectionString = ValidConnectionString })
        );

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

        var store = new SqlServerCommandDeadLetterStore(Options.Create(options));

        _ = await Assert.That(store).IsNotNull();
    }

    // Defense-in-depth: pin that an attacker-controlled Schema value cannot reach the SQL
    // builder. The constructor must fail fast when Schema contains characters that would
    // break out of the [bracketed] identifier (e.g. ']' followed by injected SQL).
    [Test]
    public async Task Store_Constructor_WithMaliciousSchema_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new SqlServerCommandDeadLetterStore(
                    Options.Create(
                        new CommandDeadLetterOptions
                        {
                            ConnectionString = ValidConnectionString,
                            Schema = "pulse].[evil] -- ",
                        }
                    )
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Management_Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        var mediator = Mock.Of<IMediatorSendOnly>();
        var serializer = Mock.Of<IPayloadSerializer>();

        _ = await Assert
            .That(() => new SqlServerCommandDeadLetterManagement(null!, mediator.Object, serializer.Object))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Management_Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        var mediator = Mock.Of<IMediatorSendOnly>();
        var serializer = Mock.Of<IPayloadSerializer>();

        _ = await Assert
            .That(() =>
                new SqlServerCommandDeadLetterManagement(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = null }),
                    mediator.Object,
                    serializer.Object
                )
            )
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Management_Constructor_WithEmptyConnectionString_ThrowsArgumentException()
    {
        var mediator = Mock.Of<IMediatorSendOnly>();
        var serializer = Mock.Of<IPayloadSerializer>();

        _ = await Assert
            .That(() =>
                new SqlServerCommandDeadLetterManagement(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = string.Empty }),
                    mediator.Object,
                    serializer.Object
                )
            )
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Management_Constructor_WithWhitespaceConnectionString_ThrowsArgumentException()
    {
        var mediator = Mock.Of<IMediatorSendOnly>();
        var serializer = Mock.Of<IPayloadSerializer>();

        _ = await Assert
            .That(() =>
                new SqlServerCommandDeadLetterManagement(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = "   " }),
                    mediator.Object,
                    serializer.Object
                )
            )
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Management_Constructor_WithNullMediator_ThrowsArgumentNullException()
    {
        var serializer = Mock.Of<IPayloadSerializer>();

        _ = await Assert
            .That(() =>
                new SqlServerCommandDeadLetterManagement(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = ValidConnectionString }),
                    null!,
                    serializer.Object
                )
            )
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Management_Constructor_WithNullPayloadSerializer_ThrowsArgumentNullException()
    {
        var mediator = Mock.Of<IMediatorSendOnly>();

        _ = await Assert
            .That(() =>
                new SqlServerCommandDeadLetterManagement(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = ValidConnectionString }),
                    mediator.Object,
                    null!
                )
            )
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Management_Constructor_WithValidArguments_CreatesInstance()
    {
        var mediator = Mock.Of<IMediatorSendOnly>();
        var serializer = Mock.Of<IPayloadSerializer>();

        var management = new SqlServerCommandDeadLetterManagement(
            Options.Create(new CommandDeadLetterOptions { ConnectionString = ValidConnectionString }),
            mediator.Object,
            serializer.Object
        );

        _ = await Assert.That(management).IsNotNull();
    }

    // Defense-in-depth: pin that an attacker-controlled Schema value cannot reach the SQL
    // builder. The constructor must fail fast when Schema contains characters that would
    // break out of the [bracketed] identifier (e.g. ']' followed by injected SQL).
    [Test]
    public async Task Management_Constructor_WithMaliciousSchema_ThrowsArgumentException()
    {
        var mediator = Mock.Of<IMediatorSendOnly>();
        var serializer = Mock.Of<IPayloadSerializer>();

        _ = await Assert
            .That(() =>
                new SqlServerCommandDeadLetterManagement(
                    Options.Create(
                        new CommandDeadLetterOptions
                        {
                            ConnectionString = ValidConnectionString,
                            Schema = "pulse].[evil] -- ",
                        }
                    ),
                    mediator.Object,
                    serializer.Object
                )
            )
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AddSqlServerCommandDeadLetterStore_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                SqlServerCommandDeadLetterMediatorBuilderExtensions.AddSqlServerCommandDeadLetterStore(
                    null!,
                    opts => opts.ConnectionString = ValidConnectionString
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddSqlServerCommandDeadLetterStore_WithNullConfigureOptions_ThrowsArgumentNullException()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        _ = await Assert
            .That(() => mock.Object.AddSqlServerCommandDeadLetterStore(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddSqlServerCommandDeadLetterStore_WithValidOptions_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddSqlServerCommandDeadLetterStore(opts =>
            opts.ConnectionString = ValidConnectionString
        );

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddSqlServerCommandDeadLetterStore_RegistersCommandDeadLetterStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSqlServerCommandDeadLetterStore(opts => opts.ConnectionString = ValidConnectionString)
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICommandDeadLetterStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(SqlServerCommandDeadLetterStore));
        }
    }

    [Test]
    public async Task AddSqlServerCommandDeadLetterStore_RegistersCommandDeadLetterManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSqlServerCommandDeadLetterStore(opts => opts.ConnectionString = ValidConnectionString)
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICommandDeadLetterManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert
                .That(descriptor.ImplementationType)
                .IsEqualTo(typeof(SqlServerCommandDeadLetterManagement));
        }
    }

    [Test]
    public async Task AddSqlServerCommandDeadLetterStore_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddSqlServerCommandDeadLetterStore(opts =>
            {
                opts.ConnectionString = ValidConnectionString;
                opts.TableName = "CustomCommandDeadLetter";
            })
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<CommandDeadLetterOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.ConnectionString).IsEqualTo(ValidConnectionString);
                _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomCommandDeadLetter");
            }
        }
    }
}
