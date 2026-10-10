using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Bunit.Rendering;
using HumanCron.Quartz.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using NSubstitute;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using BackgroundJobsPage = TelegramGroupsAdmin.Components.Shared.Settings.BackgroundJobs;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Services.Media;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for BackgroundJobs.razor: the Profile Rescan job's Name-Only Retry Limit field
/// shows the stored value (3 when the stored settings predate the field) and saves it back.
/// </summary>
[TestFixture]
public class BackgroundJobsTests : MudBlazorTestContext
{
    private readonly IBackgroundJobConfigService _jobConfig;

    public BackgroundJobsTests()
    {
        _jobConfig = Substitute.For<IBackgroundJobConfigService>();
        Services.AddSingleton(_jobConfig);
        Services.AddSingleton(Substitute.For<IMediaRefetchQueueService>());
        Services.AddSingleton(Substitute.For<IJobTriggerService>());
        Services.AddSingleton(Substitute.For<IQuartzScheduleConverter>());
        Services.AddSingleton(Substitute.For<ILogger<BackgroundJobsPage>>());
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();
        this.AddTestWebUser();
    }

    [SetUp]
    public async Task SetUp()
    {
        // The fixture shares its context and substitutes across tests.
        await DisposeComponentsAsync();
        _jobConfig.ClearReceivedCalls();
    }

    private void StoredRescanSettings(ProfileRescanSettings? settings) =>
        _jobConfig.GetAllJobsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, BackgroundJobConfig>
        {
            [BackgroundJobNames.ProfileRescan] = new()
            {
                JobName = BackgroundJobNames.ProfileRescan,
                DisplayName = "Profile Rescan",
                Description = "Retries incomplete profile scans",
                Schedule = "every 6 hours",
                Enabled = true,
                ProfileRescan = settings
            }
        });

    private IRenderedComponent<ContainerFragment> RenderAndOpenRescanSettings()
    {
        var cut = Render(builder =>
        {
            builder.OpenComponent<MudDialogProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<BackgroundJobsPage>(1);
            builder.CloseComponent();
        });
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Profile Rescan")), TimeSpan.FromSeconds(2));
        cut.Find("button[title='Configure']").Click();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Name-Only Retry Limit")), TimeSpan.FromSeconds(2));
        return cut;
    }

    private static IElement RetryLimitInput(IRenderedComponent<ContainerFragment> cut) =>
        cut.FindComponents<MudNumericField<int>>()
            .Single(f => f.Instance.Label == "Name-Only Retry Limit")
            .Find("input");

    [Test]
    public void RetryLimit_ShowsTheStoredValue()
    {
        StoredRescanSettings(new ProfileRescanSettings { NameOnlyRetryLimit = 7 });

        var cut = RenderAndOpenRescanSettings();

        Assert.That(RetryLimitInput(cut).GetAttribute("value"), Is.EqualTo("7"));
    }

    [Test]
    public void RetryLimit_StoredSettingsWithoutTheField_ShowsThree()
    {
        StoredRescanSettings(JsonSerializer.Deserialize<ProfileRescanSettings>("""{"BatchSize":50,"RescanAfter":"1w"}"""));

        var cut = RenderAndOpenRescanSettings();

        Assert.That(RetryLimitInput(cut).GetAttribute("value"), Is.EqualTo("3"));
    }

    [Test]
    public async Task RetryLimit_IsSavedBack()
    {
        StoredRescanSettings(new ProfileRescanSettings { NameOnlyRetryLimit = 3 });
        var cut = RenderAndOpenRescanSettings();

        RetryLimitInput(cut).Change("5");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save").Click();

        await _jobConfig.Received(1).UpdateJobConfigAsync(
            BackgroundJobNames.ProfileRescan,
            Arg.Is<BackgroundJobConfig>(c => c!.ProfileRescan!.NameOnlyRetryLimit == 5),
            Arg.Any<WebUserIdentity?>(),
            Arg.Any<CancellationToken>());
    }
}
