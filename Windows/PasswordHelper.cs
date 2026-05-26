using System.Security.Cryptography;
using System.Text;

namespace TimeTracker
{
    public static class PasswordHelper
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100_000;

        public static string Hash(string? pwd)
        {
            var bytes = Encoding.UTF8.GetBytes(pwd ?? "");
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(bytes, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
            return $"{Convert.ToHexString(salt)}:{Convert.ToHexString(hash)}";
        }

        public static bool Verify(string? pwd, string stored)
        {
            var parts = stored.Split(':');
            if (parts.Length != 2) return false;
            try
            {
                var salt = Convert.FromHexString(parts[0]);
                var expected = Convert.FromHexString(parts[1]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(pwd ?? ""), salt, Iterations, HashAlgorithmName.SHA256, HashSize);
                return CryptographicOperations.FixedTimeEquals(expected, actual);
            }
            catch (Exception ex)
            {
                Logger.Error("Password verification error", ex);
                return false;
            }
        }
    }
}
