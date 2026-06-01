using System.Text.RegularExpressions;

namespace TelegramGroupsAdmin.UnitTests.Architecture;

/// <summary>
/// Regression guard: ensures no production Telegram send uses any parse_mode. Everything renders
/// via entity-based <c>TelegramMessage</c> composition, so <c>ParseMode.Markdown</c> (V1),
/// <c>ParseMode.MarkdownV2</c>, and <c>ParseMode.Html</c> are all banned in production code.
/// Comment lines (// /// *) referencing old approaches for documentation are ignored.
///
/// If this test fails, the failure message lists every offending file:line so the violation is actionable.
/// </summary>
[TestFixture]
public class ParseModeUsageGuardTests
{
    private static readonly Regex LegacyMarkdownPattern = new(
        @"ParseMode\.Markdown(?!V2)",
        RegexOptions.Compiled);

    [Test]
    public void No_app_level_markdown_or_html_parse_mode_sends_remain()
    {
        var root = FindRepoRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateProductionCsFiles(root))
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                // Skip comment lines — doc-comment references to old approaches are acceptable
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*"))
                    continue;

                // Legacy ParseMode.Markdown (anything NOT followed by V2) — disallowed everywhere
                if (LegacyMarkdownPattern.IsMatch(line))
                    offenders.Add($"{file}:{i + 1} (ParseMode.Markdown)");

                // ParseMode.Html — disallowed everywhere in production code
                if (line.Contains("ParseMode.Html"))
                    offenders.Add($"{file}:{i + 1} (ParseMode.Html)");

                // ParseMode.MarkdownV2 — disallowed everywhere; all DM transport is entity-based now
                if (line.Contains("ParseMode.MarkdownV2"))
                    offenders.Add($"{file}:{i + 1} (ParseMode.MarkdownV2)");
            }
        }

        Assert.That(
            offenders,
            Is.Empty,
            "Disallowed parse-mode sends remain in production code.\n"
            + "To fix: migrate to entity-based TelegramMessageBuilder composition.\n"
            + "Violations:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// Enumerates all production .cs files, excluding obj/bin directories and all test projects.
    /// Test projects are identified by path segments: UnitTests, IntegrationTests, ComponentTests, E2ETests, Tests/.
    /// </summary>
    private static IEnumerable<string> EnumerateProductionCsFiles(string root)
    {
        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                // Normalize separators for cross-platform matching
                var normalized = f.Replace('\\', '/');

                // Exclude build output directories
                if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
                    return false;

                // Exclude all test projects by known path segments
                if (normalized.Contains(".UnitTests/")
                    || normalized.Contains(".IntegrationTests/")
                    || normalized.Contains(".ComponentTests/")
                    || normalized.Contains(".E2ETests/")
                    || normalized.Contains("Tests/"))
                    return false;

                return true;
            });
    }

    /// <summary>
    /// Walks up from the test assembly output directory until it finds the directory
    /// that contains TelegramGroupsAdmin.sln — that is the repo root.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            if (dir.GetFiles("TelegramGroupsAdmin.sln").Length > 0)
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate TelegramGroupsAdmin.sln starting from '{AppContext.BaseDirectory}'. "
            + "Ensure the solution file exists at the repository root.");
    }
}
