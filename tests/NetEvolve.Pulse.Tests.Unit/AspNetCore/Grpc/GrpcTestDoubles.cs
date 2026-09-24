namespace NetEvolve.Pulse.Tests.Unit.AspNetCore.Grpc;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using global::Grpc.Core;
using NetEvolve.Pulse.Extensibility;

internal sealed class TestStreamQuery : IStreamQuery<string>
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}

// Code-first binding: the same shape Grpc.Tools generates, without requiring a .proto file.
[BindServiceMethod(typeof(TestStreamService), nameof(BindService))]
// Not sealed: ASP.NET Core gRPC only binds RPC methods that are virtual and declared on the BindService type.
internal class TestStreamService(IMediator mediator) : PulseGrpcStreamService<TestStreamQuery, string>(mediator)
{
    public const string ServiceName = "pulse.test.StreamService";

    public static readonly Method<TestStreamQuery, string> StreamMethod = new(
        MethodType.ServerStreaming,
        ServiceName,
        nameof(Stream),
        Marshallers.Create(_ => [], _ => new TestStreamQuery()),
        Marshallers.Create(Encoding.UTF8.GetBytes, Encoding.UTF8.GetString)
    );

    [SuppressMessage(
        "Style",
        "IDE0060:Remove unused parameter",
        Justification = "Signature required by BindServiceMethodAttribute."
    )]
    public static void BindService(ServiceBinderBase binder, TestStreamService service) =>
        binder.AddMethod(StreamMethod, (ServerStreamingServerMethod<TestStreamQuery, string>)null!);

    public virtual Task Stream(
        TestStreamQuery request,
        IServerStreamWriter<string> responseStream,
        ServerCallContext context
    ) => StreamAsync(request, responseStream, context);
}

// Implements only WriteAsync(T); the token overload keeps its default implementation, like many custom writers.
internal sealed class CollectingStreamWriter(Action? onWrite = null) : IServerStreamWriter<string>
{
    public List<string> Items { get; } = [];

    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(string message)
    {
        Items.Add(message);
        onWrite?.Invoke();
        return Task.CompletedTask;
    }
}

// ServerCallContext only exposes protected abstract members, which TUnit.Mocks cannot configure.
internal sealed class TestServerCallContext(CancellationToken cancellationToken) : ServerCallContext
{
    protected override string MethodCore => TestStreamService.StreamMethod.FullName;

    protected override string HostCore => "localhost";

    protected override string PeerCore => "test";

    protected override DateTime DeadlineCore => DateTime.MaxValue;

    protected override Metadata RequestHeadersCore { get; } = [];

    protected override CancellationToken CancellationTokenCore => cancellationToken;

    protected override Metadata ResponseTrailersCore { get; } = [];

    protected override Status StatusCore { get; set; }

    protected override WriteOptions? WriteOptionsCore { get; set; }

    protected override AuthContext AuthContextCore { get; } = new(null, []);

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
        throw new NotSupportedException();

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}
