namespace TelegramGroupsAdmin.Ui.Server.Services.Auth;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hashedPassword);
}
