using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Verification Token records
/// </summary>
public static class VerificationTokenMappings
{
    public static UiModels.VerificationToken ToModel(this DataModels.VerificationTokenDto data) => new(
        Id: data.Id,
        UserId: data.UserId,
        TokenType: (UiModels.TokenType)data.TokenType,
        Token: data.Token,
        Value: data.Value,
        ExpiresAt: data.ExpiresAt,
        CreatedAt: data.CreatedAt,
        UsedAt: data.UsedAt
    );

    public static DataModels.VerificationTokenDto ToDto(this UiModels.VerificationToken ui) => new()
    {
        Id = ui.Id,
        UserId = ui.UserId,
        TokenType = (DataModels.TokenType)(int)ui.TokenType,
        Token = ui.Token,
        Value = ui.Value,
        ExpiresAt = ui.ExpiresAt,
        CreatedAt = ui.CreatedAt,
        UsedAt = ui.UsedAt
    };
}
