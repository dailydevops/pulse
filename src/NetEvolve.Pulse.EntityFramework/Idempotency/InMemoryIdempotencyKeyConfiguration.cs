namespace NetEvolve.Pulse.Idempotency;

using Microsoft.Extensions.Options;

/// <summary>
/// Entity Framework Core configuration for <see cref="IdempotencyKey"/> targeting the
/// <c>Microsoft.EntityFrameworkCore.InMemory</c> provider.
/// Intended for testing only.
/// </summary>
internal sealed class InMemoryIdempotencyKeyConfiguration : IdempotencyKeyConfigurationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryIdempotencyKeyConfiguration"/> class
    /// with default options.
    /// </summary>
    public InMemoryIdempotencyKeyConfiguration()
        : this(Options.Create(new IdempotencyKeyOptions())) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryIdempotencyKeyConfiguration"/> class.
    /// </summary>
    /// <param name="options">The idempotency key options containing schema and table configuration.</param>
    public InMemoryIdempotencyKeyConfiguration(IOptions<IdempotencyKeyOptions> options)
        : base(options) { }
}
