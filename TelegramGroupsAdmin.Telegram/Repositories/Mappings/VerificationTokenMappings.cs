using DataModels = TelegramGroupsAdmin.Data.Models;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories.Mappings;

/// <summary>
/// Mapping extensions for Verification Token records
/// </summary>
public static class VerificationTokenMappings
{
    extension(DataModels.VerificationTokenDto data)
    {
        public UiModels.VerificationToken ToModel() => new(
            Id: data.Id,
            UserId: data.UserId,
            TokenType: (UiModels.TokenType)data.TokenType,
            Token: data.Token,
            Value: data.Value,
            ExpiresAt: data.ExpiresAt,
            CreatedAt: data.CreatedAt,
            UsedAt: data.UsedAt
        );
    }

    extension(UiModels.VerificationToken ui)
    {
        public DataModels.VerificationTokenDto ToDto() => new()
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
}
