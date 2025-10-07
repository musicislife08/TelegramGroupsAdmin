using Microsoft.AspNetCore.DataProtection;

namespace TelegramGroupsAdmin.Services;

public class TotpProtectionService : ITotpProtectionService
{
    private readonly IDataProtector _protector;

    public TotpProtectionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("TgSpamPreFilter.TotpSecrets");
    }

    public string Protect(string totpSecret)
    {
        if (string.IsNullOrEmpty(totpSecret))
        {
            throw new ArgumentException("TOTP secret cannot be null or empty", nameof(totpSecret));
        }

        return _protector.Protect(totpSecret);
    }

    public string Unprotect(string protectedTotpSecret)
    {
        if (string.IsNullOrEmpty(protectedTotpSecret))
        {
            throw new ArgumentException("Protected TOTP secret cannot be null or empty", nameof(protectedTotpSecret));
        }

        return _protector.Unprotect(protectedTotpSecret);
    }
}
