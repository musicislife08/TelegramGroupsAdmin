using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Auth;

namespace TelegramGroupsAdmin.Services;

internal static class BootstrapOwnerService
{
    public record BootstrapResult(bool Success, string Message);

    public static async Task<BootstrapResult> ExecuteAsync(
        string? filePath,
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IAuditService auditService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        // Implementation will come in GREEN step
        throw new NotImplementedException();
    }
}
