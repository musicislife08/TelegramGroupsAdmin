using Microsoft.AspNetCore.DataProtection;

namespace TelegramGroupsAdmin.Data.Services;

public class TotpProtectionService : ITotpProtectionService
{
    private readonly IDataProtector _protector;

    public TotpProtectionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("TgSpamPreFilter.TotpSecrets");
    }

    public string Protect(string totpSecret)
    {
        ArgumentException.ThrowIfNullOrEmpty(totpSecret);

        return _protector.Protect(totpSecret);
    }

    public string Unprotect(string protectedTotpSecret)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedTotpSecret);

        return _protector.Unprotect(protectedTotpSecret);
    }
}
