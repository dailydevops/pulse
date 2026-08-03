namespace NetEvolve.Pulse.Tests.Unit.MySql;

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

[TestGroup("MySql")]
public sealed class MySqlCommandDeadLetterTests
{
    private const string ValidConnectionString = "Server=localhost;Database=Test;Uid=root;Pwd=secret;";

    [Test]
    public async Task Store_Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new MySqlCommandDeadLetterStore(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new MySqlCommandDeadLetterStore(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = null })
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new MySqlCommandDeadLetterStore(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = string.Empty })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new MySqlCommandDeadLetterStore(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = "   " })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithValidConnectionString_CreatesInstance()
    {
        var store = new MySqlCommandDeadLetterStore(
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

        var store = new MySqlCommandDeadLetterStore(Options.Create(options));

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Management_Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        var mediator = Mock.Of<IMediatorSendOnly>();
        var serializer = Mock.Of<IPayloadSerializer>();

        _ = await Assert
            .That(() => new MySqlCommandDeadLetterManagement(null!, mediator.Object, serializer.Object))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Management_Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        var mediator = Mock.Of<IMediatorSendOnly>();
        var serializer = Mock.Of<IPayloadSerializer>();

        _ = await Assert
            .That(() =>
                new MySqlCommandDeadLetterManagement(
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
                new MySqlCommandDeadLetterManagement(
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
                new MySqlCommandDeadLetterManagement(
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
                new MySqlCommandDeadLetterManagement(
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
                new MySqlCommandDeadLetterManagement(
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

        var management = new MySqlCommandDeadLetterManagement(
            Options.Create(new CommandDeadLetterOptions { ConnectionString = ValidConnectionString }),
            mediator.Object,
            serializer.Object
        );

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task AddMySqlCommandDeadLetterStore_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                MySqlCommandDeadLetterMediatorBuilderExtensions.AddMySqlCommandDeadLetterStore(
                    null!,
                    opts => opts.ConnectionString = ValidConnectionString
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddMySqlCommandDeadLetterStore_WithNullConfigureOptions_ThrowsArgumentNullException()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        _ = await Assert.That(() => mock.Object.AddMySqlCommandDeadLetterStore(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddMySqlCommandDeadLetterStore_WithValidOptions_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddMySqlCommandDeadLetterStore(opts => opts.ConnectionString = ValidConnectionString);

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddMySqlCommandDeadLetterStore_RegistersCommandDeadLetterStoreAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlCommandDeadLetterStore(opts => opts.ConnectionString = ValidConnectionString)
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICommandDeadLetterStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(MySqlCommandDeadLetterStore));
        }
    }

    [Test]
    public async Task AddMySqlCommandDeadLetterStore_RegistersCommandDeadLetterManagementAsScoped()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlCommandDeadLetterStore(opts => opts.ConnectionString = ValidConnectionString)
        );

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICommandDeadLetterManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(MySqlCommandDeadLetterManagement));
        }
    }

    [Test]
    public async Task AddMySqlCommandDeadLetterStore_WithConfigureOptions_AppliesOptions()
    {
        var services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.AddMySqlCommandDeadLetterStore(opts =>
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
