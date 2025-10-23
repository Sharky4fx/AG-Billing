using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
// using Microsoft.Data.SqlClient; (not needed - repository handles DB access)

namespace AGRechnung.CreateNewUser;

public class CreateNewUser
{
    private readonly ILogger<CreateNewUser> _logger;
    private readonly AGRechnung.FunctionApp.Repositories.IAuthRepository _repo;

    public CreateNewUser(ILogger<CreateNewUser> logger, AGRechnung.FunctionApp.Repositories.IAuthRepository repo)
    {
        _logger = logger;
        _repo = repo;
    }

    [Function("CreateNewUser")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("CreateNewUser function processed a request.");

        // Read email from form or query (prefer body/form for POST)
        string? email = req.Query["email"].ToString();
        if (string.IsNullOrWhiteSpace(email) && req.HasFormContentType)
        {
            email = req.Form["email"].ToString();
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return new BadRequestObjectResult(new { error = "Missing 'email' parameter" });
        }

        var emailValidator = new EmailAddressAttribute();
        if (!emailValidator.IsValid(email))
        {
            return new BadRequestObjectResult(new { error = "Invalid email format" });
        }

        // Generate secure random token (URL-safe base64)
        var token = GenerateUrlSafeToken(32);
        var tokenHash = HashToken(token);
        var expiresAt = DateTime.UtcNow.AddHours(24);

        try
        {
            var newUserId = await _repo.CreateUserWithVerificationTokenAsync(email, tokenHash, expiresAt);
            // Log raw token (will later be sent via Azure Communication)
            _logger.LogInformation("Verification token for {email}: {token}", email, token);
            return new ObjectResult(new { created = true, userId = newUserId }) { StatusCode = StatusCodes.Status201Created };
        }
        catch (AGRechnung.FunctionApp.Repositories.EmailAlreadyExistsException)
        {
            return new ConflictObjectResult(new { error = "Email already exists" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user");
            return new ObjectResult(new { error = "Internal server error" }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    private static string GenerateUrlSafeToken(int bytesLength)
    {
        var bytes = new byte[bytesLength];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        // Base64 URL-safe
        var token = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return token;
    }

    private static string HashToken(string token)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha.ComputeHash(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}