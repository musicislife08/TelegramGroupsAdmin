using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace TelegramGroupsAdmin.Services.Auth;

public class PasswordHasher : IPasswordHasher
{
    private const int Pbkdf2IterationCount = 100000;
    private const int Pbkdf2SubkeyLength = 32;
    private const int SaltSize = 16;

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var subkey = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: Pbkdf2IterationCount,
            numBytesRequested: Pbkdf2SubkeyLength
        );

        var outputBytes = new byte[1 + SaltSize + Pbkdf2SubkeyLength];
        outputBytes[0] = 0x01; // Version marker
        Buffer.BlockCopy(salt, 0, outputBytes, 1, SaltSize);
        Buffer.BlockCopy(subkey, 0, outputBytes, 1 + SaltSize, Pbkdf2SubkeyLength);

        return Convert.ToBase64String(outputBytes);
    }

    public bool VerifyPassword(string password, string hashedPassword)
    {
        try
        {
            var decodedHash = Convert.FromBase64String(hashedPassword);

            if (decodedHash.Length != 1 + SaltSize + Pbkdf2SubkeyLength || decodedHash[0] != 0x01)
            {
                return false;
            }

            var salt = new byte[SaltSize];
            Buffer.BlockCopy(decodedHash, 1, salt, 0, SaltSize);

            var expectedSubkey = new byte[Pbkdf2SubkeyLength];
            Buffer.BlockCopy(decodedHash, 1 + SaltSize, expectedSubkey, 0, Pbkdf2SubkeyLength);

            var actualSubkey = KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: Pbkdf2IterationCount,
                numBytesRequested: Pbkdf2SubkeyLength
            );

            return CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey);
        }
        catch
        {
            return false;
        }
    }
}
