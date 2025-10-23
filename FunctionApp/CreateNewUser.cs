using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AGRechnung.FunctionApp.Security;
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

    private record CreateUserRequest(string? Email, string? Password);

    public CreateNewUser(ILogger<CreateNewUser> logger, AGRechnung.FunctionApp.Repositories.IAuthRepository repo)
    {
        _logger = logger;
        _repo = repo;
    }

    [Function("CreateNewUser")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("CreateNewUser function processed a request.");

        string? email = null;
        string? password = null;

        if (req.HasFormContentType)
        {
            var form = await req.ReadFormAsync();
            email = form["email"].ToString();
            password = form["password"].ToString();
        }
        else if (IsJsonContentType(req.ContentType))
        {
            var payload = await DeserializeBodyAsync(req);
            email = payload?.Email;
            password = payload?.Password;
        }

        email ??= req.Query["email"].ToString();
        password ??= req.Query["password"].ToString();

        if (string.IsNullOrWhiteSpace(email))
        {
            return new BadRequestObjectResult(new { error = "Missing 'email' value" });
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return new BadRequestObjectResult(new { error = "Missing 'password' value" });
        }

        if (password.Length < 8)
        {
            return new BadRequestObjectResult(new { error = "Password must be at least 8 characters" });
        }

        // Normalize input email for validation and uniqueness checks
        email = email.Trim();

        var emailValidator = new EmailAddressAttribute();
        if (!emailValidator.IsValid(email))
        {
            return new BadRequestObjectResult(new { error = "Invalid email format" });
        }

        // Best-practice normalization for uniqueness: lowercase and trimmed
        var normalizedEmail = email.ToLowerInvariant();

        var (passwordHash, passwordSalt, passwordAlgorithm) = PasswordHasher.HashPassword(password);

        // Generate secure random token (URL-safe base64)
        var token = GenerateUrlSafeToken(32);
        var tokenHash = HashToken(token);
        var expiresAt = DateTime.UtcNow.AddHours(24);

        try
        {
            var newUserId = await _repo.CreateUserWithVerificationTokenAsync(
                normalizedEmail,
                passwordHash,
                passwordSalt,
                passwordAlgorithm,
                tokenHash,
                expiresAt);
            // Do not log the raw token; just indicate that the token was generated
            _logger.LogInformation("Verification token generated for {email}", normalizedEmail);
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

    private static bool IsJsonContentType(string? contentType)
    {
        return !string.IsNullOrWhiteSpace(contentType)
            && contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<CreateUserRequest?> DeserializeBodyAsync(HttpRequest req)
    {
        if (req.Body == null)
        {
            return null;
        }

        req.EnableBuffering();

        using var reader = new StreamReader(req.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        if (req.Body.CanSeek)
        {
            req.Body.Position = 0;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CreateUserRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return null;
        }
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
