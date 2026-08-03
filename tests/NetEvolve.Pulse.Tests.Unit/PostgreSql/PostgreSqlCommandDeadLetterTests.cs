namespace NetEvolve.Pulse.Tests.Unit.PostgreSql;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.DeadLetter;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using TUnit.Core;
using TUnit.Mocks;

[TestGroup("PostgreSql")]
public sealed class PostgreSqlCommandDeadLetterTests
{
    private const string ValidConnectionString = "Host=localhost;Database=Test;Username=postgres;Password=secret;";

    [Test]
    public async Task Store_Constructor_WithNullOptions_ThrowsArgumentNullException() =>
        _ = await Assert.That(() => new PostgreSqlCommandDeadLetterStore(null!)).Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithNullConnectionString_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlCommandDeadLetterStore(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = null })
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task Store_Constructor_WithEmptyConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlCommandDeadLetterStore(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = string.Empty })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithWhitespaceConnectionString_ThrowsArgumentException() =>
        _ = await Assert
            .That(() =>
                new PostgreSqlCommandDeadLetterStore(
                    Options.Create(new CommandDeadLetterOptions { ConnectionString = "   " })
                )
            )
            .Throws<ArgumentException>();

    [Test]
    public async Task Store_Constructor_WithValidConnectionString_CreatesInstance()
    {
        var options = new CommandDeadLetterOptions { ConnectionString = ValidConnectionString };

        var store = new PostgreSqlCommandDeadLetterStore(Options.Create(options));

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Store_Constructor_WithCustomSchemaAndTableName_CreatesInstance()
    {
        var options = new CommandDeadLetterOptions
        {
            ConnectionString = ValidConnectionString,
            Schema = "custom",
            TableName = "CustomDeadLetter",
        };

        var store = new PostgreSqlCommandDeadLetterStore(Options.Create(options));

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Store_Constructor_WithNullSchema_CreatesInstance()
    {
        var options = new CommandDeadLetterOptions { ConnectionString = ValidConnectionString, Schema = null };

        var store = new PostgreSqlCommandDeadLetterStore(Options.Create(options));

        _ = await Assert.That(store).IsNotNull();
    }

    [Test]
    public async Task Management_Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        var mediator = Mock.Of<IMediatorSendOnly>();
        var serializer = Mock.Of<IPayloadSerializer>();

        _ = await Assert
            .That(() => new PostgreSqlCommandDeadLetterManagement(null!, mediator.Object, serializer.Object))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Management_Constructor_WithNullMediator_ThrowsArgumentNullException()
    {
        var options = Options.Create(new CommandDeadLetterOptions { ConnectionString = ValidConnectionString });
        var serializer = Mock.Of<IPayloadSerializer>();

        _ = await Assert
            .That(() => new PostgreSqlCommandDeadLetterManagement(options, null!, serializer.Object))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Management_Constructor_WithNullPayloadSerializer_ThrowsArgumentNullException()
    {
        var options = Options.Create(new CommandDeadLetterOptions { ConnectionString = ValidConnectionString });
        var mediator = Mock.Of<IMediatorSendOnly>();

        _ = await Assert
            .That(() => new PostgreSqlCommandDeadLetterManagement(options, mediator.Object, null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Management_Constructor_WithNullConnectionString_ThrowsArgumentNullException()
    {
        var options = Options.Create(new CommandDeadLetterOptions { ConnectionString = null });
        var mediator = Mock.Of<IMediatorSendOnly>();
        var serializer = Mock.Of<IPayloadSerializer>();

        _ = await Assert
            .That(() => new PostgreSqlCommandDeadLetterManagement(options, mediator.Object, serializer.Object))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Management_Constructor_WithEmptyConnectionString_ThrowsArgumentException()
    {
        var options = Options.Create(new CommandDeadLetterOptions { ConnectionString = string.Empty });
        var mediator = Mock.Of<IMediatorSendOnly>();
        var serializer = Mock.Of<IPayloadSerializer>();

        _ = await Assert
            .That(() => new PostgreSqlCommandDeadLetterManagement(options, mediator.Object, serializer.Object))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Management_Constructor_WithWhitespaceConnectionString_ThrowsArgumentException()
    {
        var options = Options.Create(new CommandDeadLetterOptions { ConnectionString = "   " });
        var mediator = Mock.Of<IMediatorSendOnly>();
        var serializer = Mock.Of<IPayloadSerializer>();

        _ = await Assert
            .That(() => new PostgreSqlCommandDeadLetterManagement(options, mediator.Object, serializer.Object))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Management_Constructor_WithValidArguments_CreatesInstance()
    {
        var options = Options.Create(new CommandDeadLetterOptions { ConnectionString = ValidConnectionString });
        var mediator = Mock.Of<IMediatorSendOnly>();
        var serializer = Mock.Of<IPayloadSerializer>();

        var management = new PostgreSqlCommandDeadLetterManagement(options, mediator.Object, serializer.Object);

        _ = await Assert.That(management).IsNotNull();
    }

    [Test]
    public async Task AddPostgreSqlCommandDeadLetterStore_WithNullConfigurator_ThrowsArgumentNullException() =>
        _ = await Assert
            .That(() =>
                PostgreSqlCommandDeadLetterMediatorBuilderExtensions.AddPostgreSqlCommandDeadLetterStore(
                    null!,
                    opts => opts.ConnectionString = ValidConnectionString
                )
            )
            .Throws<ArgumentNullException>();

    [Test]
    public async Task AddPostgreSqlCommandDeadLetterStore_WithNullConfigureOptions_ThrowsArgumentNullException()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        _ = await Assert
            .That(() => mock.Object.AddPostgreSqlCommandDeadLetterStore(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddPostgreSqlCommandDeadLetterStore_WithValidOptions_ReturnsConfiguratorForChaining()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        _ = mock.Services.Returns(new ServiceCollection());

        var result = mock.Object.AddPostgreSqlCommandDeadLetterStore(opts =>
            opts.ConnectionString = ValidConnectionString
        );

        _ = await Assert.That(result).IsSameReferenceAs(mock.Object);
    }

    [Test]
    public async Task AddPostgreSqlCommandDeadLetterStore_RegistersCommandDeadLetterStoreAsScoped()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        var services = new ServiceCollection();
        _ = mock.Services.Returns(services);

        _ = mock.Object.AddPostgreSqlCommandDeadLetterStore(opts => opts.ConnectionString = ValidConnectionString);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICommandDeadLetterStore));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(PostgreSqlCommandDeadLetterStore));
        }
    }

    [Test]
    public async Task AddPostgreSqlCommandDeadLetterStore_RegistersCommandDeadLetterManagementAsScoped()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        var services = new ServiceCollection();
        _ = mock.Services.Returns(services);

        _ = mock.Object.AddPostgreSqlCommandDeadLetterStore(opts => opts.ConnectionString = ValidConnectionString);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICommandDeadLetterManagement));

        using (Assert.Multiple())
        {
            _ = await Assert.That(descriptor).IsNotNull();
            _ = await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            _ = await Assert
                .That(descriptor.ImplementationType)
                .IsEqualTo(typeof(PostgreSqlCommandDeadLetterManagement));
        }
    }

    [Test]
    public async Task AddPostgreSqlCommandDeadLetterStore_AppliesOptions()
    {
        var mock = Mock.Of<IMediatorBuilder>();
        var services = new ServiceCollection();
        _ = mock.Services.Returns(services);

        _ = mock.Object.AddPostgreSqlCommandDeadLetterStore(opts =>
        {
            opts.ConnectionString = ValidConnectionString;
            opts.Schema = "custom";
            opts.TableName = "CustomDeadLetter";
        });

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options = provider.GetRequiredService<IOptions<CommandDeadLetterOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.ConnectionString).IsEqualTo(ValidConnectionString);
                _ = await Assert.That(options.Value.Schema).IsEqualTo("custom");
                _ = await Assert.That(options.Value.TableName).IsEqualTo("CustomDeadLetter");
            }
        }
    }
}
