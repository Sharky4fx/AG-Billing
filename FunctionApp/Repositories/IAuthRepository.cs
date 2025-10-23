using System;
using System.Threading.Tasks;

namespace AGRechnung.FunctionApp.Repositories
{
    public class EmailAlreadyExistsException : Exception
    {
        public EmailAlreadyExistsException(string email) : base($"Email already exists: {email}") { }
    }

    public class InvalidVerificationTokenException : Exception
    {
        public InvalidVerificationTokenException() : base("Invalid or expired verification token") { }
    }

    public interface IAuthRepository
    {
        Task<bool> EmailExistsAsync(string email);

        /// <summary>
        /// Creates a user, stores hashed password details, and creates a verification token atomically.
        /// Returns the new user id. Throws EmailAlreadyExistsException when email already exists.
        /// </summary>
        Task<int> CreateUserWithVerificationTokenAsync(
            string email,
            byte[] passwordHash,
            byte[] passwordSalt,
            string passwordHashAlgorithm,
            string tokenHash,
            DateTime expiresAt);

        /// <summary>
        /// Verifies a user's email using the provided token.
        /// Returns true on success, throws InvalidVerificationTokenException if token is invalid or expired.
        /// </summary>
        Task<bool> VerifyEmailAsync(int userId, string tokenHash);

        /// <summary>
        /// Deletes expired verification tokens and their associated unverified users.
        /// Returns the number of users cleaned up.
        /// </summary>
        Task<int> CleanupUnverifiedUsersAsync();
    }
}
