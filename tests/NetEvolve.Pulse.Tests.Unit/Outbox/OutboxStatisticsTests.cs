namespace NetEvolve.Pulse.Tests.Unit.Outbox;

using NetEvolve.Pulse.Extensibility;
using TUnit.Core;

public sealed class OutboxStatisticsTests
{
    [Test]
    public async Task Total_WithDefaultValues_ReturnsZero()
    {
        var statistics = new OutboxStatistics();

        _ = await Assert.That(statistics.Total).IsEqualTo(0L);
    }

    [Test]
    public async Task Total_WithOnlyPending_ReturnsPendingCount()
    {
        var statistics = new OutboxStatistics { Pending = 5 };

        _ = await Assert.That(statistics.Total).IsEqualTo(5L);
    }

    [Test]
    public async Task Total_WithAllStatusesSet_ReturnsSumOfAllStatuses()
    {
        var statistics = new OutboxStatistics
        {
            Pending = 1,
            Processing = 2,
            Completed = 3,
            Failed = 4,
            DeadLetter = 5,
        };

        _ = await Assert.That(statistics.Total).IsEqualTo(15L);
    }

    [Test]
    public async Task Total_WithOnlyDeadLetter_ReturnsDeadLetterCount()
    {
        var statistics = new OutboxStatistics { DeadLetter = 7 };

        _ = await Assert.That(statistics.Total).IsEqualTo(7L);
    }

    [Test]
    public async Task AllProperties_WhenSet_TotalReflectsCorrectSum()
    {
        var statistics = new OutboxStatistics
        {
            Pending = 10,
            Processing = 20,
            Completed = 30,
            Failed = 40,
            DeadLetter = 50,
        };

        using (Assert.Multiple())
        {
            _ = await Assert.That(statistics.Pending).IsEqualTo(10L);
            _ = await Assert.That(statistics.Processing).IsEqualTo(20L);
            _ = await Assert.That(statistics.Completed).IsEqualTo(30L);
            _ = await Assert.That(statistics.Failed).IsEqualTo(40L);
            _ = await Assert.That(statistics.DeadLetter).IsEqualTo(50L);
            _ = await Assert.That(statistics.Total).IsEqualTo(150L);
        }
    }
}
