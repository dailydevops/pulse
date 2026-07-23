namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Interceptors;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// Constructor invariant tests for <see cref="EventFilterInterceptor{TEvent}"/>.
/// Pins the null filter-collection guard that protects against DI mis-configuration.
/// </summary>
[TestGroup("Interceptors")]
public sealed class EventFilterInterceptorConstructorTests
{
    [Test]
    public async Task Constructor_NullFilters_ThrowsArgumentNullException()
    {
        IEnumerable<IEventFilter<TestEvent>>? filters = null;

        _ = Assert.Throws<ArgumentNullException>("filters", () => _ = new EventFilterInterceptor<TestEvent>(filters!));

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private sealed class TestEvent : IEvent
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string? CausationId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
