namespace NetEvolve.Pulse.Tests.Unit.AzureQueueStorage;

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Outbox;
using NetEvolve.Pulse.Outbox;
using TUnit.Assertions.Extensions;
using TUnit.Core;

[TestGroup("AzureQueueStorage")]
public sealed class AzureQueueStorageExtensionsTests
{
    private static readonly Uri FakeServiceUri = new("https://fakeaccount.queue.core.windows.net");
    private const string FakeConnectionString =
        "DefaultEndpointsProtocol=https;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;QueueEndpoint=https://127.0.0.1:10001/devstoreaccount1;";

    [Test]
    public void UseAzureQueueStorageTransport_ConnectionString_When_configurator_is_null_throws_ArgumentNullException()
    {
        IMediatorBuilder? configurator = null;

        _ = Assert.Throws<ArgumentNullException>(
            "configurator",
            () => configurator!.UseAzureQueueStorageTransport(FakeConnectionString)
        );
    }

    [Test]
    public void UseAzureQueueStorageTransport_ConnectionString_When_connectionString_is_null_throws_ArgumentException()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config =>
            _ = Assert.Throws<ArgumentException>(
                "connectionString",
                () => config.UseAzureQueueStorageTransport((string)null!)
            )
        );
    }

    [Test]
    public void UseAzureQueueStorageTransport_Uri_When_configurator_is_null_throws_ArgumentNullException()
    {
        IMediatorBuilder? configurator = null;

        _ = Assert.Throws<ArgumentNullException>(
            "configurator",
            () => configurator!.UseAzureQueueStorageTransport(FakeServiceUri)
        );
    }

    [Test]
    public void UseAzureQueueStorageTransport_Uri_When_uri_is_null_throws_ArgumentNullException()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config =>
            _ = Assert.Throws<ArgumentNullException>(
                "queueServiceUri",
                () => config.UseAzureQueueStorageTransport((Uri)null!)
            )
        );
    }

    [Test]
    public async Task UseAzureQueueStorageTransport_ConnectionString_registers_transport()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseAzureQueueStorageTransport(FakeConnectionString));

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(AzureQueueStorageMessageTransport));
    }

    [Test]
    public async Task UseAzureQueueStorageTransport_Uri_registers_transport()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseAzureQueueStorageTransport(FakeServiceUri));

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(AzureQueueStorageMessageTransport));
    }

    [Test]
    public async Task UseAzureQueueStorageTransport_ConnectionString_sets_connection_string_in_options()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseAzureQueueStorageTransport(FakeConnectionString));

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options =
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureQueueStorageTransportOptions>>();
            _ = await Assert.That(options.Value.ConnectionString).IsEqualTo(FakeConnectionString);
        }
    }

    [Test]
    public async Task UseAzureQueueStorageTransport_Uri_sets_service_uri_in_options()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseAzureQueueStorageTransport(FakeServiceUri));

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options =
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureQueueStorageTransportOptions>>();
            _ = await Assert.That(options.Value.QueueServiceUri).IsEqualTo(FakeServiceUri);
        }
    }

    [Test]
    public async Task UseAzureQueueStorageTransport_ConnectionString_default_options_have_correct_defaults()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseAzureQueueStorageTransport(FakeConnectionString));

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options =
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureQueueStorageTransportOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.QueueName).IsEqualTo("pulse-outbox");
                _ = await Assert.That(options.Value.CreateQueueIfNotExists).IsTrue();
                _ = await Assert.That(options.Value.MessageVisibilityTimeout).IsNull();
            }
        }
    }

    [Test]
    public async Task UseAzureQueueStorageTransport_ConnectionString_configureOptions_is_applied()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config =>
            config.UseAzureQueueStorageTransport(
                FakeConnectionString,
                options =>
                {
                    options.QueueName = "my-queue";
                    options.CreateQueueIfNotExists = false;
                }
            )
        );

        var provider = services.BuildServiceProvider();
        await using (provider.ConfigureAwait(false))
        {
            var options =
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureQueueStorageTransportOptions>>();

            using (Assert.Multiple())
            {
                _ = await Assert.That(options.Value.QueueName).IsEqualTo("my-queue");
                _ = await Assert.That(options.Value.CreateQueueIfNotExists).IsFalse();
            }
        }
    }

    [Test]
    public async Task UseAzureQueueStorageTransport_ConnectionString_replaces_existing_transport()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddSingleton<IMessageTransport>(new DummyTransport());
        _ = services.AddPulse(config => config.UseAzureQueueStorageTransport(FakeConnectionString));

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(AzureQueueStorageMessageTransport));
    }

    [Test]
    public async Task UseAzureQueueStorageTransport_ConnectionString_registers_transport_as_singleton()
    {
        IServiceCollection services = new ServiceCollection();
        _ = services.AddPulse(config => config.UseAzureQueueStorageTransport(FakeConnectionString));

        var descriptor = services.Single(d => d.ServiceType == typeof(IMessageTransport));
        _ = await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Singleton);
    }

    private sealed class DummyTransport : IMessageTransport
    {
        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
