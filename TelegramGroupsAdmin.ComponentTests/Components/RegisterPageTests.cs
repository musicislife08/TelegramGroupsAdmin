using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using TelegramGroupsAdmin.Components.Pages;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Services;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// The Register page reads the strict email-configuration state. When it is Indeterminate the server refuses
/// every registration, so the page says so and keeps the form disabled instead of inviting a doomed submit.
/// </summary>
[TestFixture]
public class RegisterPageTests : MudBlazorTestContext
{
    private readonly IAuthService _authService = Substitute.For<IAuthService>();
    private readonly IFeatureAvailabilityService _features = Substitute.For<IFeatureAvailabilityService>();

    public RegisterPageTests()
    {
        Services.AddSingleton(_authService);
        Services.AddSingleton(_features);
        Services.AddSingleton(new InternalApiClient(
            Substitute.For<IHttpClientFactory>(), Options.Create(new AppOptions { BaseUrl = "https://app.unit.test" })));
    }

    [SetUp]
    public void SetUp()
    {
        _authService.IsFirstRunAsync(Arg.Any<CancellationToken>()).Returns(false);
    }

    [TestCase(EmailConfigurationState.Indeterminate, true)]
    [TestCase(EmailConfigurationState.Enabled, false)]
    [TestCase(EmailConfigurationState.Disabled, false)]
    public void Form_IsDisabledOnlyWhenTheEmailStateIsIndeterminate(EmailConfigurationState state, bool expectDisabled)
    {
        _features.GetEmailConfigurationStateAsync().Returns(state);

        var cut = Render<Register>();

        cut.WaitForAssertion(() =>
        {
            var submit = cut.FindAll("button").Single(b => b.TextContent.Contains("Create Account"));
            var inputs = cut.FindAll("input");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(submit.HasAttribute("disabled"), Is.EqualTo(expectDisabled));
                Assert.That(inputs, Is.Not.Empty);
                Assert.That(inputs.All(i => i.HasAttribute("disabled") == expectDisabled), Is.True,
                    "every form field follows the submit button");
                Assert.That(cut.Markup.Contains("Registration is temporarily unavailable"), Is.EqualTo(expectDisabled));
            }
        });
    }

    [Test]
    public void Form_IsDisabledWhileTheStateLookupIsPending_ThenEnabledOnceItResolves()
    {
        var firstRun = new TaskCompletionSource<bool>();
        _authService.IsFirstRunAsync(Arg.Any<CancellationToken>()).Returns(firstRun.Task);
        _features.GetEmailConfigurationStateAsync().Returns(EmailConfigurationState.Enabled);

        var cut = Render<Register>();

        var pendingSubmit = cut.FindAll("button").Single(b => b.TextContent.Contains("Create Account"));
        Assert.That(pendingSubmit.HasAttribute("disabled"), Is.True, "not submittable before the state resolves");

        firstRun.SetResult(false);

        cut.WaitForAssertion(() =>
        {
            var submit = cut.FindAll("button").Single(b => b.TextContent.Contains("Create Account"));
            Assert.That(submit.HasAttribute("disabled"), Is.False, "submittable once the state has resolved");
        });
    }
}
