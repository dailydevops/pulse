namespace NetEvolve.Pulse.Tests.Unit.Interceptors;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;

/// <summary>
/// Collects measurements of the <c>NetEvolve.Pulse</c> meter that carry a specific tag value, so tests
/// running in parallel with other Pulse telemetry only see their own measurements. Without a tag filter,
/// all measurements are collected.
/// </summary>
internal sealed class PulseMeasurementCollector : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<Measurement> _measurements = new();
    private readonly string? _tagKey;
    private readonly string? _tagValue;

    public PulseMeasurementCollector(string? tagKey = null, string? tagValue = null)
    {
        _tagKey = tagKey;
        _tagValue = tagValue;

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, "NetEvolve.Pulse", StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
        _listener.Start();
    }

    public IReadOnlyList<Measurement> For(string instrumentName) =>
        [.. _measurements.Where(m => string.Equals(m.Instrument, instrumentName, StringComparison.Ordinal))];

    public void Dispose() => _listener.Dispose();

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var tagMap = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            tagMap[tag.Key] = tag.Value;
        }

        if (
            _tagKey is null
            || (
                tagMap.TryGetValue(_tagKey, out var actual)
                && string.Equals(actual as string, _tagValue, StringComparison.Ordinal)
            )
        )
        {
            _measurements.Enqueue(new Measurement(instrument.Name, instrument.Unit, value, tagMap));
        }
    }

    internal sealed record Measurement(
        string Instrument,
        string? Unit,
        double Value,
        IReadOnlyDictionary<string, object?> Tags
    );
}
