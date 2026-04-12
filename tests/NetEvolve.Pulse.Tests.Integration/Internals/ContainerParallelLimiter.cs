namespace NetEvolve.Pulse.Tests.Integration.Outbox;

using TUnit.Core.Interfaces;

internal class ContainerParallelLimiter : IParallelLimit
{
    public int Limit => (Math.Max(1, (Environment.ProcessorCount / 3) - 1));
}
