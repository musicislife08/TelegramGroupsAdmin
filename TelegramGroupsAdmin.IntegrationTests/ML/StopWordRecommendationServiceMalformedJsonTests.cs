using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.ContentDetection.ML;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.ML;

/// <summary>
/// Regression guard for #495 on the second caller of CheckResultsSerializer.Deserialize.
///
/// StopWordRecommendationService.GenerateRemovalRecommendationsAsync deserializes every
/// in-window detection_result's check_results_json inside a per-stop-word loop. A single
/// row whose JSON no longer binds to the current CheckResults shape must be logged and
/// skipped (continue) — it must never abort the whole recommendation batch.
///
/// Substrate: the full golden template (no Reduce). The service's ValidateDataAvailabilityAsync
/// gate requires >= 50 spam training samples and >= 100 legit messages, so a narrowed substrate
/// would short-circuit before the removal loop ever runs. We pass a far-past 'since' so every
/// frozen canonical timestamp lands in-window, then assert ValidationMessage is null to prove the
/// gate passed and the removal loop (where the catch lives) actually executed.
/// </summary>
[TestFixture]
public class StopWordRecommendationServiceMalformedJsonTests
{
    private MigrationTestHelper _testHelper = null!;
    private IServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private IStopWordRecommendationService _service = null!;

    // Same realistic corruption shape as the AnalyticsRepository guard: a legacy V1-era row
    // where CheckName is a JSON *string* rather than its integer ordinal. CheckResult.CheckName
    // is a bare enum and DeserializeOptions registers no JsonStringEnumConverter, so
    // System.Text.Json rejects the string token with a JsonException. This is the exact shape
    // migration 20260307182307_FixCheckResultsJsonCheckNameType was written to repair. The
    // column is JSONB, so the payload must be valid JSON of the wrong shape, not malformed.
    private const string LegacyStringEnumJson =
        """{"Checks":[{"CheckName":"Bayes","Score":3.5,"Abstained":false,"Details":"legacy V1 string-enum row","ProcessingTimeMs":1.0}]}""";

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        // TEST-DATA RULE EXCEPTION: directly overwrite one canonical detection_result's
        // check_results_json via raw SQL rather than via a GoldenMutatePlanBuilder verb. The
        // golden dataset is scrubbed real prod data and cannot carry deserialization-breaking
        // JSON by construction; a reusable builder verb for a single caller would be YAGNI. No
        // rows are added or removed — only one existing row's column value is overwritten. The
        // lowest-id row with non-null JSON is chosen so the removal loop is guaranteed to visit it.
        await using (var ctx = _testHelper.GetDbContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                """
                UPDATE detection_results
                SET check_results_json = {0}::jsonb
                WHERE id = (
                    SELECT id FROM detection_results
                    WHERE check_results_json IS NOT NULL
                    ORDER BY id
                    LIMIT 1)
                """,
                LegacyStringEnumJson);
        }

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>((_, options) => options.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<ITokenizerService, TokenizerService>();
        services.AddScoped<IStopWordsRepository, StopWordsRepository>();
        services.AddScoped<IStopWordRecommendationService, StopWordRecommendationService>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _service = _scope.ServiceProvider.GetRequiredService<IStopWordRecommendationService>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    [Test]
    public async Task GenerateRecommendations_WhenRowHasUndeserializableJson_SkipsRowAndCompletesBatch()
    {
        // Far-past 'since' so every frozen canonical timestamp counts toward the data gate.
        var since = DateTimeOffset.UtcNow.AddYears(-5);

        // Act — without the #495 catch, the corrupt row's Deserialize throws JsonException out of
        // GenerateRemovalRecommendationsAsync and aborts the whole batch. With it, the row is
        // logged and skipped (continue) and the batch completes normally.
        var batch = await _service.GenerateRecommendationsAsync(since);

        // Assert
        Assert.That(batch, Is.Not.Null);
        Assert.That(batch.ValidationMessage, Is.Null,
            "Golden template must satisfy the data-availability gate so the removal loop "
            + "(where the malformed-JSON catch lives) actually runs; a non-null message means "
            + "the test short-circuited before exercising #495");
    }
}
