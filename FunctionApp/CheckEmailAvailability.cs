using System;
using System.Data;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;

namespace AGBilling.CheckEMailAvailability;

public class CheckEmailAvailability
{
    private readonly ILogger<CheckEmailAvailability> _logger;
    private readonly AGRechnung.FunctionApp.Repositories.IAuthRepository _repo;

    public CheckEmailAvailability(ILogger<CheckEmailAvailability> logger, AGRechnung.FunctionApp.Repositories.IAuthRepository repo)
    {
        _logger = logger;
        _repo = repo;
    }

    [Function("CheckEmailAvailability")]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("CheckEmailAvailability function processed a request.");

        // Read email from query string or form/body (safe access)
        var email = req.Query["email"].ToString();
        if (string.IsNullOrWhiteSpace(email) && req.HasFormContentType)
        {
            email = req.Form["email"].ToString();
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return new BadRequestObjectResult(new { error = "Missing 'email' parameter" });
        }

        // Validate email format
        var emailValidator = new EmailAddressAttribute();
        if (!emailValidator.IsValid(email))
        {
            return new BadRequestObjectResult(new { error = "Invalid email format" });
        }

        try
        {
            var exists = await _repo.EmailExistsAsync(email);
            return new OkObjectResult(new { available = !exists });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking email availability");
            return new ObjectResult(new { error = "Internal server error" }) { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }
}