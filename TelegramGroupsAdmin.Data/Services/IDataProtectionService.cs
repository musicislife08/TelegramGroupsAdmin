namespace TelegramGroupsAdmin.Data.Services;

public interface IDataProtectionService
{
    string Protect(string totpSecret);
    string Unprotect(string protectedTotpSecret);
}
