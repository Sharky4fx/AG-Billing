using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AGBilling.HelloWorld;

public class HelloWorldHttpTrigger1
{
    private readonly ILogger<HelloWorldHttpTrigger1> _logger;

    public HelloWorldHttpTrigger1(ILogger<HelloWorldHttpTrigger1> logger)
    {
        _logger = logger;
    }

    [Function("HelloWorldHttpTrigger1")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}