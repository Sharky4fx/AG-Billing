using System;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AGRechnung.FunctionApp.Repositories;
using AGRechnung.FunctionApp.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace AGRechnung.Authenticate;

public class Authenticate
{
    private readonly ILogger<Authenticate> _logger;
    private readonly IAuthRepository _repo;

    private record AuthenticateRequest(string? Email, string? Password);

    public Authenticate(ILogger<Authenticate> logger, IAuthRepository repo)
    {
        _logger = logger;
        _repo = repo;
    }

    [Function("Authenticate")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("Authenticate function processed a request.");

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

        var emailValidator = new EmailAddressAttribute();
        if (!emailValidator.IsValid(email))
        {
            return new BadRequestObjectResult(new { error = "Invalid email format" });
        }

        var user = await _repo.GetUserCredentialsByEmailAsync(email);
        if (user is null)
        {
            return new UnauthorizedObjectResult(new { error = "Invalid email or password" });
        }

        if (!user.Active)
        {
            return new ObjectResult(new { error = "Account is disabled" }) { StatusCode = StatusCodes.Status403Forbidden };
        }

        if (!user.VerifiedEmail)
        {
            return new ObjectResult(new { error = "Email is not verified" }) { StatusCode = StatusCodes.Status403Forbidden };
        }

        bool passwordValid;
        try
        {
            passwordValid = PasswordHasher.VerifyPassword(password, user.PasswordHash, user.PasswordSalt, user.PasswordHashAlgorithm);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Unsupported password algorithm for user {UserId}", user.UserId);
            return new ObjectResult(new { error = "Authentication configuration error" }) { StatusCode = StatusCodes.Status500InternalServerError };
        }

        if (!passwordValid)
        {
            return new UnauthorizedObjectResult(new { error = "Invalid email or password" });
        }

        try
        {
            var tokenResult = GenerateToken(user);
            return new OkObjectResult(new
            {
                token = tokenResult.Token,
                tokenType = "Bearer",
                expiresAt = tokenResult.ExpiresAt,
                user = new { id = user.UserId, uuid = user.Uuid, email = user.Email }
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to generate authentication token");
            return new ObjectResult(new { error = "Authentication configuration error" }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    private static bool IsJsonContentType(string? contentType)
    {
        return !string.IsNullOrWhiteSpace(contentType)
            && contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<AuthenticateRequest?> DeserializeBodyAsync(HttpRequest req)
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
            return JsonSerializer.Deserialize<AuthenticateRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string Token, DateTime ExpiresAt) GenerateToken(UserCredentials user)
    {
        var signingKey = Environment.GetEnvironmentVariable("AuthTokenSigningKey");
        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
        {
            throw new InvalidOperationException("Missing or invalid AuthTokenSigningKey configuration.");
        }

        var issuer = Environment.GetEnvironmentVariable("AuthTokenIssuer") ?? "AGRechnung";
        var audience = Environment.GetEnvironmentVariable("AuthTokenAudience") ?? "AGRechnungClients";
        var expires = DateTime.UtcNow.AddHours(1);

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Uuid.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("uid", user.UserId.ToString()),
            new Claim("uuid", user.Uuid.ToString()),
            new Claim("email", user.Email)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
        return (tokenString, expires);
    }
}
