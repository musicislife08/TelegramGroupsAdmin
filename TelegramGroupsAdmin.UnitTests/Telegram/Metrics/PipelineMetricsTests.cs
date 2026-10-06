using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Metrics;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Metrics;

[TestFixture]
public class PipelineMetricsTests
{
    [TestCase(NameVerdict.Promotional, "promotional")]
    [TestCase(NameVerdict.Explicit, "explicit")]
    public void RecordMaskedUsername_MaskingVerdict_TagsFixedValue(NameVerdict verdict, string expected)
    {
        var metrics = new PipelineMetrics();
        var measurements = new ConcurrentQueue<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (ReferenceEquals(instrument.Meter, metrics.Meter)
                && instrument.Name == "tga.pipeline.ban_celebration.masked_username_total")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => measurements.Enqueue(tags.ToArray()));
        listener.Start();

        metrics.RecordMaskedUsername("auto_ban", verdict);

        Assert.That(measurements, Has.Count.EqualTo(1));
        Assert.That(measurements.Single(), Is.EquivalentTo(new[]
        {
            new KeyValuePair<string, object?>("trigger", "auto_ban"),
            new KeyValuePair<string, object?>("verdict", expected)
        }));
    }

    // Only a flagged name is ever masked; any other verdict is a caller bug.
    [TestCase(NameVerdict.Unscanned)]
    [TestCase(NameVerdict.Clean)]
    [TestCase((NameVerdict)99)]
    public void RecordMaskedUsername_NonMaskingVerdict_Throws(NameVerdict verdict)
    {
        var metrics = new PipelineMetrics();

        Assert.Throws<InvalidOperationException>(() => metrics.RecordMaskedUsername("auto_ban", verdict));
    }
}
