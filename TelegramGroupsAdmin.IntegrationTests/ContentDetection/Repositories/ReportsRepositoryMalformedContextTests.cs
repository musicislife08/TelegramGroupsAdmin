using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Testing.Golden;

namespace TelegramGroupsAdmin.IntegrationTests.ContentDetection.Repositories;

/// <summary>
/// One report whose JSONB context no longer deserializes must not take the whole queue down:
/// <see cref="ReportsRepository"/> queue list queries skip that row with a warning naming the report
/// id (never the payload) and return every other row — while the per-user sibling-cleanup query keeps
/// throwing, so an Allow never strands a pending alert. The malformed row is the assertion subject and
/// is produced by <see cref="CorruptContextAsync"/> (a sanctioned raw UPDATE of one pinned canonical
/// row on this test's clone; see the TEST-DATA RULE EXCEPTION there) — the only write in these tests.
/// Integration level because the skip lives in the repository's per-row mapping loop; the mapping
/// extension itself is static and throws by design.
/// </summary>
[TestFixture]
public class ReportsRepositoryMalformedContextTests
{
    private MigrationTestHelper? _helper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IReportsRepository? _repository;
    private CapturingLoggerProvider? _logs;

    [SetUp]
    public async Task SetUp()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromGoldenTemplateAsync();
        _logs = new CapturingLoggerProvider();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(_helper.ConnectionString));
        services.AddLogging(builder => builder.AddProvider(_logs).SetMinimumLevel(LogLevel.Debug));
        services.AddScoped<IReportsRepository, ReportsRepository>();
        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _repository = _scope.ServiceProvider.GetRequiredService<IReportsRepository>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _logs?.Dispose();
        _helper?.Dispose();
    }

    [Test]
    public async Task GetProfileScanAlertsAsync_SkipsAnAlertWhoseContextDoesNotDeserialize()
    {
        const long malformedId = GoldenDatasetConstants.Reports.ResolvedProfileScanAlertId;
        var profileScanAlerts = await _helper!.ExecuteScalarAsync<long>("SELECT count(*) FROM reports WHERE type = 3");
        Assert.That(profileScanAlerts, Is.GreaterThan(1));

        // The subject: aiSignals is string[]; a string there is what a sanitizer artifact once produced.
        await CorruptContextAsync(malformedId, "aiSignals", "\"a, b\"");

        var results = await _repository!.GetProfileScanAlertsAsync(pendingOnly: false, CancellationToken.None);

        Assert.That(results, Has.Count.EqualTo(profileScanAlerts - 1));
        Assert.That(results.Select(r => r.Id), Does.Not.Contain(malformedId));
        AssertSkipWasLogged(malformedId, payloadFragment: "a, b");
    }

    [Test]
    public async Task GetExamResultsAsync_SkipsAnExamWhoseContextDoesNotDeserialize()
    {
        const long malformedId = GoldenDatasetConstants.Reports.ResolvedExamFailureId;
        var examResults = await _helper!.ExecuteScalarAsync<long>("SELECT count(*) FROM reports WHERE type = 2");
        Assert.That(examResults, Is.GreaterThan(1));

        // The subject: score is an int; a string there cannot be read.
        await CorruptContextAsync(malformedId, "score", "\"ninety\"");

        var results = await _repository!.GetExamResultsAsync(pendingOnly: false, cancellationToken: CancellationToken.None);

        Assert.That(results, Has.Count.EqualTo(examResults - 1));
        Assert.That(results.Select(r => r.Id), Does.Not.Contain(malformedId));
        AssertSkipWasLogged(malformedId, payloadFragment: "ninety");
    }

    [Test]
    public async Task GetPendingProfileScanAlertsForUserAsync_ReturnsTheUsersPendingAlertOnCleanData()
    {
        const long userId = GoldenDatasetConstants.Reports.PendingFixturesTelegramUserId;
        var pendingForUser = await _helper!.ExecuteScalarAsync<long>(
            $"SELECT count(*) FROM reports WHERE type = 3 AND status = 0 AND (context->>'userId')::bigint = {userId}");
        Assert.That(pendingForUser, Is.GreaterThan(0));

        var results = await _repository!.GetPendingProfileScanAlertsForUserAsync(userId, CancellationToken.None);

        Assert.That(results, Has.Count.EqualTo(pendingForUser));
        Assert.That(results.Select(r => r.Id), Does.Contain(GoldenDatasetConstants.Reports.PendingProfileScanAlertId));
    }

    /// <summary>
    /// Sibling cleanup after an Allow (<c>ProfileScanHandler.CleanupSiblingAlertsAsync</c>) must not
    /// silently skip: a skipped sibling stays Pending, invisible in the queue, while the raw JSONB
    /// <c>HasPendingProfileScanAlertAsync</c> keeps holding the user at the join gate.
    /// </summary>
    [Test]
    public async Task GetPendingProfileScanAlertsForUserAsync_ThrowsWhenAPendingAlertDoesNotDeserialize()
    {
        const long malformedId = GoldenDatasetConstants.Reports.PendingProfileScanAlertId;
        await CorruptContextAsync(malformedId, "aiSignals", "\"a, b\"");

        Assert.That(
            () => _repository!.GetPendingProfileScanAlertsForUserAsync(
                GoldenDatasetConstants.Reports.PendingFixturesTelegramUserId, CancellationToken.None),
            Throws.TypeOf<System.Text.Json.JsonException>());
    }

    /// <summary>
    /// Overwrites one key of a pinned canonical report's <c>context</c> with <paramref name="jsonValue"/>.
    /// </summary>
    private async Task CorruptContextAsync(long reportId, string key, string jsonValue)
    {
        // TEST-DATA RULE EXCEPTION: directly overwrite one canonical report's context via raw SQL
        // rather than via a GoldenMutatePlanBuilder verb. The golden dataset is scrubbed real prod
        // data and must never carry a deserialization-breaking context by construction
        // (CanonicalReportContextShapeTests forbids it), and a builder verb for this one fixture
        // would be YAGNI. No rows are added or removed — only one pinned existing row's column
        // value is overwritten, on this test's own clone. The malformed row is the assertion subject.
        await _helper!.ExecuteSqlAsync(
            $"UPDATE reports SET context = jsonb_set(context, '{{{key}}}', '{jsonValue}') WHERE id = {reportId}");
    }

    private void AssertSkipWasLogged(long reportId, string payloadFragment)
    {
        var warnings = _logs!.Entries
            .Where(e => e.Level >= LogLevel.Warning && e.Message.Contains(reportId.ToString()))
            .ToList();
        Assert.That(warnings, Has.Count.EqualTo(1), "exactly one warning names the skipped report");
        Assert.That(warnings[0].Message, Does.Not.Contain(payloadFragment), "the log must not carry the context payload");
        Assert.That(warnings[0].Exception?.ToString() ?? string.Empty, Does.Not.Contain(payloadFragment));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<(LogLevel, string, Exception?)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Enqueue((logLevel, formatter(state, exception), exception));
        }
    }
}
