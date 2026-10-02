using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared.ContentDetection;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services.Blocklists;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// UrlFiltersConfig saves the rules first, then rebuilds the blocklist cache. When some subscriptions fail to
/// sync (<see cref="BlocklistSyncException"/>), the save itself still happened: the panel must reload and warn,
/// not report the save as failed.
/// </summary>
[TestFixture]
public class UrlFiltersConfigTests : MudBlazorTestContext
{
    private readonly IBlocklistSubscriptionsRepository _blocklistRepo = Substitute.For<IBlocklistSubscriptionsRepository>();
    private readonly IDomainFiltersRepository _domainFiltersRepo = Substitute.For<IDomainFiltersRepository>();
    private readonly IBlocklistSyncService _blocklistSync = Substitute.For<IBlocklistSyncService>();
    private readonly ISnackbar _snackbar = Substitute.For<ISnackbar>();

    private static BlocklistSyncException PartialSyncFailure() =>
        new([new BlocklistSyncFailure(7, "Dead List", "The URL returned HTTP 404.")], attempted: 3);

    public UrlFiltersConfigTests()
    {
        // The bUnit context lives for the fixture, so services are registered once here and reset per test.
        Services.AddSingleton(_blocklistRepo);
        Services.AddSingleton(_domainFiltersRepo);
        Services.AddSingleton(_blocklistSync);
        Services.AddSingleton(Substitute.For<IConfigService>());
        Services.AddSingleton(_snackbar);

        this.AddTestWebUser();

        // Complete the popover interop calls so the context can be reused across the fixture's tests.
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();
    }

    [SetUp]
    public void SetUp()
    {
        _blocklistRepo.ClearReceivedCalls();
        _domainFiltersRepo.ClearReceivedCalls();
        _blocklistSync.ClearReceivedCalls();
        _snackbar.ClearReceivedCalls();

        _blocklistRepo.GetAllAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns([]);
        _domainFiltersRepo.GetAllAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns([]);
        _blocklistSync.RebuildCacheAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _blocklistSync.SyncAllAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    [TestCase("Save Hard Block Rules", "Hard block configuration saved")]
    [TestCase("Save Soft Block Rules", "Soft block configuration saved")]
    [TestCase("Save Whitelist", "Whitelist saved")]
    public void Save_WhenSomeBlocklistsFailToSync_ReloadsAndWarnsInsteadOfReportingAnError(string button, string savedText)
    {
        // Arrange
        _blocklistSync.RebuildCacheAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(PartialSyncFailure()));
        var cut = Render<UrlFiltersConfig>(p => p.Add(c => c.ChatId, 0));
        cut.WaitForState(() => cut.FindAll("button").Any(b => b.TextContent.Contains(button)));
        _blocklistRepo.ClearReceivedCalls();

        // Act
        cut.FindAll("button").Single(b => b.TextContent.Contains(button)).Click();

        // Assert
        cut.WaitForAssertion(() => AssertReloadedAndWarned(savedText));
    }

    [Test]
    public void SyncAll_WhenSomeBlocklistsFailToSync_ReloadsAndWarnsInsteadOfReportingAnError()
    {
        // Arrange
        _blocklistSync.SyncAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(PartialSyncFailure()));
        var cut = Render<UrlFiltersConfig>(p => p.Add(c => c.ChatId, 0));
        cut.WaitForState(() => cut.FindAll("button").Any(b => b.TextContent.Contains("Sync All Blocklists Now")));
        _blocklistRepo.ClearReceivedCalls();

        // Act
        cut.FindAll("button").Single(b => b.TextContent.Contains("Sync All Blocklists Now")).Click();

        // Assert
        cut.WaitForAssertion(() => AssertReloadedAndWarned("synced"));
    }

    private void AssertReloadedAndWarned(string expectedText)
    {
        var messages = SnackbarMessages();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(messages.Where(m => m.Severity == Severity.Error), Is.Empty, "a partial sync is not a failed save");
            Assert.That(messages.Where(m => m.Severity == Severity.Warning).Select(m => m.Message),
                Has.One.Contains(expectedText).And.Contains("Dead List"));
            _blocklistRepo.Received(1).GetAllAsync(0, Arg.Any<CancellationToken>());
        }
    }

    private List<(string Message, Severity Severity)> SnackbarMessages() =>
        _snackbar.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ISnackbar.Add) && c.GetArguments() is [string, Severity, ..])
            .Select(c => ((string)c.GetArguments()[0]!, (Severity)c.GetArguments()[1]!))
            .ToList();
}
