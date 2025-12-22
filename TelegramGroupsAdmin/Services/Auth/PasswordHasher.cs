using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using TelegramGroupsAdmin.Constants;

namespace TelegramGroupsAdmin.Services.Auth;

public class PasswordHasher : IPasswordHasher
{

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(PasswordHashingConstants.SaltSize);
        var subkey = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: PasswordHashingConstants.Pbkdf2IterationCount,
            numBytesRequested: PasswordHashingConstants.Pbkdf2SubkeyLength
        );

        var outputBytes = new byte[PasswordHashingConstants.TotalHashSize];
        var span = outputBytes.AsSpan();
        span[0] = PasswordHashingConstants.VersionMarker;
        salt.CopyTo(span.Slice(1, PasswordHashingConstants.SaltSize));
        subkey.CopyTo(span.Slice(1 + PasswordHashingConstants.SaltSize, PasswordHashingConstants.Pbkdf2SubkeyLength));

        return Convert.ToBase64String(outputBytes);
    }

    public bool VerifyPassword(string password, string hashedPassword)
    {
        try
        {
            var decodedHash = Convert.FromBase64String(hashedPassword);

            if (decodedHash.Length != PasswordHashingConstants.TotalHashSize || decodedHash[0] != PasswordHashingConstants.VersionMarker)
            {
                return false;
            }

            var decodedSpan = decodedHash.AsSpan();
            var salt = decodedSpan.Slice(1, PasswordHashingConstants.SaltSize).ToArray();
            var expectedSubkey = decodedSpan.Slice(1 + PasswordHashingConstants.SaltSize, PasswordHashingConstants.Pbkdf2SubkeyLength).ToArray();

            var actualSubkey = KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: PasswordHashingConstants.Pbkdf2IterationCount,
                numBytesRequested: PasswordHashingConstants.Pbkdf2SubkeyLength
            );

            return CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey);
        }
        catch
        {
            return false;
        }
    }
}
