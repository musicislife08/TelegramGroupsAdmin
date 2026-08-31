using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.UnitTests.Core;

[TestFixture]
public class SystemActorIdsTests
{
    [Test]
    public void AllExistingActorStaticFields_ResolveToSystemActorIdsValues()
    {
        Assert.That(Actor.AutoDetection.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.AutoDetection));
        Assert.That(Actor.BotProtection.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.BotProtection));
        Assert.That(Actor.FileScanner.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.FileScanner));
        Assert.That(Actor.AutoTrust.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.AutoTrust));
        Assert.That(Actor.Impersonation.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.Impersonation));
        Assert.That(Actor.AutoBan.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.AutoBan));
        Assert.That(Actor.Cas.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.Cas));
        Assert.That(Actor.LanguageWarning.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.LanguageWarning));
        Assert.That(Actor.SystemSeed.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.SystemSeed));
        Assert.That(Actor.ExamFlow.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.ExamFlow));
        Assert.That(Actor.WelcomeFlow.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.WelcomeFlow));
        Assert.That(Actor.TempbanExpiry.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.TempbanExpiry));
        Assert.That(Actor.Unknown.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.Unknown));
        Assert.That(Actor.ProfileScan.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.ProfileScan));
        Assert.That(Actor.UsernameBlacklist.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.UsernameBlacklist));
        Assert.That(Actor.Bootstrap.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.Bootstrap));
        Assert.That(Actor.ProfileDiffDetection.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.ProfileDiffDetection));
        Assert.That(Actor.WelcomeBypass.GetSystemIdentifier(), Is.EqualTo(SystemActorIds.WelcomeBypass));
    }

    [Test]
    public void FromSystem_ResolvesDisplayName_ForEveryKnownConstant()
    {
        Assert.That(Actor.FromSystem(SystemActorIds.AutoDetection).DisplayName, Is.EqualTo("Auto-Detection"));
        Assert.That(Actor.FromSystem(SystemActorIds.WelcomeBypass).DisplayName, Is.EqualTo("Welcome Bypass"));
        Assert.That(Actor.FromSystem(SystemActorIds.BotProtection).DisplayName, Is.EqualTo("Bot Protection"));
    }
}
