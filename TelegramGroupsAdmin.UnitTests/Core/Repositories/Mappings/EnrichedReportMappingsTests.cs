using NUnit.Framework;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;
using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.UnitTests.Core.Repositories.Mappings;

[TestFixture]
public class EnrichedReportMappingsTests
{
    [Test]
    public void ToImpersonationAlert_ExplicitFlags_MapToExplicitVerdicts()
    {
        var view = new EnrichedReportView
        {
            Type = (short)ReportType.ImpersonationAlert,
            Context = """{"suspectedUserId":11,"targetUserId":22}""",
            SuspectedFirstName = "Suspect", SuspectedIsBot = false, SuspectedLatestScanExplicit = true,
            TargetFirstName = "Target", TargetIsBot = false, TargetLatestScanExplicit = false
        };

        var alert = view.ToImpersonationAlert()!;

        Assert.That(alert.SuspectedUser.Verdict, Is.EqualTo(NameVerdict.Explicit));
        Assert.That(alert.TargetUser.Verdict, Is.EqualTo(NameVerdict.Clean));
    }

    [Test]
    public void ToExamResult_ExplicitFlag_MapsToExplicitVerdict()
    {
        var view = new EnrichedReportView
        {
            Type = (short)ReportType.ExamResult,
            Context = """{"userId":33}""",
            ExamFirstName = "Examinee", ExamUserIsBot = false, ExamUserLatestScanExplicit = true
        };

        Assert.That(view.ToExamResult()!.User.Verdict, Is.EqualTo(NameVerdict.Explicit));
    }

    [Test]
    public void ToProfileScanAlert_ExplicitFlag_MapsToExplicitVerdict()
    {
        var view = new EnrichedReportView
        {
            Type = (short)ReportType.ProfileScanAlert,
            Context = """{"userId":44}""",
            ProfileFirstName = "Scanned", ProfileUserIsBot = false, ProfileUserLatestScanExplicit = true
        };

        Assert.That(view.ToProfileScanAlert()!.User.Verdict, Is.EqualTo(NameVerdict.Explicit));
    }
}
