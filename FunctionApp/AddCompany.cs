using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using AGRechnung.FunctionApp.Repositories;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace AGRechnung.AddCompany;

public class AddCompany
{
    private readonly ILogger<AddCompany> _logger;
    private readonly IAuthRepository _authRepo;
    private readonly ICompanyRepository _companyRepo;

    private record AddCompanyRequest(string? Name, string? VatNumber, string? Street, string? PostalCode, string? City, string? Country);

    public AddCompany(ILogger<AddCompany> logger, IAuthRepository authRepo, ICompanyRepository companyRepo)
    {
        _logger = logger;
        _authRepo = authRepo;
        _companyRepo = companyRepo;
    }

    [Function("AddCompany")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "companies")] HttpRequest req)
    {
        // Validate Authorization: Bearer <jwt>
        var principal = ValidateBearerToken(req, _logger, out var tokenError);
        if (principal is null)
        {
            return new UnauthorizedObjectResult(new { error = tokenError ?? "Unauthorized" });
        }

        // Extract identity claims: email and uuid
        var email = principal.FindFirstValue(ClaimTypes.Email)
                    ?? principal.FindFirstValue("email");
        var uuidStr = principal.FindFirstValue("uuid")
                      ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(email) || !Guid.TryParse(uuidStr, out var userUuid))
        {
            return new UnauthorizedObjectResult(new { error = "Token missing required claims (email/uuid)" });
        }

        // Normalize email and verify it maps to an active user with the same UUID
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var creds = await _authRepo.GetUserCredentialsByEmailAsync(normalizedEmail);
        if (creds is null || creds.Uuid != userUuid || !creds.Active)
        {
            return new UnauthorizedObjectResult(new { error = "Invalid user or token does not match user" });
        }

        // Accept JSON or form body
        AddCompanyRequest? payload = null;
        try
        {
            if (req.HasFormContentType)
            {
                var form = await req.ReadFormAsync();
                payload = new AddCompanyRequest(
                    form["name"].ToString(),
                    form["vatNumber"].ToString(),
                    form["street"].ToString(),
                    form["postalCode"].ToString(),
                    form["city"].ToString(),
                    form["country"].ToString()
                );
            }
            else
            {
                using var reader = new StreamReader(req.Body);
                var json = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    payload = JsonSerializer.Deserialize<AddCompanyRequest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { error = "Invalid JSON body" });
        }

        if (payload is null)
        {
            return new BadRequestObjectResult(new { error = "Missing request body" });
        }

        // Validate required fields mapped to NOT NULL columns
        if (string.IsNullOrWhiteSpace(payload.Name))
            return new BadRequestObjectResult(new { error = "Missing 'name'" });
        if (string.IsNullOrWhiteSpace(payload.Street))
            return new BadRequestObjectResult(new { error = "Missing 'street'" });
        if (string.IsNullOrWhiteSpace(payload.PostalCode))
            return new BadRequestObjectResult(new { error = "Missing 'postalCode'" });
        if (string.IsNullOrWhiteSpace(payload.City))
            return new BadRequestObjectResult(new { error = "Missing 'city'" });

        var name = payload.Name.Trim();
        var vat = string.IsNullOrWhiteSpace(payload.VatNumber) ? null : payload.VatNumber.Trim();
        var street = payload.Street.Trim();
        var postalCode = payload.PostalCode.Trim();
        var city = payload.City.Trim();
        var country = string.IsNullOrWhiteSpace(payload.Country) ? null : payload.Country.Trim();

        try
        {
            var companyId = await _companyRepo.CreateCompanyAsync(
                creds.UserId,
                name,
                vat,
                street,
                postalCode,
                city,
                country // null => DB default 'Deutschland'
            );

            _logger.LogInformation("Company created for user {UserUuid} with Id {CompanyId}", creds.Uuid, companyId);
            return new ObjectResult(new { created = true, id = companyId }) { StatusCode = StatusCodes.Status201Created };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create company for user {UserUuid}", creds.Uuid);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    private static ClaimsPrincipal? ValidateBearerToken(HttpRequest req, ILogger logger, out string? error)
    {
        error = null;
        if (!req.Headers.TryGetValue("Authorization", out var values))
        {
            error = "Missing Authorization header";
            return null;
        }
        var auth = values.ToString();
        const string prefix = "Bearer ";
        if (!auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "Authorization header must be Bearer";
            return null;
        }
        var token = auth.Substring(prefix.Length).Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            error = "Empty bearer token";
            return null;
        }

        var issuer = Environment.GetEnvironmentVariable("Jwt__Issuer");
        var audience = Environment.GetEnvironmentVariable("Jwt__Audience");
        var signingKey = Environment.GetEnvironmentVariable("Jwt__SigningKey");

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            logger.LogError("JWT signing key not configured (Jwt__SigningKey)");
            error = "Auth not configured";
            return null;
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateIssuer = !string.IsNullOrWhiteSpace(issuer),
            ValidIssuer = issuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(audience),
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        try
        {
            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch (SecurityTokenException ste)
        {
            error = ste.Message;
            return null;
        }
        catch (Exception)
        {
            error = "Token validation error";
            return null;
        }
    }
}
