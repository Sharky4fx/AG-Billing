using System;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using AGRechnung.FunctionApp.Repositories;

namespace AGRechnung.VerifyEmail;

public class VerifyEmail
{
    private readonly ILogger<VerifyEmail> _logger;
    private readonly IAuthRepository _repo;

    public VerifyEmail(ILogger<VerifyEmail> logger, IAuthRepository repo)
    {
        _logger = logger;
        _repo = repo;
    }

    [Function("VerifyEmail")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        _logger.LogInformation("VerifyEmail function processing a request.");

        // Get userId and token from query string
        if (!int.TryParse(req.Query["userId"], out var userId))
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing user ID" });
        }

        var token = req.Query["token"].ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            return new BadRequestObjectResult(new { error = "Missing verification token" });
        }

        try
        {
            // Hash the token for comparison with stored hash
            var tokenHash = HashToken(token);
            
            await _repo.VerifyEmailAsync(userId, tokenHash);
            
            return new OkObjectResult(new { 
                verified = true,
                message = "Email verified successfully. You can now log in."
            });
        }
        catch (InvalidVerificationTokenException)
        {
            return new BadRequestObjectResult(new { 
                error = "Invalid or expired verification token. Please request a new verification email."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying email for user {userId}", userId);
            return new ObjectResult(new { error = "Internal server error" }) 
            { 
                StatusCode = StatusCodes.Status500InternalServerError 
            };
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