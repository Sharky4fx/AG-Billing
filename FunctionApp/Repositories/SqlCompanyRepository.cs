using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace AGRechnung.FunctionApp.Repositories
{
    public class SqlCompanyRepository : ICompanyRepository
    {
        private readonly string _connectionString;

        public SqlCompanyRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        public async Task<int> CreateCompanyAsync(
            int userId,
            string name,
            string? vatNumber,
            string street,
            string postalCode,
            string city,
            string? country)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            const string sql = @"
INSERT INTO core.Companies (UserId, Name, VatNumber, Street, PostalCode, City, Country)
OUTPUT INSERTED.Id
VALUES (@userId, @name, @vatNumber, @street, @postalCode, @city, @country);";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@userId", SqlDbType.Int) { Value = userId });
            cmd.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 255) { Value = name });
            cmd.Parameters.Add(new SqlParameter("@vatNumber", SqlDbType.NVarChar, 50) { Value = (object?)vatNumber ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@street", SqlDbType.NVarChar, 255) { Value = street });
            cmd.Parameters.Add(new SqlParameter("@postalCode", SqlDbType.NVarChar, 20) { Value = postalCode });
            cmd.Parameters.Add(new SqlParameter("@city", SqlDbType.NVarChar, 100) { Value = city });
            cmd.Parameters.Add(new SqlParameter("@country", SqlDbType.NVarChar, 100) { Value = (object?)country ?? DBNull.Value });

            var result = await cmd.ExecuteScalarAsync();
            if (result == null || result == DBNull.Value)
            {
                throw new Exception("Failed to create company");
            }

            return Convert.ToInt32(result);
        }
    }
}
