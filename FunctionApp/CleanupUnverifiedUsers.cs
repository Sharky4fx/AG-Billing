using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using AGRechnung.FunctionApp.Repositories;

namespace AGRechnung.CleanupUnverifiedUsers;

public class CleanupUnverifiedUsers
{
    private readonly ILogger<CleanupUnverifiedUsers> _logger;
    private readonly IAuthRepository _repo;

    public CleanupUnverifiedUsers(ILogger<CleanupUnverifiedUsers> logger, IAuthRepository repo)
    {
        _logger = logger;
        _repo = repo;
    }

    [Function("CleanupUnverifiedUsers")]
    public async Task Run([TimerTrigger("0 0 * * * *")] // Run once every hour
        TimerInfo timerInfo)
    {
        _logger.LogInformation("Starting cleanup of unverified users at: {time}", DateTime.UtcNow);

        try
        {
            var deletedCount = await _repo.CleanupUnverifiedUsersAsync();
            
            if (deletedCount > 0)
            {
                _logger.LogInformation("Cleaned up {count} unverified user(s) with expired tokens", deletedCount);
            }
            else
            {
                _logger.LogInformation("No expired unverified users found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup of unverified users");
            throw; // Let the function runtime handle the error
        }
    }
}