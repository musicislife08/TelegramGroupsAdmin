using dnYara;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Manages loading, compiling, and caching YARA rules
/// Thread-safe singleton for rule compilation
/// Uses dnYara library for cross-platform support (Windows/Linux/macOS)
/// </summary>
public class YaraRuleManager : IDisposable
{
    private readonly ILogger<YaraRuleManager> _logger;
    private readonly FileScanningConfig _config;
    private readonly SemaphoreSlim _compileLock = new(1, 1);
    private YaraContext? _context;
    private CompiledRules? _compiledRules;
    private DateTimeOffset _lastCompiled = DateTimeOffset.MinValue;
    private bool _disposed;

    public YaraRuleManager(
        ILogger<YaraRuleManager> logger,
        IOptions<FileScanningConfig> config)
    {
        _logger = logger;
        _config = config.Value;
    }

    /// <summary>
    /// Get compiled YARA rules (compiles on first access, caches afterward)
    /// </summary>
    public async Task<CompiledRules?> GetCompiledRulesAsync(CancellationToken cancellationToken = default)
    {
        // Return cached rules if available
        if (_compiledRules != null)
        {
            return _compiledRules;
        }

        // Compile rules (thread-safe)
        await _compileLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_compiledRules != null)
            {
                return _compiledRules;
            }

            await CompileRulesAsync(cancellationToken);
            _lastCompiled = DateTimeOffset.UtcNow;

            return _compiledRules;
        }
        finally
        {
            _compileLock.Release();
        }
    }

    /// <summary>
    /// Force recompilation of rules (for hot-reload scenarios)
    /// </summary>
    public async Task RecompileRulesAsync(CancellationToken cancellationToken = default)
    {
        await _compileLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Forcing YARA rules recompilation");

            // Dispose old rules and context
            _compiledRules?.Dispose();
            _context?.Dispose();
            _compiledRules = null;
            _context = null;

            // Compile fresh
            await CompileRulesAsync(cancellationToken);
            _lastCompiled = DateTimeOffset.UtcNow;
        }
        finally
        {
            _compileLock.Release();
        }
    }

    /// <summary>
    /// Compile YARA rules from configured directory
    /// </summary>
    private async Task CompileRulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rulesPath = _config.Tier1.Yara.RulesPath;

            // Check if rules directory exists
            if (!Directory.Exists(rulesPath))
            {
                _logger.LogWarning("YARA rules directory does not exist: {RulesPath}. Creating it...", rulesPath);
                Directory.CreateDirectory(rulesPath);

                _logger.LogWarning("YARA rules directory is empty. YARA scanning will be non-functional until rules are added.");
                return;
            }

            // Find .yar files
            var ruleFiles = new List<string>();

            if (_config.Tier1.Yara.RuleFiles.Any())
            {
                // Use specific rule files from config
                foreach (var ruleFile in _config.Tier1.Yara.RuleFiles)
                {
                    var fullPath = Path.Combine(rulesPath, ruleFile);
                    if (File.Exists(fullPath))
                    {
                        ruleFiles.Add(fullPath);
                    }
                    else
                    {
                        _logger.LogWarning("Configured YARA rule file not found: {RuleFile}", fullPath);
                    }
                }
            }
            else
            {
                // Auto-discover all .yar and .yara files
                ruleFiles.AddRange(Directory.GetFiles(rulesPath, "*.yar", SearchOption.AllDirectories));
                ruleFiles.AddRange(Directory.GetFiles(rulesPath, "*.yara", SearchOption.AllDirectories));
            }

            if (!ruleFiles.Any())
            {
                _logger.LogWarning("No YARA rule files (.yar or .yara) found in {RulesPath}", rulesPath);
                return;
            }

            _logger.LogInformation("Compiling {Count} YARA rule files from {RulesPath}", ruleFiles.Count, rulesPath);

            // Create YARA context (required for all operations)
            _context = new YaraContext();

            // Compile rules using dnYara
            using var compiler = new Compiler();

            foreach (var ruleFile in ruleFiles)
            {
                try
                {
                    compiler.AddRuleFile(ruleFile);
                    _logger.LogDebug("Added YARA rule file: {RuleFile}", Path.GetFileName(ruleFile));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add YARA rule file: {RuleFile}", ruleFile);
                    // Continue with other rules
                }
            }

            _compiledRules = compiler.Compile();

            _logger.LogInformation("Successfully compiled YARA rules from {Count} files", ruleFiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compile YARA rules");
            _compiledRules = null;
            _context?.Dispose();
            _context = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _compiledRules?.Dispose();
        _context?.Dispose();
        _compileLock.Dispose();
        _disposed = true;
    }
}
