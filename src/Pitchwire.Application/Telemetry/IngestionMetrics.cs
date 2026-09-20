using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Pitchwire.Application.Telemetry;

/// <summary>
/// What the ingestion boundary reports about itself.
/// </summary>
/// <remarks>
/// These are the numbers that answer the questions asked at two in the morning: is the provider
/// redelivering more than usual, is it sending events for fixtures nobody has, and how many matches
/// are currently showing a score known to be incomplete. A duplicate count that climbs is normal. A
/// rejection count that climbs means the two sides disagree about what a fixture is.
/// </remarks>
public sealed class IngestionMetrics : IDisposable
{
    public const string MeterName = "Pitchwire.Ingestion";
    public const string ActivitySourceName = "Pitchwire.Ingestion";

    private readonly Meter _meter;
    private readonly Counter<long> _accepted;
    private readonly Counter<long> _duplicate;
    private readonly Counter<long> _rejected;
    private readonly Counter<long> _repaired;
    private readonly Histogram<double> _deliveryLag;

    public IngestionMetrics(IMeterFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _meter = factory.Create(MeterName);
        _accepted = _meter.CreateCounter<long>("pitchwire.ingest.accepted", "events");
        _duplicate = _meter.CreateCounter<long>("pitchwire.ingest.duplicate", "events");
        _rejected = _meter.CreateCounter<long>("pitchwire.ingest.rejected", "events");
        _repaired = _meter.CreateCounter<long>("pitchwire.gap.repaired", "events");

        // How long an event took to reach this service after the provider says it happened. This is
        // the first half of the goal to screen number the README will have to publish.
        _deliveryLag = _meter.CreateHistogram<double>("pitchwire.ingest.delivery_lag", "ms");
    }

    public static ActivitySource Activity { get; } = new(ActivitySourceName);

    public void Accepted(int count) => Add(_accepted, count);

    public void Duplicate(int count) => Add(_duplicate, count);

    public void Rejected(int count) => Add(_rejected, count);

    public void Repaired(int count) => Add(_repaired, count);

    public void RecordDeliveryLag(TimeSpan lag)
    {
        // A provider clock that runs ahead produces a negative lag, which would drag the average down
        // and hide a real delay. It is not recorded rather than recorded as zero, because zero would
        // claim an instant delivery that did not happen.
        if (lag > TimeSpan.Zero)
        {
            _deliveryLag.Record(lag.TotalMilliseconds);
        }
    }

    public void Dispose() => _meter.Dispose();

    private static void Add(Counter<long> counter, int count)
    {
        if (count > 0)
        {
            counter.Add(count);
        }
    }
}
