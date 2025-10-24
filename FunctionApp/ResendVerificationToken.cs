using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AGRechnung.FunctionApp.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure;
using Azure.Communication.Email;

namespace AGRechnung.ResendVerificationToken;

public class ResendVerificationToken
{
    private readonly ILogger<ResendVerificationToken> _logger;
    private readonly IAuthRepository _repo;

    private record ResendRequest(string? Email);

    public ResendVerificationToken(ILogger<ResendVerificationToken> logger, IAuthRepository repo)
    {
        _logger = logger;
        _repo = repo;
    }

    [Function("ResendVerificationToken")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("ResendVerificationToken function processed a request.");

        string? email = null;

        if (req.HasFormContentType)
        {
            var form = await req.ReadFormAsync();
            email = form["email"].ToString();
        }
        else if (IsJsonContentType(req.ContentType))
        {
            var payload = await DeserializeBodyAsync(req);
            email = payload?.Email;
        }

        email ??= req.Query["email"].ToString();

        if (string.IsNullOrWhiteSpace(email))
        {
            return new BadRequestObjectResult(new { error = "Missing 'email' value" });
        }

        // Normalize email
        email = email.Trim();
        var emailValidator = new EmailAddressAttribute();
        if (!emailValidator.IsValid(email))
        {
            return new BadRequestObjectResult(new { error = "Invalid email format" });
        }

        var normalizedEmail = email.ToLowerInvariant();

        try
        {
            // Get existing verification token info
            var tokenInfo = await _repo.GetVerificationTokenForResendAsync(normalizedEmail);
            if (tokenInfo is null)
            {
                // User not found, already verified, or doesn't have a token
                // Return success for security (don't reveal if email exists)
                return new OkObjectResult(new { sent = true, message = "If the email exists and is unverified, a verification link has been sent." });
            }

            // Generate new token
            var newToken = GenerateUrlSafeToken(32);
            var newTokenHash = HashToken(newToken);
            var expiresAt = DateTime.UtcNow.AddHours(24);

            // Update token in DB
            await _repo.UpdateVerificationTokenAsync(normalizedEmail, newTokenHash, expiresAt);

            // Send verification email
            var emailSent = await SendVerificationEmailAsync(normalizedEmail, tokenInfo.Value.Uuid, newToken);
            if (!emailSent)
            {
                _logger.LogWarning("Token updated but email sending failed for {email}", normalizedEmail);
                return new ObjectResult(new { error = "Failed to send email" }) { StatusCode = StatusCodes.Status500InternalServerError };
            }

            _logger.LogInformation("Verification email resent to {email}", normalizedEmail);
            return new OkObjectResult(new { sent = true, message = "Verification email has been sent." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resending verification token for {email}", normalizedEmail);
            return new ObjectResult(new { error = "Internal server error" }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    private static string GenerateUrlSafeToken(int bytesLength)
    {
        var bytes = new byte[bytesLength];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
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

    private static async Task<ResendRequest?> DeserializeBodyAsync(HttpRequest req)
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
            return JsonSerializer.Deserialize<ResendRequest>(body, new JsonSerializerOptions
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

    private async Task<bool> SendVerificationEmailAsync(string emailAddress, Guid userUuid, string token)
    {
        try
        {
            var connectionString = Environment.GetEnvironmentVariable("COMMUNICATION_SERVICES_CONNECTION_STRING");
            var senderAddress = Environment.GetEnvironmentVariable("Email__SenderAddress");
            var baseUrl = Environment.GetEnvironmentVariable("VerifyEmail__BaseUrl");

            if (string.IsNullOrWhiteSpace(connectionString) ||
                string.IsNullOrWhiteSpace(senderAddress) ||
                string.IsNullOrWhiteSpace(baseUrl))
            {
                _logger.LogError("Email configuration missing (COMMUNICATION_SERVICES_CONNECTION_STRING, Email__SenderAddress, or VerifyEmail__BaseUrl)");
                return false;
            }

            var emailClient = new EmailClient(connectionString);

            var verificationLink = $"{baseUrl}/VerifyEmail?uuid={userUuid}&token={Uri.EscapeDataString(token)}";

            var emailMessage = new EmailMessage(
                senderAddress: senderAddress,
                content: new EmailContent("Verify your email address")
                {
                    PlainText = $"Please verify your email address by clicking this link: {verificationLink}",
                    Html = $@"
                    <html>
                        <body>
                            <h1>Welcome to AG Billing!</h1>
                            <p>Please verify your email address by clicking the link below:</p>
                            <p><a href=""{verificationLink}"">Verify Email</a></p>
                            <p>This link will expire in 24 hours.</p>
                            <p>If you did not create an account, please ignore this email.</p>
                        </body>
                    </html>"
                },
                recipients: new EmailRecipients(new List<EmailAddress>
                {
                    new EmailAddress(emailAddress)
                }));

            EmailSendOperation emailSendOperation = await emailClient.SendAsync(
                WaitUntil.Completed,
                emailMessage);

            _logger.LogInformation("Verification email sent to {email}", emailAddress);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification email to {email}", emailAddress);
            return false;
        }
    }
}
