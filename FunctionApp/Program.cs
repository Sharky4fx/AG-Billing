using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Register repository using connection string from environment variable
var sqlConn = Environment.GetEnvironmentVariable("SqlConnection") ?? string.Empty;
builder.Services.AddSingleton<AGRechnung.FunctionApp.Repositories.IAuthRepository>(sp =>
{
    return new AGRechnung.FunctionApp.Repositories.SqlAuthRepository(sqlConn);
});

builder.Services.AddSingleton<AGRechnung.FunctionApp.Repositories.ICompanyRepository>(sp =>
{
    return new AGRechnung.FunctionApp.Repositories.SqlCompanyRepository(sqlConn);
});

builder.Build().Run();
