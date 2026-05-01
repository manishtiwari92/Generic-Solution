// IPS.AutoPost.Host.FeedWorker — Program.cs
// Task 18.2: Full DI wiring for the Feed Worker ECS Fargate service.
// Task 7.5:  Apply EF Core migrations at startup to create the 10 generic tables.

using Amazon.CloudWatch;
using Amazon.SecretsManager;
using Amazon.SQS;
using IPS.AutoPost.Core.DataAccess;
using IPS.AutoPost.Core.Extensions;
using IPS.AutoPost.Core.Infrastructure;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Migrations;
using IPS.AutoPost.Core.Services;
using IPS.AutoPost.Host.FeedWorker;
using IPS.AutoPost.Plugins;
using Microsoft.EntityFrameworkCore;
using Serilog;

// ============================================================
// 1. Bootstrap Serilog early so startup errors are captured
// ============================================================
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("IPS.AutoPost.Host.FeedWorker starting up...");

    var builder = Host.CreateApplicationBuilder(args);

    // ============================================================
    // 2. Serilog — read full config from appsettings.json
    //    (CloudWatch sink is configured there for production)
    // ============================================================
    builder.Services.AddSerilog((services, loggerConfig) =>
        loggerConfig
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

    // ============================================================
    // 3. AWS Secrets Manager — resolve "/" prefixed config values
    //    MUST be called before any service that reads ConnectionStrings
    // ============================================================
    await builder.Configuration.AddSecretsManagerAsync();

    // ============================================================
    // 4. AWS SDK clients — registered as singletons (thread-safe)
    // ============================================================
    builder.Services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient());
    builder.Services.AddSingleton<IAmazonSecretsManager>(_ => new AmazonSecretsManagerClient());
    builder.Services.AddSingleton<IAmazonCloudWatch>(_ => new AmazonCloudWatchClient());

    // ============================================================
    // 5. SqlHelper — static class, no registration needed.
    //    Connection string is resolved from IConfiguration at call time.
    //    Register a factory/accessor so repositories can get the
    //    resolved connection string from IConfiguration.
    // ============================================================
    // (SqlHelper is a static class — repositories receive IConfiguration
    //  and call builder.Configuration.GetConnectionString("Workflow") directly)

    // ============================================================
    // 6. Core services
    // ============================================================
    builder.Services.AddSingleton<ConfigurationService>();
    builder.Services.AddScoped<S3ImageService>();
    builder.Services.AddScoped<IEmailService, EmailService>();
    builder.Services.AddScoped<ICorrelationIdService, CorrelationIdService>();
    builder.Services.AddSingleton<ICloudWatchMetricsService, CloudWatchMetricsService>();

    // ============================================================
    // 7. Core engine: MediatR, pipeline behaviors, repositories,
    //    orchestrator, plugin registry, scheduler service
    // ============================================================
    builder.Services.AddAutoPostCoreServices();

    // ============================================================
    // 8. EF Core — AutoPostDatabaseContext for migration auto-apply
    // ============================================================
    builder.Services.AddDbContext<AutoPostDatabaseContext>(options =>
    {
        var connectionString = builder.Configuration.GetConnectionString("Workflow")
            ?? throw new InvalidOperationException(
                "Connection string 'Workflow' not found. " +
                "Ensure SecretsManagerConfigurationProvider resolved the secret path.");

        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsAssembly("IPS.AutoPost.Core");
            sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "dbo");
            sqlOptions.CommandTimeout(120); // 2-minute timeout for migration DDL
        });
    });

    // ============================================================
    // 9. Plugin DI registrations
    //    InvitedClub: scoped services resolved from DI container
    //    Sevita: requires connection string at construction time
    // ============================================================
    builder.Services.AddInvitedClubPlugin();

    // Sevita plugin needs the connection string at construction time.
    // We read it here after SecretsManagerConfigurationProvider has resolved it.
    var workflowConnectionString = builder.Configuration.GetConnectionString("Workflow")
        ?? throw new InvalidOperationException(
            "Connection string 'Workflow' is required for SevitaPlugin. " +
            "Ensure SecretsManagerConfigurationProvider resolved the secret path.");

    builder.Services.AddSevitaPlugin(workflowConnectionString);

    // ============================================================
    // 10. FeedWorker BackgroundService
    // ============================================================
    builder.Services.AddHostedService<FeedWorker>();

    // ============================================================
    // 11. Build the host
    // ============================================================
    var host = builder.Build();

    // ============================================================
    // 12. Auto-apply EF Core migrations at startup
    //     Creates the 10 generic tables if they do not exist.
    //     Safe to run on every startup — EF Core tracks applied
    //     migrations in __EFMigrationsHistory.
    // ============================================================
    using (var scope = host.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<AutoPostDatabaseContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            logger.LogInformation("Applying EF Core migrations...");
            await context.Database.MigrateAsync();
            logger.LogInformation("EF Core migrations applied successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply EF Core migrations. Worker will not start.");
            throw;
        }
    }

    // ============================================================
    // 13. Register plugins into the PluginRegistry
    //     Must be done after the host is built so all scoped
    //     plugin services are resolvable.
    // ============================================================
    using (var scope = host.Services.CreateScope())
    {
        var registry = scope.ServiceProvider.GetRequiredService<IPS.AutoPost.Core.Engine.PluginRegistry>();
        PluginRegistration.RegisterAll(scope.ServiceProvider, registry);
        Log.Information("Plugin registry populated. Registered plugins: INVITEDCLUB, SEVITA");
    }

    // ============================================================
    // 14. Run the host
    // ============================================================
    await host.RunAsync();
}
catch (Exception ex) when (ex is not OperationCanceledException && ex is not HostAbortedException)
{
    Log.Fatal(ex, "IPS.AutoPost.Host.FeedWorker terminated unexpectedly.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
