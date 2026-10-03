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

    // Explicit construction, with any namespace qualification. The lookbehind keeps WebUserIdentity
    // (an unrelated web-account type) out of the match; FromId is the id-only factory.
    private static readonly Regex Explicit = new(
        @"new\s+([A-Za-z_]\w*\.)*UserIdentity\s*\(|(?<![A-Za-z])UserIdentity\.FromId\s*\(",
        RegexOptions.Compiled);

    // Target-typed construction (`new(...)`) never names the type, so it is caught by heuristic:
    // a line with `new(` that either mentions UserIdentity (a declaration like `UserIdentity? x = new(`)
    // or assigns to a member name that holds a UserIdentity (object initializers, property sets).
    // Extend UserIdentityMembers when a new UserIdentity-typed member is introduced.
    private static readonly Regex TargetTyped = new(
        @"(?<![A-Za-z])UserIdentity\??\s+\w+\s*=\s*new\s*\(|\b(User|Sender|Recipient|Reporter|SuspectedUser|TargetUser|Target|Editor|ChangedBy|ReceivedSender)\s*=\s*new\s*\(",
        RegexOptions.Compiled);

    private static bool Constructs(string text) => Explicit.IsMatch(text) || TargetTyped.IsMatch(text);

    internal static IEnumerable<string> Violations(string repoRoot) =>
        Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
            .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
            .Where(f => f.StartsWith("TelegramGroupsAdmin") && !f.Contains("Tests/") && !f.Contains("Tests.")
                        && !f.Contains("/bin/") && !f.Contains("/obj/") && !f.Contains("/Migrations/")
                        && !f.StartsWith("TelegramGroupsAdmin.Testing."))
            .Where(f => !Allowlist.Contains(f))
            .Where(f => Constructs(File.ReadAllText(Path.Combine(repoRoot, f))));

    [Test]
    public void ProductionCode_BuildsUserIdentityOnlyThroughSanctionedPaths()
    {
        var violations = Violations(RepoRoot()).ToList();

        Assert.That(violations, Is.Empty,
            "Build UserIdentity through IUserIdentityService or UserIdentityMapping (see .claude/rules/user-identity.md). Offending files: "
            + string.Join(", ", violations));
    }

    [TestCase("var u = new UserIdentity(1, null, null, null);")]
    [TestCase("var u = new Core.Models.UserIdentity(1, null, null, null);")]
    [TestCase("var u = new Models.UserIdentity(1, null, null, null);")]
    [TestCase("var u = new TelegramGroupsAdmin.Core.Models.UserIdentity(1, null, null, null);")]
    [TestCase("var u = UserIdentity.FromId(1);")]
    [TestCase("UserIdentity x = new(1, null, null, null);")]
    [TestCase("UserIdentity? x = new(1, null, null, null);")]
    [TestCase("var m = new Foo { User = new(1, null, null, null) };")]
    public void Detector_FlagsConstructionOutsideAllowlist(string code)
    {
        Assert.That(ViolationsFor("TelegramGroupsAdmin.Telegram/Bad.cs", code),
            Is.EquivalentTo(new[] { "TelegramGroupsAdmin.Telegram/Bad.cs" }));
    }

    [TestCase("var u = WebUserIdentity.FromId(\"x\");")]
    [TestCase("var u = new WebUserIdentity(\"x\");")]
    public void Detector_IgnoresWebUserIdentity(string code)
    {
        Assert.That(ViolationsFor("TelegramGroupsAdmin/Ok.cs", code), Is.Empty);
    }

    [Test]
    public void Detector_IgnoresAllowlistedFile()
    {
        Assert.That(ViolationsFor(Allowlist[0], "var u = new UserIdentity(1, null, null, null);"), Is.Empty);
    }

    private static List<string> ViolationsFor(string relativePath, string code)
    {
        var root = Path.Combine(Path.GetTempPath(), "uid-scan-" + Guid.NewGuid());
        var file = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, code);
        try
        {
            return Violations(root).ToList();
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
