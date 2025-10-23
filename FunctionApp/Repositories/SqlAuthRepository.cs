using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace AGRechnung.FunctionApp.Repositories
{
    public class SqlAuthRepository : IAuthRepository
    {
        private readonly string _connectionString;

        public SqlAuthRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public async Task<bool> EmailExistsAsync(string email)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            const string sql = "SELECT COUNT(1) FROM auth.Users WHERE Email = @email";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@email", SqlDbType.NVarChar, 255) { Value = email });
            var result = await cmd.ExecuteScalarAsync();
            if (result == null || result == DBNull.Value) return false;
            return int.TryParse(result.ToString(), out var count) && count > 0;
        }

        public async Task<int> CreateUserWithVerificationTokenAsync(
            string email,
            byte[] passwordHash,
            byte[] passwordSalt,
            string passwordHashAlgorithm,
            string tokenHash,
            DateTime expiresAt)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var tx = conn.BeginTransaction();

            // Check
            const string checkSql = "SELECT COUNT(1) FROM auth.Users WHERE Email = @email";
            await using (var checkCmd = new SqlCommand(checkSql, conn, (SqlTransaction)tx))
            {
                checkCmd.Parameters.Add(new SqlParameter("@email", SqlDbType.NVarChar, 255) { Value = email });
                var existsObj = await checkCmd.ExecuteScalarAsync();
                if (existsObj != null && existsObj != DBNull.Value && int.TryParse(existsObj.ToString(), out var cnt) && cnt > 0)
                {
                    await tx.RollbackAsync();
                    throw new EmailAlreadyExistsException(email);
                }
            }

            // Insert user and get id
            const string insertUserSql = @"
                INSERT INTO auth.Users (Email, PasswordHash, PasswordSalt, PasswordHashAlgorithm)
                OUTPUT INSERTED.Id
                VALUES (@email, @passwordHash, @passwordSalt, @passwordHashAlgorithm);
                ";
            int newUserId;
            await using (var insertCmd = new SqlCommand(insertUserSql, conn, (SqlTransaction)tx))
            {
                insertCmd.Parameters.Add(new SqlParameter("@email", SqlDbType.NVarChar, 255) { Value = email });
                insertCmd.Parameters.Add(new SqlParameter("@passwordHash", SqlDbType.VarBinary, passwordHash.Length) { Value = passwordHash });
                insertCmd.Parameters.Add(new SqlParameter("@passwordSalt", SqlDbType.VarBinary, passwordSalt.Length) { Value = passwordSalt });
                insertCmd.Parameters.Add(new SqlParameter("@passwordHashAlgorithm", SqlDbType.NVarChar, 50) { Value = passwordHashAlgorithm });
                var insertedIdObj = await insertCmd.ExecuteScalarAsync();
                if (insertedIdObj == null || insertedIdObj == DBNull.Value || !int.TryParse(insertedIdObj.ToString(), out newUserId))
                {
                    await tx.RollbackAsync();
                    throw new Exception("Failed to create user");
                }
            }

            // Insert token (store hash)
            const string insertTokenSql = @"
                INSERT INTO auth.VerificationTokens (UserId, Token, ExpiresAt)
                VALUES (@userId, @token, @expiresAt);
                ";
            await using (var tokenCmd = new SqlCommand(insertTokenSql, conn, (SqlTransaction)tx))
            {
                tokenCmd.Parameters.Add(new SqlParameter("@userId", SqlDbType.Int) { Value = newUserId });
                tokenCmd.Parameters.Add(new SqlParameter("@token", SqlDbType.NVarChar, 255) { Value = tokenHash });
                tokenCmd.Parameters.Add(new SqlParameter("@expiresAt", SqlDbType.DateTime2) { Value = expiresAt });
                await tokenCmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return newUserId;
        }

        public async Task<UserCredentials?> GetUserCredentialsByEmailAsync(string email)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            const string sql = @"
SELECT Id, Email, PasswordHash, PasswordSalt, PasswordHashAlgorithm, VerifiedEmail, Active
FROM auth.Users
WHERE Email = @email;";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@email", SqlDbType.NVarChar, 255) { Value = email });

            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (!await reader.ReadAsync())
            {
                return null;
            }

            var userId = reader.GetInt32(0);
            var userEmail = reader.GetString(1);
            var passwordHash = (byte[])reader["PasswordHash"];
            var passwordSalt = (byte[])reader["PasswordSalt"];
            var algorithm = reader.GetString(4);
            var verified = reader.GetBoolean(5);
            var active = reader.GetBoolean(6);

            return new UserCredentials(userId, userEmail, passwordHash, passwordSalt, algorithm, verified, active);
        }

        public async Task<bool> VerifyEmailAsync(int userId, string tokenHash)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var tx = conn.BeginTransaction();

            try
            {
                // Check token validity and expiration
                const string checkTokenSql = @"
SELECT 1
FROM auth.VerificationTokens
WHERE UserId = @userId 
    AND Token = @tokenHash 
    AND ExpiresAt > SYSUTCDATETIME();";

                await using (var checkCmd = new SqlCommand(checkTokenSql, conn, tx))
                {
                    checkCmd.Parameters.Add(new SqlParameter("@userId", SqlDbType.Int) { Value = userId });
                    checkCmd.Parameters.Add(new SqlParameter("@tokenHash", SqlDbType.NVarChar, 255) { Value = tokenHash });

                    var result = await checkCmd.ExecuteScalarAsync();
                    if (result == null || result == DBNull.Value)
                    {
                        throw new InvalidVerificationTokenException();
                    }
                }

                // Update user's verified status
                const string updateUserSql = @"
UPDATE auth.Users 
SET VerifiedEmail = 1
WHERE Id = @userId;";

                await using (var updateCmd = new SqlCommand(updateUserSql, conn, tx))
                {
                    updateCmd.Parameters.Add(new SqlParameter("@userId", SqlDbType.Int) { Value = userId });
                    await updateCmd.ExecuteNonQueryAsync();
                }

                // Remove used token
                const string deleteTokenSql = @"
DELETE FROM auth.VerificationTokens
WHERE UserId = @userId;";

                await using (var deleteCmd = new SqlCommand(deleteTokenSql, conn, tx))
                {
                    deleteCmd.Parameters.Add(new SqlParameter("@userId", SqlDbType.Int) { Value = userId });
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                return true;
            }
            catch (Exception)
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<int> CleanupUnverifiedUsersAsync()
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var tx = conn.BeginTransaction();

            try
            {
                // First, get the list of users to be deleted (unverified with expired tokens)
                const string findUsersToDeleteSql = @"
SELECT u.Id
FROM auth.Users u
INNER JOIN auth.VerificationTokens vt ON u.Id = vt.UserId
WHERE u.VerifiedEmail = 0 
    AND vt.ExpiresAt < SYSUTCDATETIME();";

                var usersToDelete = new List<int>();
                await using (var cmd = new SqlCommand(findUsersToDeleteSql, conn, tx))
                {
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        usersToDelete.Add(reader.GetInt32(0));
                    }
                }

                if (usersToDelete.Count == 0)
                {
                    await tx.CommitAsync();
                    return 0;
                }

                // Due to cascade delete (configured in DB), deleting from Users will automatically
                // delete corresponding VerificationTokens
                const string deleteUsersSql = @"
DELETE FROM auth.Users 
WHERE Id = @userId AND VerifiedEmail = 0;";

                var deletedCount = 0;
                foreach (var userId in usersToDelete)
                {
                    await using var cmd = new SqlCommand(deleteUsersSql, conn, tx);
                    cmd.Parameters.Add(new SqlParameter("@userId", SqlDbType.Int) { Value = userId });
                    deletedCount += await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                return deletedCount;
            }
            catch (Exception)
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }
}
