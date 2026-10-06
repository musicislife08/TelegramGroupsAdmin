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
            SuspectedFirstName = "Suspect", SuspectedIsBot = false, SuspectedLatestScanExplicit = true, SuspectedIsBanned = true,
            TargetFirstName = "Target", TargetIsBot = false, TargetLatestScanExplicit = false,
            TargetIsBanned = true, TargetLatestScanPromotional = false
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
            ExamFirstName = "Examinee", ExamUserIsBot = false, ExamUserLatestScanExplicit = true, ExamUserIsBanned = true
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
            ProfileFirstName = "Scanned", ProfileUserIsBot = false, ProfileUserLatestScanExplicit = true, ProfileUserIsBanned = true
        };

        Assert.That(view.ToProfileScanAlert()!.User.Verdict, Is.EqualTo(NameVerdict.Explicit));
    }

    [Test]
    public void ToProfileScanAlert_PromotionalFlag_MapsByBanState()
    {
        EnrichedReportView View(bool banned) => new()
        {
            Type = (short)ReportType.ProfileScanAlert,
            Context = """{"userId":44}""",
            ProfileFirstName = "Scanned", ProfileUserIsBot = false,
            ProfileUserLatestScanExplicit = false, ProfileUserLatestScanPromotional = true, ProfileUserIsBanned = banned
        };

        Assert.That(View(banned: true).ToProfileScanAlert()!.User.Verdict, Is.EqualTo(NameVerdict.Promotional));
        Assert.That(View(banned: false).ToProfileScanAlert()!.User.Verdict, Is.EqualTo(NameVerdict.Clean));
    }
}
