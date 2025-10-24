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

    public record UserCredentials(
        int UserId,
        Guid Uuid,
        string Email,
        byte[] PasswordHash,
        byte[] PasswordSalt,
        string PasswordHashAlgorithm,
        bool VerifiedEmail,
        bool Active);

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
        /// Retrieves stored credentials for a user by email. Returns null when no user exists.
        /// </summary>
        Task<UserCredentials?> GetUserCredentialsByEmailAsync(string email);

        /// <summary>
        /// Verifies a user's email using the provided token.
        /// Returns true on success, throws InvalidVerificationTokenException if token is invalid or expired.
        /// </summary>
        Task<bool> VerifyEmailAsync(Guid userUuid, string tokenHash);

        /// <summary>
        /// Deletes expired verification tokens and their associated unverified users.
        /// Returns the number of users cleaned up.
        /// </summary>
        Task<int> CleanupUnverifiedUsersAsync();

        /// <summary>
        /// Gets the user UUID and existing verification token (if any) for resending.
        /// Returns tuple with (uuid, existingTokenHash, expiresAt) or null if user not found or already verified.
        /// </summary>
        Task<(Guid Uuid, string TokenHash, DateTime ExpiresAt)?> GetVerificationTokenForResendAsync(string email);

        /// <summary>
        /// Updates or creates a verification token for an existing unverified user.
        /// Used when resending verification emails.
        /// </summary>
        Task UpdateVerificationTokenAsync(string email, string newTokenHash, DateTime expiresAt);
    }
}
