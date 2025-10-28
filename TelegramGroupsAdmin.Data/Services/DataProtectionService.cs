using Microsoft.AspNetCore.DataProtection;

namespace TelegramGroupsAdmin.Data.Services;

public class DataProtectionService : IDataProtectionService
{
    private readonly IDataProtector _protector;

    public DataProtectionService(IDataProtectionProvider provider)
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
