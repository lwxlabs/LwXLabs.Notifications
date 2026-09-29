using System;
using System.Security.Cryptography;
using System.Text;

namespace LwXLabs.Notifications.AspNetCore;

internal static class NotificationSecrets
{
    public static string Generate()
    {
        var bytes = new byte[32];
        using var random = RandomNumberGenerator.Create();
        random.GetBytes(bytes);
        return Base64Url(bytes);
    }

    public static string Hash(string value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    public static bool FixedEquals(string rawValue, string expectedHash)
    {
        var actual = Convert.FromBase64String(Hash(rawValue));
        var expected = Convert.FromBase64String(expectedHash);
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static bool FixedRawEquals(string left, string right)
    {
        var leftHash = Convert.FromBase64String(Hash(left ?? string.Empty));
        var rightHash = Convert.FromBase64String(Hash(right ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
