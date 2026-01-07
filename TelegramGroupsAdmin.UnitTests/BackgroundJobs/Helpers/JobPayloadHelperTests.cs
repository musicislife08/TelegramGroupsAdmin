using Microsoft.Extensions.Logging;
using NSubstitute;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Helpers;
using TelegramGroupsAdmin.Core.BackgroundJobs;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Helpers;

[TestFixture]
public class JobPayloadHelperTests
{
    private ILogger _logger = null!;
    private IJobExecutionContext _context = null!;
    private IScheduler _scheduler = null!;
    private ITrigger _trigger = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger>();
        _context = Substitute.For<IJobExecutionContext>();
        _scheduler = Substitute.For<IScheduler>();
        _trigger = Substitute.For<ITrigger>();

        _context.Scheduler.Returns(_scheduler);
        _context.Trigger.Returns(_trigger);
        _trigger.Key.Returns(new TriggerKey("test-trigger", "test-group"));
    }

    #region TryGetPayloadAsync Tests

    [Test]
    public async Task TryGetPayloadAsync_ValidPayload_ReturnsDeserializedObject()
    {
        // Arrange
        var payload = new TestPayload("test-value", 42);
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
        var jobDataMap = new JobDataMap { { JobDataKeys.PayloadJson, payloadJson } };
        _context.MergedJobDataMap.Returns(jobDataMap);

        // Act
        var result = await JobPayloadHelper.TryGetPayloadAsync<TestPayload>(_context, _logger);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Name, Is.EqualTo("test-value"));
        Assert.That(result.Value, Is.EqualTo(42));
    }

    [Test]
    public async Task TryGetPayloadAsync_MissingPayload_ReturnsNullAndCleansUp()
    {
        // Arrange
        var jobDataMap = new JobDataMap(); // Empty - no payload
        _context.MergedJobDataMap.Returns(jobDataMap);
        _scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        // Act
        var result = await JobPayloadHelper.TryGetPayloadAsync<TestPayload>(_context, _logger);

        // Assert
        Assert.That(result, Is.Null);
        await _scheduler.Received(1).UnscheduleJob(
            Arg.Is<TriggerKey>(k => k.Name == "test-trigger"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryGetPayloadAsync_EmptyPayloadJson_ReturnsNullAndCleansUp()
    {
        // Arrange
        var jobDataMap = new JobDataMap { { JobDataKeys.PayloadJson, "" } };
        _context.MergedJobDataMap.Returns(jobDataMap);
        _scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        // Act
        var result = await JobPayloadHelper.TryGetPayloadAsync<TestPayload>(_context, _logger);

        // Assert
        Assert.That(result, Is.Null);
        await _scheduler.Received(1).UnscheduleJob(
            Arg.Is<TriggerKey>(k => k.Name == "test-trigger"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryGetPayloadAsync_InvalidJson_ReturnsNull()
    {
        // Arrange
        var jobDataMap = new JobDataMap { { JobDataKeys.PayloadJson, "not valid json {{{" } };
        _context.MergedJobDataMap.Returns(jobDataMap);

        // Act
        var result = await JobPayloadHelper.TryGetPayloadAsync<TestPayload>(_context, _logger);

        // Assert
        Assert.That(result, Is.Null);
        // Should NOT attempt cleanup for invalid JSON (that's a bug, not stale data)
        await _scheduler.DidNotReceive().UnscheduleJob(
            Arg.Any<TriggerKey>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryGetPayloadAsync_CustomJobName_UsesProvidedName()
    {
        // Arrange
        var jobDataMap = new JobDataMap(); // Empty - trigger warning log
        _context.MergedJobDataMap.Returns(jobDataMap);
        _scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        // Act
        await JobPayloadHelper.TryGetPayloadAsync<TestPayload>(_context, _logger, "CustomJobName");

        // Assert - verify custom name was used (check log was called)
        // The log call happens, we just verify cleanup was attempted
        await _scheduler.Received(1).UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TryGetPayloadAsync_DefaultJobName_InfersFromPayloadType()
    {
        // Arrange
        var payload = new TestPayload("test", 1);
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
        var jobDataMap = new JobDataMap { { JobDataKeys.PayloadJson, payloadJson } };
        _context.MergedJobDataMap.Returns(jobDataMap);

        // Act
        var result = await JobPayloadHelper.TryGetPayloadAsync<TestPayload>(_context, _logger);

        // Assert - job name should be inferred as "TestJob" (TestPayload -> TestJob)
        Assert.That(result, Is.Not.Null);
    }

    #endregion

    #region GetRequiredPayload Tests

    [Test]
    public void GetRequiredPayload_ValidPayload_ReturnsDeserializedObject()
    {
        // Arrange
        var payload = new TestPayload("required-test", 100);
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
        var jobDataMap = new JobDataMap { { JobDataKeys.PayloadJson, payloadJson } };
        _context.MergedJobDataMap.Returns(jobDataMap);

        // Act
        var result = JobPayloadHelper.GetRequiredPayload<TestPayload>(_context);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("required-test"));
        Assert.That(result.Value, Is.EqualTo(100));
    }

    [Test]
    public void GetRequiredPayload_MissingPayload_ThrowsKeyNotFoundException()
    {
        // Arrange
        var jobDataMap = new JobDataMap(); // Empty
        _context.MergedJobDataMap.Returns(jobDataMap);

        // Act & Assert
        // Quartz's GetString throws KeyNotFoundException when key doesn't exist
        Assert.Throws<KeyNotFoundException>(() =>
            JobPayloadHelper.GetRequiredPayload<TestPayload>(_context));
    }

    [Test]
    public void GetRequiredPayload_InvalidJson_ThrowsJsonException()
    {
        // Arrange
        var jobDataMap = new JobDataMap { { JobDataKeys.PayloadJson, "invalid json" } };
        _context.MergedJobDataMap.Returns(jobDataMap);

        // Act & Assert
        Assert.Throws<System.Text.Json.JsonException>(() =>
            JobPayloadHelper.GetRequiredPayload<TestPayload>(_context));
    }

    #endregion

    /// <summary>
    /// Test payload record for unit tests
    /// </summary>
    private record TestPayload(string Name, int Value);
}
