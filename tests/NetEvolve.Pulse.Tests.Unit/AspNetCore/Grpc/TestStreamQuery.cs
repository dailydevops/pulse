namespace NetEvolve.Pulse.Tests.Unit.AspNetCore.Grpc;

using NetEvolve.Pulse.Extensibility;

internal sealed class TestStreamQuery : IStreamQuery<string>
{
    public string? CausationId { get; set; }

    public string? CorrelationId { get; set; }
}
