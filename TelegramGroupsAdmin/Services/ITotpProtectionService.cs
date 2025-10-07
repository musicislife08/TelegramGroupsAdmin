namespace TelegramGroupsAdmin.Services;

public interface ITotpProtectionService
{
    string Protect(string totpSecret);
    string Unprotect(string protectedTotpSecret);
}
