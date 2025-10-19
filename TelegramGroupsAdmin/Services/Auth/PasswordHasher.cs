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
        var span = outputBytes.AsSpan();
        span[0] = 0x01; // Version marker
        salt.CopyTo(span.Slice(1, SaltSize));
        subkey.CopyTo(span.Slice(1 + SaltSize, Pbkdf2SubkeyLength));

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

            var decodedSpan = decodedHash.AsSpan();
            var salt = decodedSpan.Slice(1, SaltSize).ToArray();
            var expectedSubkey = decodedSpan.Slice(1 + SaltSize, Pbkdf2SubkeyLength).ToArray();

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
