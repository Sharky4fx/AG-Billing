using System;
using System.Security.Cryptography;

namespace AGRechnung.FunctionApp.Security
{
    public static class PasswordHasher
    {
        public const string AlgorithmDescriptor = "PBKDF2-SHA256-100000";

        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100_000;

        public static (byte[] Hash, byte[] Salt, string Algorithm) HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password cannot be null or empty", nameof(password));
            }

            var salt = RandomNumberGenerator.GetBytes(SaltSize);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
            var hash = pbkdf2.GetBytes(HashSize);
            return (hash, salt, AlgorithmDescriptor);
        }

        public static bool VerifyPassword(string password, byte[] expectedHash, byte[] salt, string algorithm)
        {
            if (string.IsNullOrEmpty(password))
            {
                return false;
            }

            if (!string.Equals(algorithm, AlgorithmDescriptor, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Unsupported password hash algorithm '{algorithm}'.");
            }

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
            var computed = pbkdf2.GetBytes(HashSize);
            return CryptographicOperations.FixedTimeEquals(computed, expectedHash);
        }
    }
}
