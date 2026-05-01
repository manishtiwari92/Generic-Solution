// IPS.AutoPost.Api — Program.cs
// Task 20.4: Full DI wiring for the API project.
//
// Responsibilities:
//   - Resolve AWS Secrets Manager secrets at startup (connection string, API key)
//   - Register all core services, repositories, plugins, and orchestrator
//   - Wire API key authentication middleware
//   - Expose Swagger/OpenAPI in non-production environments

using Amazon.CloudWatch;
using Amazon.SecretsManager;
using IPS.AutoPost.Api.Middleware;
using IPS.AutoPost.Core.DataAccess;
using IPS.AutoPost.Core.Extensions;
using IPS.AutoPost.Core.Infrastructure;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Services;
using IPS.AutoPost.Plugins;
using Serilog;

// ============================================================
// 1. Bootstrap Serilog early so startup errors are captured
// ============================================================
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("IPS.AutoPost.Api starting up...");

    var builder = WebApplication.CreateBuilder(args);

    // ============================================================
    // 2. Serilog — read full config from appsettings.json
    //    (CloudWatch sink is configured there for production)
    //    In Testing environment, use console-only to avoid loading
    //    CloudWatch sink assembly which isn't available in test output.
    // ============================================================
    if (builder.Environment.IsEnvironment("Testing"))
    {
        builder.Services.AddSerilog(loggerConfig =>
            loggerConfig
                .MinimumLevel.Warning()
                .WriteTo.Console()
                .Enrich.FromLogContext());
    }
    else
    {
        builder.Services.AddSerilog((services, loggerConfig) =>
            loggerConfig
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext());
    }

    // ============================================================
    // 3. AWS Secrets Manager — resolve "/" prefixed config values
    //    MUST be called before any service that reads ConnectionStrings
    //    or ApiKey:Value. Resolves:
    //      ConnectionStrings:Workflow → /IPS/Common/prod/Database/Workflow
    //      ApiKey:Value               → /IPS/Common/prod/ApiKey
    //    Skipped in Testing environment (WebApplicationFactory injects values directly).
    // ============================================================
    if (!builder.Environment.IsEnvironment("Testing"))
    {
        await builder.Configuration.AddSecretsManagerAsync();
    }

    // ============================================================
    // 4. AWS SDK clients — registered as singletons (thread-safe)
    // ============================================================
    builder.Services.AddSingleton<IAmazonSecretsManager>(_ => new AmazonSecretsManagerClient());
    builder.Services.AddSingleton<IAmazonCloudWatch>(_ => new AmazonCloudWatchClient());

    // ============================================================
    // 5. Core services
    // ============================================================
    builder.Services.AddSingleton<ConfigurationService>();
    builder.Services.AddScoped<S3ImageService>();
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddScoped<ICorrelationIdService, CorrelationIdService>();
    builder.Services.AddSingleton<ICloudWatchMetricsService, CloudWatchMetricsService>();

    // ============================================================
    // 6. Core engine: MediatR, pipeline behaviors, repositories,
    //    orchestrator, plugin registry, scheduler service
    // ============================================================
    builder.Services.AddAutoPostCoreServices();

    // ============================================================
    // 7. Plugin DI registrations
    //    InvitedClub: scoped services resolved from DI container
    //    Sevita: requires connection string at construction time
    //    Skipped in Testing environment (plugins are removed by ApiFactory).
    // ============================================================
    if (!builder.Environment.IsEnvironment("Testing"))
    {
        builder.Services.AddInvitedClubPlugin();

        var workflowConnectionString = builder.Configuration.GetConnectionString("Workflow")
            ?? throw new InvalidOperationException(
                "Connection string 'Workflow' is required for SevitaPlugin. " +
                "Ensure SecretsManagerConfigurationProvider resolved the secret path.");

        builder.Services.AddSevitaPlugin(workflowConnectionString);
    }

    // ============================================================
    // 8. ASP.NET Core MVC controllers
    // ============================================================
    builder.Services.AddControllers();

    // ============================================================
    // 9. OpenAPI (non-production only)
    // ============================================================
    builder.Services.AddOpenApi();

    // ============================================================
    // 10. Build the application
    // ============================================================
    var app = builder.Build();

    // ============================================================
    // 11. Register plugins into the PluginRegistry
    //     Must be done after the app is built so all scoped
    //     plugin services are resolvable.
    //     Skipped in Testing environment (plugins are not registered).
    // ============================================================
    if (!app.Environment.IsEnvironment("Testing"))
    {
        using var scope = app.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IPS.AutoPost.Core.Engine.PluginRegistry>();
        PluginRegistration.RegisterAll(scope.ServiceProvider, registry);
        Log.Information("Plugin registry populated. Registered plugins: INVITEDCLUB, SEVITA");
    }

    // ============================================================
    // 12. Middleware pipeline
    //     Order matters: Serilog → Swagger → ApiKey → Controllers
    // ============================================================
    app.UseSerilogRequestLogging();

    if (!app.Environment.IsProduction())
    {
        app.MapOpenApi();
    }

    // API key authentication — enforced before any controller is reached
    app.UseMiddleware<ApiKeyMiddleware>();

    app.MapControllers();

    // Simple health check endpoint (exempt from API key auth — see ApiKeyMiddleware)
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

    // ============================================================
    // 13. Run
    // ============================================================
    await app.RunAsync();
}
catch (Exception ex) when (ex is not OperationCanceledException && ex is not HostAbortedException)
{
    Log.Fatal(ex, "IPS.AutoPost.Api terminated unexpectedly.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Expose Program class for WebApplicationFactory in integration tests
public partial class Program { }
