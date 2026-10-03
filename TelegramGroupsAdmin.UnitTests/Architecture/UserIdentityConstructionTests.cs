using System.Text.RegularExpressions;

namespace TelegramGroupsAdmin.UnitTests.Architecture;

/// <summary>
/// A UserIdentity that reaches bot-written text must come from IUserIdentityService or the shared
/// row mapper, so it carries the name verdict. See .claude/rules/user-identity.md.
/// </summary>
[TestFixture]
public class UserIdentityConstructionTests
{
    // Sanctioned constructors: the type itself, the shared row mapper, and the service's
    // fallback when recording names fails.
    private static readonly string[] Allowlist =
    [
        "TelegramGroupsAdmin.Core/Models/UserIdentity.cs",
        "TelegramGroupsAdmin.Core/Repositories/Mappings/UserIdentityMapping.cs",
        "TelegramGroupsAdmin.Telegram/Services/Identity/UserIdentityService.cs",
    ];

    // The lookbehinds keep WebUserIdentity (an unrelated web-account type) out of the match.
    private static readonly Regex Construction = new(
        @"new\s+(Core\.Models\.)?(?<![A-Za-z])UserIdentity\s*\(|(?<![A-Za-z])UserIdentity\.FromId\s*\(",
        RegexOptions.Compiled);

    internal static IEnumerable<string> Violations(string repoRoot) =>
        Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
            .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
            .Where(f => f.StartsWith("TelegramGroupsAdmin") && !f.Contains("Tests/") && !f.Contains("Tests.")
                        && !f.Contains("/bin/") && !f.Contains("/obj/") && !f.Contains("/Migrations/")
                        && !f.StartsWith("TelegramGroupsAdmin.Testing."))
            .Where(f => !Allowlist.Contains(f))
            .Where(f => Construction.IsMatch(File.ReadAllText(Path.Combine(repoRoot, f))));

    [Test]
    public void ProductionCode_BuildsUserIdentityOnlyThroughSanctionedPaths()
    {
        var violations = Violations(RepoRoot()).ToList();

        Assert.That(violations, Is.Empty,
            "Build UserIdentity through IUserIdentityService or UserIdentityMapping (see .claude/rules/user-identity.md). Offending files: "
            + string.Join(", ", violations));
    }

    [Test]
    public void Detector_FlagsConstructionOutsideAllowlist()
    {
        var root = Path.Combine(Path.GetTempPath(), "uid-scan-" + Guid.NewGuid());
        var file = Path.Combine(root, "TelegramGroupsAdmin.Telegram", "Bad.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "var u = new UserIdentity(1, null, null, null);");
        try
        {
            Assert.That(Violations(root), Is.EquivalentTo(new[] { "TelegramGroupsAdmin.Telegram/Bad.cs" }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Detector_IgnoresWebUserIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "uid-scan-" + Guid.NewGuid());
        var file = Path.Combine(root, "TelegramGroupsAdmin", "Ok.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "var u = WebUserIdentity.FromId(\"x\");");
        try
        {
            Assert.That(Violations(root), Is.Empty);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TelegramGroupsAdmin.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }
}
