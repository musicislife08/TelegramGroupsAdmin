/*
 * Manual Integration Test for File Scanning
 *
 * This file demonstrates how to manually test the YARA scanner
 * using the YaraRuleManager and YaraScannerService.
 *
 * To run this test:
 * 1. Add this code to a test project or console app
 * 2. Set YARA rules path to: /Users/keisenmenger/Repos/personal/TelegramGroupsAdmin/yara-rules
 * 3. Load test files from: ./test-files/
 *
 * Expected Results:
 * - eicar.com: Detected by EICAR_Test_File rule
 * - fake_stealer.txt: Detected by Crypto_Wallet_Path_Targeting rule
 * - clean.txt: No detections (clean file)
 */

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.ContentDetection.Services;

public class FileScanningIntegrationTest
{
    public static async Task Main()
    {
        Console.WriteLine("=== File Scanning Integration Test ===\n");

        // Setup configuration
        var config = Options.Create(new FileScanningConfig
        {
            Tier1 = new Tier1Config
            {
                Yara = new YaraConfig
                {
                    Enabled = true,
                    RulesPath = "/Users/keisenmenger/Repos/personal/TelegramGroupsAdmin/yara-rules",
                    TimeoutSeconds = 10
                }
            }
        });

        // Create logger factory (console output)
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var yaraLogger = loggerFactory.CreateLogger<YaraScannerService>();
        var managerLogger = loggerFactory.CreateLogger<YaraRuleManager>();

        try
        {
            // Initialize YARA rule manager
            Console.WriteLine("Initializing YARA rule manager...");
            var ruleManager = new YaraRuleManager(config, managerLogger);
            await ruleManager.InitializeAsync(CancellationToken.None);

            // Initialize YARA scanner service
            var yaraScanner = new YaraScannerService(config, ruleManager, yaraLogger);

            Console.WriteLine($"\nYARA rules compiled successfully from: {config.Value.Tier1.Yara.RulesPath}\n");

            // Test files
            var testFiles = new[]
            {
                ("./test-files/eicar.com", "EICAR Test File"),
                ("./test-files/fake_stealer.txt", "Fake Crypto Stealer"),
                ("./test-files/clean.txt", "Clean Text File")
            };

            foreach (var (filePath, description) in testFiles)
            {
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"⚠️  {description}: File not found at {filePath}");
                    continue;
                }

                Console.WriteLine($"Testing: {description}");
                Console.WriteLine($"File: {filePath}");

                var fileBytes = await File.ReadAllBytesAsync(filePath);
                var result = await yaraScanner.ScanFileAsync(fileBytes, Path.GetFileName(filePath), CancellationToken.None);

                if (result.IsClean)
                {
                    Console.WriteLine($"✅ Result: CLEAN (no threats detected)");
                }
                else
                {
                    Console.WriteLine($"🚨 Result: THREAT DETECTED");
                    Console.WriteLine($"   Scanner: {result.Scanner}");
                    Console.WriteLine($"   Threat: {result.ThreatName}");
                    Console.WriteLine($"   Duration: {result.ScanDurationMs}ms");
                }

                Console.WriteLine();
            }

            Console.WriteLine("=== Test Complete ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Error: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }
    }
}
