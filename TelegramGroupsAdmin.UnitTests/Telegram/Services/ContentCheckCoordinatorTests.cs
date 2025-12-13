using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using TelegramGroupsAdmin.ContentDetection.Constants;
using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using SpamLibRequest = TelegramGroupsAdmin.ContentDetection.Models.ContentCheckRequest;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services;

/// <summary>
/// Pure logical unit tests for ContentCheckCoordinator.
/// Uses NSubstitute for mocking - no database integration.
/// </summary>
[TestFixture]
public class ContentCheckCoordinatorTests
{
    private IContentDetectionEngine _mockSpamDetectionEngine = null!;
    private IServiceProvider _mockServiceProvider = null!;
    private ILogger<ContentCheckCoordinator> _mockLogger = null!;
    private ITelegramUserRepository _mockUserRepository = null!;
    private IChatAdminsRepository _mockChatAdminsRepository = null!;
    private IContentCheckConfigRepository _mockContentCheckConfigRepo = null!;
    private IServiceScope _mockScope = null!;
    private ContentCheckCoordinator _coordinator = null!;

    [SetUp]
    public void SetUp()
    {
        // Create mocks
        _mockSpamDetectionEngine = Substitute.For<IContentDetectionEngine>();
        _mockServiceProvider = Substitute.For<IServiceProvider>();
        _mockLogger = Substitute.For<ILogger<ContentCheckCoordinator>>();
        _mockUserRepository = Substitute.For<ITelegramUserRepository>();
        _mockChatAdminsRepository = Substitute.For<IChatAdminsRepository>();
        _mockContentCheckConfigRepo = Substitute.For<IContentCheckConfigRepository>();
        _mockScope = Substitute.For<IServiceScope>();

        // Create a mock scope service provider
        var mockScopeServiceProvider = Substitute.For<IServiceProvider>();

        // Setup scope service provider to return mocked repositories
        // REFACTOR-5: Use ITelegramUserRepository for trust checks (source of truth)
        mockScopeServiceProvider.GetService(typeof(ITelegramUserRepository))
            .Returns(_mockUserRepository);
        mockScopeServiceProvider.GetService(typeof(IChatAdminsRepository))
            .Returns(_mockChatAdminsRepository);
        mockScopeServiceProvider.GetService(typeof(IContentCheckConfigRepository))
            .Returns(_mockContentCheckConfigRepo);

        // Setup scope to return the mocked scope service provider
        _mockScope.ServiceProvider.Returns(mockScopeServiceProvider);

        // Setup IServiceScopeFactory (required by CreateScope() extension method)
        var mockScopeFactory = Substitute.For<IServiceScopeFactory>();
        mockScopeFactory.CreateScope().Returns(_mockScope);
        _mockServiceProvider.GetService(typeof(IServiceScopeFactory))
            .Returns(mockScopeFactory);

        // Create coordinator with mocked dependencies
        _coordinator = new ContentCheckCoordinator(
            _mockSpamDetectionEngine,
            _mockServiceProvider,
            _mockLogger);
    }

    [TearDown]
    public void TearDown()
    {
        _mockScope?.Dispose();
    }

    #region Service Account Tests (Critical - Race Condition Prevention)

    [Test]
    public async Task CheckAsync_ServiceAccount_SkipsAllChecks_NoRepositoryQueries()
    {
        // Arrange
        var request = new SpamLibRequest
        {
            UserId = TelegramConstants.ServiceAccountUserId, // 777000
            ChatId = -1001234567890,
            Message = "Channel post content"
        };

        // Act
        var result = await _coordinator.CheckAsync(request);

        // Assert
        Assert.Multiple(() =>
        {
            // Result validation
            Assert.That(result.IsUserTrusted, Is.True, "Service account should be marked as trusted");
            Assert.That(result.IsUserAdmin, Is.False, "Service account is not a chat admin");
            Assert.That(result.SpamCheckSkipped, Is.True, "Spam checks should be skipped");
            Assert.That(result.SkipReason, Does.Contain("Telegram service account"));
            Assert.That(result.CriticalCheckViolations, Is.Empty, "No violations for service account");
            Assert.That(result.SpamResult, Is.Null, "No spam detection should run");
        });

        // CRITICAL: Verify NO repository queries were made (race condition prevention)
        await _mockUserRepository.DidNotReceive()
            .IsTrustedAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _mockChatAdminsRepository.DidNotReceive()
            .IsAdminAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _mockContentCheckConfigRepo.DidNotReceive()
            .GetCriticalChecksAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());

