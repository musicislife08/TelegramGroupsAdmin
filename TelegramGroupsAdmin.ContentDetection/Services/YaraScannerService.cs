using System.Diagnostics;
using dnYara;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// YARA pattern matching scanner service (Tier 1)
/// Uses compiled YARA rules to detect malware patterns and behaviors
/// Cross-platform via dnYara (Windows/Linux/macOS)
/// </summary>
public class YaraScannerService : IFileScannerService
{
    private readonly ILogger<YaraScannerService> _logger;
    private readonly FileScanningConfig _config;
    private readonly YaraRuleManager _ruleManager;

    public string ScannerName => "YARA";

    public YaraScannerService(
        ILogger<YaraScannerService> logger,
        IOptions<FileScanningConfig> config,
        YaraRuleManager ruleManager)
    {
        _logger = logger;
        _config = config.Value;
        _ruleManager = ruleManager;
    }

    public async Task<FileScanResult> ScanFileAsync(
        byte[] fileBytes,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Check if YARA is enabled
            if (!_config.Tier1.Yara.Enabled)
            {
                _logger.LogDebug("YARA scanner is disabled, returning clean result");
                return new FileScanResult
                {
                    Scanner = ScannerName,
                    IsClean = true,
                    ResultType = ScanResultType.Clean,
                    ScanDurationMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Get compiled rules
            var compiledRules = await _ruleManager.GetCompiledRulesAsync(cancellationToken);

            if (compiledRules == null)
            {
                _logger.LogWarning("YARA rules not available (not compiled or no rules found)");
                return new FileScanResult
                {
                    Scanner = ScannerName,
                    IsClean = true,  // Fail-open when no rules
                    ResultType = ScanResultType.Error,
                    ErrorMessage = "YARA rules not compiled",
                    ScanDurationMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogDebug("Scanning file with YARA (size: {Size} bytes, name: {FileName})",
                fileBytes.Length, fileName ?? "unknown");

            // Scan file bytes using dnYara
            var scanner = new Scanner();
            var matches = scanner.ScanMemory(ref fileBytes, compiledRules);

            stopwatch.Stop();

            // Check for matches
            if (matches == null || !matches.Any())
            {
                _logger.LogDebug("YARA scan: No rules matched (duration: {Duration}ms)",
                    stopwatch.ElapsedMilliseconds);

                return new FileScanResult
                {
                    Scanner = ScannerName,
                    IsClean = true,
                    ResultType = ScanResultType.Clean,
                    ScanDurationMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            // Rules matched - file is suspicious/infected
            var matchedRuleNames = matches.Select(m => m.MatchingRule.Identifier).ToList();
            var primaryThreat = matchedRuleNames.First();

            _logger.LogWarning("YARA scan: {Count} rule(s) matched - {Rules} (duration: {Duration}ms)",
                matchedRuleNames.Count,
                string.Join(", ", matchedRuleNames),
                stopwatch.ElapsedMilliseconds);

            return new FileScanResult
            {
                Scanner = ScannerName,
                IsClean = false,
                ResultType = ScanResultType.Infected,
                ThreatName = primaryThreat,
                ScanDurationMs = (int)stopwatch.ElapsedMilliseconds,
                Metadata = new Dictionary<string, object>
                {
                    ["matched_rules"] = matchedRuleNames,
                    ["match_count"] = matchedRuleNames.Count
                }
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Exception during YARA scan");

            return new FileScanResult
            {
                Scanner = ScannerName,
                IsClean = true,  // Fail-open on exception
                ResultType = ScanResultType.Error,
                ErrorMessage = ex.Message,
                ScanDurationMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}
