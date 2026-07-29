using TelegramGroupsAdmin.Core.BackgroundJobs;

namespace TelegramGroupsAdmin.UnitTests.Core.BackgroundJobs;

[TestFixture]
public class BackgroundJobNamesTests
{
    [Test]
    public void AllRegisteredNames_ContainsEveryConstField()
    {
        var expected = new[]
        {
            BackgroundJobNames.ScheduledBackup,
            BackgroundJobNames.DataCleanup,
            BackgroundJobNames.UserPhotoRefresh,
            BackgroundJobNames.BlocklistSync,
            BackgroundJobNames.DatabaseMaintenance,
            BackgroundJobNames.ChatHealthCheck,
            BackgroundJobNames.ClassifierRetraining,
            BackgroundJobNames.DeleteMessage,
            BackgroundJobNames.DeleteUserMessages,
            BackgroundJobNames.FetchUserPhoto,
            BackgroundJobNames.FileScan,
            BackgroundJobNames.RotateBackupPassphrase,
            BackgroundJobNames.TempbanExpiry,
            BackgroundJobNames.WelcomeTimeout,
            BackgroundJobNames.ProfileRescan,
        };

        Assert.That(BackgroundJobNames.AllRegisteredNames, Is.EquivalentTo(expected));
    }

    [Test]
    public void AllRegisteredNames_IsCaseSensitive()
    {
        Assert.That(BackgroundJobNames.AllRegisteredNames.Contains("DeleteMessage"), Is.True);
        Assert.That(BackgroundJobNames.AllRegisteredNames.Contains("deletemessage"), Is.False);
    }

    [Test]
    public void AllRegisteredNames_DoesNotIncludeNonStringConstants()
    {
        // Sanity check: if any future const non-string fields are added to
        // BackgroundJobNames, they should not appear in the registered-names set.
        foreach (var name in BackgroundJobNames.AllRegisteredNames)
            Assert.That(name, Is.TypeOf<string>());
    }
}