        // Verify spam detection engine was NOT called
        await _mockSpamDetectionEngine.DidNotReceive()
            .CheckMessageAsync(Arg.Any<SpamLibRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckAsync_ServiceAccount_SkipsEvenWithCriticalChecksConfigured()
    {
        // Arrange - Even if critical checks exist, service account bypasses ALL checks
        var request = new SpamLibRequest
        {
            UserId = TelegramConstants.ServiceAccountUserId,
            ChatId = -1001234567890,
            Message = "https://malicious-link.com" // Would normally trigger URL check
        };

        // Act
        var result = await _coordinator.CheckAsync(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.SpamCheckSkipped, Is.True);
            Assert.That(result.SkipReason, Does.Contain("Telegram service account"));
            Assert.That(result.SpamResult, Is.Null, "No spam detection for service account");
        });

        // Verify NO critical checks query was made
        await _mockContentCheckConfigRepo.DidNotReceive()
            .GetCriticalChecksAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Trusted User Tests

    [Test]
    public async Task CheckAsync_TrustedUser_NoCriticalChecks_SkipsAllDetection()
    {
        // Arrange
        var request = new SpamLibRequest
        {
            UserId = 12345,
            ChatId = -1001234567890,
            Message = "Normal message"
        };

        _mockUserRepository.IsTrustedAsync(request.UserId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockChatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, Arg.Any<CancellationToken>())
            .Returns(false);
        _mockContentCheckConfigRepo.GetCriticalChecksAsync(request.ChatId, Arg.Any<CancellationToken>())
            .Returns([]);

        // Act
        var result = await _coordinator.CheckAsync(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsUserTrusted, Is.True);
            Assert.That(result.IsUserAdmin, Is.False);
            Assert.That(result.SpamCheckSkipped, Is.True);
            Assert.That(result.SkipReason, Does.Contain("trusted and no critical checks"));
            Assert.That(result.CriticalCheckViolations, Is.Empty);
            Assert.That(result.SpamResult, Is.Null);
        });

        // Verify spam detection was NOT called (early exit optimization)
        await _mockSpamDetectionEngine.DidNotReceive()
            .CheckMessageAsync(Arg.Any<SpamLibRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckAsync_TrustedUser_WithCriticalChecks_RunsDetection()
    {
        // Arrange
        var request = new SpamLibRequest
        {
            UserId = 12345,
            ChatId = -1001234567890,
            Message = "Normal message"
        };

        _mockUserRepository.IsTrustedAsync(request.UserId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockChatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Configure critical checks
        var criticalCheck = new ContentCheckConfig(
            Id: 1,
            ChatId: request.ChatId,
            CheckName: "URLCheck",
            Enabled: true,
            AlwaysRun: true,
            ConfidenceThreshold: null,
            ConfigurationJson: null,
            ModifiedDate: DateTimeOffset.UtcNow,
            ModifiedBy: "System"
        );
        _mockContentCheckConfigRepo.GetCriticalChecksAsync(request.ChatId, Arg.Any<CancellationToken>())
            .Returns([criticalCheck]);

        // Mock detection result with NO violations
        var detectionResult = new ContentDetectionResult
        {
            IsSpam = false,
            MaxConfidence = 0,
            AvgConfidence = 0,
            SpamFlags = 0,
            NetConfidence = 0,
            CheckResults = []
        };
        _mockSpamDetectionEngine.CheckMessageAsync(
                Arg.Is<SpamLibRequest>(r => r.IsUserTrusted && r.UserId == request.UserId),
                Arg.Any<CancellationToken>())
            .Returns(detectionResult);

        // Act
        var result = await _coordinator.CheckAsync(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsUserTrusted, Is.True);
            Assert.That(result.SpamCheckSkipped, Is.True, "Regular checks skipped after critical checks pass");
            Assert.That(result.SkipReason, Does.Contain("critical checks passed"));
            Assert.That(result.CriticalCheckViolations, Is.Empty);
            Assert.That(result.SpamResult, Is.Null, "Regular spam result not evaluated");
        });

        // Verify detection WAS called (to run critical checks)
        await _mockSpamDetectionEngine.Received(1)
            .CheckMessageAsync(Arg.Any<SpamLibRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckAsync_TrustedUser_CriticalCheckViolation_ReturnsFullResult()
    {
        // Arrange
        var request = new SpamLibRequest
        {
            UserId = 12345,
            ChatId = -1001234567890,
            Message = "https://malicious-link.com"
        };

        _mockUserRepository.IsTrustedAsync(request.UserId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockChatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Configure critical URL check
        var criticalCheck = new ContentCheckConfig(
            Id: 1,
            ChatId: request.ChatId,
            CheckName: nameof(CheckName.UrlBlocklist), // "UrlBlocklist"
            Enabled: true,
            AlwaysRun: true,
            ConfidenceThreshold: null,
            ConfigurationJson: null,
            ModifiedDate: DateTimeOffset.UtcNow,
            ModifiedBy: "System"
        );
        _mockContentCheckConfigRepo.GetCriticalChecksAsync(request.ChatId, Arg.Any<CancellationToken>())
            .Returns([criticalCheck]);

        // Mock detection result WITH violation
        var detectionResult = new ContentDetectionResult
        {
            IsSpam = true,
            MaxConfidence = 95,
            AvgConfidence = 95,
            SpamFlags = 1,
            NetConfidence = 95,
            CheckResults =
            [
                new ContentCheckResponse
                {
                    CheckName = CheckName.UrlBlocklist,
                    Result = CheckResultType.Spam,
                    Details = "Malicious URL detected: malicious-link.com",
                    Confidence = 95
                }
            ]
        };
        _mockSpamDetectionEngine.CheckMessageAsync(Arg.Any<SpamLibRequest>(), Arg.Any<CancellationToken>())
            .Returns(detectionResult);

        // Act
        var result = await _coordinator.CheckAsync(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsUserTrusted, Is.True);
            Assert.That(result.SpamCheckSkipped, Is.False, "Full results returned due to critical violation");
            Assert.That(result.CriticalCheckViolations, Has.Count.EqualTo(1));
            Assert.That(result.CriticalCheckViolations[0], Does.Contain("UrlBlocklist"));
            Assert.That(result.SpamResult, Is.Not.Null, "Full spam result included");
        });
    }

    #endregion

    #region Admin User Tests

    [Test]
    public async Task CheckAsync_AdminUser_NoCriticalChecks_SkipsAllDetection()
    {
        // Arrange
        var request = new SpamLibRequest
        {
            UserId = 12345,
            ChatId = -1001234567890,
            Message = "Admin message"
        };

        _mockUserRepository.IsTrustedAsync(request.UserId, Arg.Any<CancellationToken>())
            .Returns(false);
        _mockChatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockContentCheckConfigRepo.GetCriticalChecksAsync(request.ChatId, Arg.Any<CancellationToken>())
            .Returns([]);

        // Act
        var result = await _coordinator.CheckAsync(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsUserTrusted, Is.False);
            Assert.That(result.IsUserAdmin, Is.True);
            Assert.That(result.SpamCheckSkipped, Is.True);
            Assert.That(result.SkipReason, Does.Contain("admin and no critical checks"));
            Assert.That(result.SpamResult, Is.Null);
        });

        // Verify spam detection was NOT called
        await _mockSpamDetectionEngine.DidNotReceive()
            .CheckMessageAsync(Arg.Any<SpamLibRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckAsync_AdminUser_CriticalChecksPassed_SkipsRegularChecks()
    {
        // Arrange
        var request = new SpamLibRequest
        {
            UserId = 12345,
            ChatId = -1001234567890,
            Message = "Admin message"
        };

        _mockUserRepository.IsTrustedAsync(request.UserId, Arg.Any<CancellationToken>())
            .Returns(false);
        _mockChatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, Arg.Any<CancellationToken>())
            .Returns(true);

        var criticalCheck = new ContentCheckConfig(
            Id: 1,
            ChatId: request.ChatId,
            CheckName: "URLCheck",
            Enabled: true,
            AlwaysRun: true,
            ConfidenceThreshold: null,
            ConfigurationJson: null,
            ModifiedDate: DateTimeOffset.UtcNow,
            ModifiedBy: "System"
        );
        _mockContentCheckConfigRepo.GetCriticalChecksAsync(request.ChatId, Arg.Any<CancellationToken>())
            .Returns([criticalCheck]);

        // Mock clean result (no violations)
        var detectionResult = new ContentDetectionResult
        {
            IsSpam = false,
            MaxConfidence = 0,
            CheckResults = []
        };
        _mockSpamDetectionEngine.CheckMessageAsync(Arg.Any<SpamLibRequest>(), Arg.Any<CancellationToken>())
            .Returns(detectionResult);

        // Act
        var result = await _coordinator.CheckAsync(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsUserAdmin, Is.True);
            Assert.That(result.SpamCheckSkipped, Is.True);
            Assert.That(result.SkipReason, Does.Contain("chat admin"));
            Assert.That(result.SkipReason, Does.Contain("critical checks passed"));
        });
    }

    #endregion

    #region Untrusted User Tests

    [Test]
    public async Task CheckAsync_UntrustedUser_RunsFullDetection()
    {
        // Arrange
        var request = new SpamLibRequest
        {
            UserId = 12345,
            ChatId = -1001234567890,
            Message = "Suspicious message"
        };

        _mockUserRepository.IsTrustedAsync(request.UserId, Arg.Any<CancellationToken>())
            .Returns(false);
        _mockChatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, Arg.Any<CancellationToken>())
            .Returns(false);
        _mockContentCheckConfigRepo.GetCriticalChecksAsync(request.ChatId, Arg.Any<CancellationToken>())
            .Returns([]);

        var detectionResult = new ContentDetectionResult
        {
            IsSpam = true,
            MaxConfidence = 85,
            AvgConfidence = 85,
            SpamFlags = 1,
            NetConfidence = 85,
            CheckResults =
            [
                new ContentCheckResponse
                {
                    CheckName = CheckName.Bayes,
                    Result = CheckResultType.Spam,
                    Details = "Spam pattern detected",
                    Confidence = 85
                }
            ]
        };
        _mockSpamDetectionEngine.CheckMessageAsync(Arg.Any<SpamLibRequest>(), Arg.Any<CancellationToken>())
            .Returns(detectionResult);

        // Act
        var result = await _coordinator.CheckAsync(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsUserTrusted, Is.False);
            Assert.That(result.IsUserAdmin, Is.False);
            Assert.That(result.SpamCheckSkipped, Is.False);
            Assert.That(result.SkipReason, Is.Null);
            Assert.That(result.SpamResult, Is.Not.Null);
            Assert.That(result.SpamResult!.IsSpam, Is.True);
        });

        // Verify detection WAS called
        await _mockSpamDetectionEngine.Received(1)
            .CheckMessageAsync(Arg.Any<SpamLibRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckAsync_UntrustedUser_PassesTrustContextToEngine()
    {
        // Arrange
        var request = new SpamLibRequest
        {
            UserId = 12345,
            ChatId = -1001234567890,
            Message = "Message",
            IsUserTrusted = false, // Initially false
            IsUserAdmin = false
        };

        _mockUserRepository.IsTrustedAsync(request.UserId, Arg.Any<CancellationToken>())
            .Returns(false);
        _mockChatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, Arg.Any<CancellationToken>())
            .Returns(false);
        _mockContentCheckConfigRepo.GetCriticalChecksAsync(request.ChatId, Arg.Any<CancellationToken>())
            .Returns([]);

        var detectionResult = new ContentDetectionResult { IsSpam = false };
        _mockSpamDetectionEngine.CheckMessageAsync(Arg.Any<SpamLibRequest>(), Arg.Any<CancellationToken>())
            .Returns(detectionResult);

        // Act
        await _coordinator.CheckAsync(request);

        // Assert - Verify enriched request was passed to engine
        await _mockSpamDetectionEngine.Received(1)
            .CheckMessageAsync(
                Arg.Is<SpamLibRequest>(r =>
                    r.UserId == request.UserId &&
                    r.IsUserTrusted == false &&
                    r.IsUserAdmin == false),
                Arg.Any<CancellationToken>());
    }

    #endregion

    #region Critical Check Filtering Tests

    [Test]
    public async Task CheckAsync_MultipleCriticalChecks_OnlyFlagsEnabledAlwaysRun()
    {
        // Arrange
        var request = new SpamLibRequest
        {
            UserId = 12345,
            ChatId = -1001234567890,
            Message = "Test"
        };

        _mockUserRepository.IsTrustedAsync(request.UserId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockChatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Mix of enabled/disabled and always_run/normal checks
        var checks = new List<ContentCheckConfig>
        {
            new(1, request.ChatId, "URLCheck", true, true, null, null, DateTimeOffset.UtcNow, "System"),       // Should count
            new(2, request.ChatId, "BayesianClassifier", true, false, null, null, DateTimeOffset.UtcNow, "System"), // Should NOT count
            new(3, request.ChatId, "ImageSpam", false, true, null, null, DateTimeOffset.UtcNow, "System"),     // Should NOT count (disabled)
            new(4, request.ChatId, "CASBan", true, true, null, null, DateTimeOffset.UtcNow, "System")          // Should count
        };
        _mockContentCheckConfigRepo.GetCriticalChecksAsync(request.ChatId, Arg.Any<CancellationToken>())
            .Returns(checks);

        var detectionResult = new ContentDetectionResult
        {
            IsSpam = false,
            CheckResults = []
        };
        _mockSpamDetectionEngine.CheckMessageAsync(Arg.Any<SpamLibRequest>(), Arg.Any<CancellationToken>())
            .Returns(detectionResult);

        // Act
        var result = await _coordinator.CheckAsync(request);

        // Assert - Only 2 critical checks should be identified (URLCheck, CASBan)
        Assert.That(result.SpamCheckSkipped, Is.True, "Trusted user with no critical violations should skip regular checks");
    }

    [Test]
    public async Task CheckAsync_CriticalCheckViolation_IncludesDetailsInViolationList()
    {
        // Arrange
        var request = new SpamLibRequest
        {
            UserId = 12345,
            ChatId = -1001234567890,
            Message = "Test"
        };

        _mockUserRepository.IsTrustedAsync(request.UserId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockChatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, Arg.Any<CancellationToken>())
            .Returns(false);

        var criticalCheck = new ContentCheckConfig(
            Id: 1,
            ChatId: request.ChatId,
            CheckName: nameof(CheckName.UrlBlocklist), // "UrlBlocklist"
            Enabled: true,
            AlwaysRun: true,
            ConfidenceThreshold: null,
            ConfigurationJson: null,
            ModifiedDate: DateTimeOffset.UtcNow,
            ModifiedBy: "System"
        );
        _mockContentCheckConfigRepo.GetCriticalChecksAsync(request.ChatId, Arg.Any<CancellationToken>())
            .Returns([criticalCheck]);

        var detectionResult = new ContentDetectionResult
        {
            IsSpam = true,
            MaxConfidence = 98,
            CheckResults =
            [
                new ContentCheckResponse
                {
                    CheckName = CheckName.UrlBlocklist,
                    Result = CheckResultType.Spam,
                    Details = "Phishing site detected: evil.com",
                    Confidence = 98
                }
            ]
        };
        _mockSpamDetectionEngine.CheckMessageAsync(Arg.Any<SpamLibRequest>(), Arg.Any<CancellationToken>())
            .Returns(detectionResult);

        // Act
        var result = await _coordinator.CheckAsync(request);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.CriticalCheckViolations, Has.Count.EqualTo(1));
            Assert.That(result.CriticalCheckViolations[0], Does.Contain("UrlBlocklist"));
            Assert.That(result.CriticalCheckViolations[0], Does.Contain("Phishing site detected"));
        });
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task CheckAsync_BothTrustedAndAdmin_UsesTrustedInSkipReason()
    {
        // Arrange - User is both trusted AND admin
        var request = new SpamLibRequest
        {
            UserId = 12345,
            ChatId = -1001234567890,
            Message = "Message"
        };

        _mockUserRepository.IsTrustedAsync(request.UserId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockChatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockContentCheckConfigRepo.GetCriticalChecksAsync(request.ChatId, Arg.Any<CancellationToken>())
            .Returns([]);

        // Act
        var result = await _coordinator.CheckAsync(request);

        // Assert - Should prioritize "trusted" in skip reason
        Assert.Multiple(() =>
        {
            Assert.That(result.IsUserTrusted, Is.True);
            Assert.That(result.IsUserAdmin, Is.True);
            Assert.That(result.SkipReason, Does.Contain("trusted"), "Should mention trusted status");
        });
    }

    [Test]
    public async Task CheckAsync_NullCheckResults_HandlesGracefully()
    {
        // Arrange
        var request = new SpamLibRequest
        {
            UserId = 12345,
            ChatId = -1001234567890,
            Message = "Test"
        };

        _mockUserRepository.IsTrustedAsync(request.UserId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockChatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, Arg.Any<CancellationToken>())
            .Returns(false);

        var criticalCheck = new ContentCheckConfig(
            Id: 1,
            ChatId: request.ChatId,
            CheckName: "URLCheck",
            Enabled: true,
            AlwaysRun: true,
            ConfidenceThreshold: null,
            ConfigurationJson: null,
            ModifiedDate: DateTimeOffset.UtcNow,
            ModifiedBy: "System"
        );
        _mockContentCheckConfigRepo.GetCriticalChecksAsync(request.ChatId, Arg.Any<CancellationToken>())
            .Returns([criticalCheck]);

        // Return result with null CheckResults (testing defensive coding)
        var detectionResult = new ContentDetectionResult
        {
            IsSpam = false,
            CheckResults = null! // Force null for testing
        };
        _mockSpamDetectionEngine.CheckMessageAsync(Arg.Any<SpamLibRequest>(), Arg.Any<CancellationToken>())
            .Returns(detectionResult);

        // Act & Assert - Should not throw
        var result = await _coordinator.CheckAsync(request);
        Assert.That(result.CriticalCheckViolations, Is.Empty, "Should handle null CheckResults gracefully");
    }

    [Test]
    public async Task CheckAsync_CaseInsensitiveCheckNameMatching()
    {
        // Arrange
        var request = new SpamLibRequest
        {
            UserId = 12345,
            ChatId = -1001234567890,
            Message = "Test"
        };

        _mockUserRepository.IsTrustedAsync(request.UserId, Arg.Any<CancellationToken>())
            .Returns(true);
        _mockChatAdminsRepository.IsAdminAsync(request.ChatId, request.UserId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Config has "urlblocklist" (lowercase) - should match "UrlBlocklist" case-insensitively
        var criticalCheck = new ContentCheckConfig(
            Id: 1,
            ChatId: request.ChatId,
            CheckName: "urlblocklist",
            Enabled: true,
            AlwaysRun: true,
            ConfidenceThreshold: null,
            ConfigurationJson: null,
            ModifiedDate: DateTimeOffset.UtcNow,
            ModifiedBy: "System"
        );
        _mockContentCheckConfigRepo.GetCriticalChecksAsync(request.ChatId, Arg.Any<CancellationToken>())
            .Returns([criticalCheck]);

        // Detection returns "UrlBlocklist" (different case from config)
        var detectionResult = new ContentDetectionResult
        {
            IsSpam = true,
            MaxConfidence = 90,
            CheckResults =
            [
                new ContentCheckResponse
                {
                    CheckName = CheckName.UrlBlocklist, // "UrlBlocklist"
                    Result = CheckResultType.Spam,
                    Details = "Test",
                    Confidence = 90
                }
            ]
        };
        _mockSpamDetectionEngine.CheckMessageAsync(Arg.Any<SpamLibRequest>(), Arg.Any<CancellationToken>())
            .Returns(detectionResult);

        // Act
        var result = await _coordinator.CheckAsync(request);

        // Assert - Should match case-insensitively
        Assert.That(result.CriticalCheckViolations, Has.Count.EqualTo(1),
            "Should match check names case-insensitively");
    }

    #endregion
}
