namespace NetEvolve.Pulse.Tests.Unit.AspNetCore.Grpc;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using global::Grpc.Core;
using NetEvolve.Pulse.Extensibility;

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

    // Not an RPC: exposes the mapping overload to unit tests.
    public Task StreamMapped(
        TestStreamQuery request,
        IServerStreamWriter<string> responseStream,
        ServerCallContext context,
        Func<string, string> map
    ) => StreamAsync(request, responseStream, context, map);
}
