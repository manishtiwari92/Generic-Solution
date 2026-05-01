using Amazon.Lambda.Core;
using Amazon.Scheduler;
using Amazon.SecretsManager;
using IPS.AutoPost.Core.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Register the Lambda JSON serializer
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace IPS.AutoPost.Scheduler;

/// <summary>
/// AWS Lambda function handler for the IPS AutoPost Scheduler.
/// Triggered every 10 minutes by an EventBridge rate rule.
/// Reads <c>generic_execution_schedule</c> from RDS and synchronises
/// the corresponding Amazon EventBridge Scheduler rules.
/// </summary>
/// <remarks>
/// This Lambda function:
/// <list type="bullet">
///   <item>NEVER reads workitems.</item>
///   <item>NEVER calls external ERP APIs.</item>
///   <item>NEVER posts invoices.</item>
///   <item>Only manages EventBridge Scheduler rules.</item>
/// </list>
/// </remarks>
public sealed class Function
{
    private readonly IServiceProvider _serviceProvider;

    // -----------------------------------------------------------------------
    // Constructor — called once per Lambda container lifetime
    // -----------------------------------------------------------------------

    /// <summary>
    /// Parameterless constructor used by the Lambda runtime.
    /// Builds the DI container and resolves configuration from AWS Secrets Manager.
    /// </summary>
    public Function() : this(serviceProvider: null) { }

    /// <summary>
    /// Constructor for dependency injection (used in tests and local runs).
    /// When <paramref name="serviceProvider"/> is <c>null</c>, the default
    /// production DI container is built.
    /// </summary>
    public Function(IServiceProvider? serviceProvider)
    {
        _serviceProvider = serviceProvider ?? BuildServiceProvider();
    }

    // -----------------------------------------------------------------------
    // Lambda handler
    // -----------------------------------------------------------------------

    /// <summary>
    /// Lambda entry point. Triggered by EventBridge on a <c>rate(10 minutes)</c> schedule.
    /// Calls <see cref="SchedulerSyncService.SyncAsync"/> to synchronise all EventBridge rules.
    /// </summary>
    /// <param name="input">The raw Lambda event input (not used — trigger is time-based).</param>
    /// <param name="context">Lambda execution context (used for logging).</param>
    public async Task FunctionHandler(object? input, ILambdaContext context)
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<Function>>();

        logger.LogInformation(
            "IPS.AutoPost.Scheduler Lambda invoked. RequestId={RequestId}, RemainingTime={RemainingMs}ms",
            context.AwsRequestId,
            context.RemainingTime.TotalMilliseconds);

        try
        {
            var syncService = _serviceProvider.GetRequiredService<SchedulerSyncService>();
            var configuration = _serviceProvider.GetRequiredService<IConfiguration>();

            // Read queue ARNs and scheduler role ARN from environment variables.
            // These are non-secret values injected by CloudFormation at deploy time.
            var feedQueueArn     = GetRequiredEnvVar("FEED_QUEUE_ARN");
            var postQueueArn     = GetRequiredEnvVar("POST_QUEUE_ARN");
            var schedulerRoleArn = GetRequiredEnvVar("SCHEDULER_ROLE_ARN");

            await syncService.SyncAsync(feedQueueArn, postQueueArn, schedulerRoleArn);

            logger.LogInformation("IPS.AutoPost.Scheduler Lambda completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "IPS.AutoPost.Scheduler Lambda failed.");
            throw; // Re-throw so Lambda marks the invocation as failed
        }
    }

    // -----------------------------------------------------------------------
    // DI container bootstrap
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds the production DI container for the Lambda function.
    /// Resolves secrets from AWS Secrets Manager before registering services.
    /// </summary>
    private static IServiceProvider BuildServiceProvider()
    {
        // Build configuration — reads appsettings.json + environment variables
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        // Resolve "/" prefixed connection strings from AWS Secrets Manager
        // This is synchronous bootstrap — Lambda cold start is acceptable here
        configBuilder.AddSecretsManagerAsync(new AmazonSecretsManagerClient())
            .GetAwaiter()
            .GetResult();

        var configuration = configBuilder.Build();

        var services = new ServiceCollection();

        // ----------------------------------------------------------------
        // Configuration
        // ----------------------------------------------------------------
        services.AddSingleton<IConfiguration>(configuration);

        // ----------------------------------------------------------------
        // Logging — write to CloudWatch Logs via Lambda's built-in log sink
        // ----------------------------------------------------------------
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole(); // Lambda captures stdout → CloudWatch Logs
            logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
        });

        // ----------------------------------------------------------------
        // AWS SDK clients
        // ----------------------------------------------------------------
        services.AddSingleton<IAmazonScheduler>(_ => new AmazonSchedulerClient());
        services.AddSingleton<IAmazonSecretsManager>(_ => new AmazonSecretsManagerClient());

        // ----------------------------------------------------------------
        // SchedulerSyncService
        // ----------------------------------------------------------------
        services.AddSingleton<SchedulerSyncService>(sp =>
        {
            var connectionString = configuration.GetConnectionString("Workflow")
                ?? throw new InvalidOperationException(
                    "Connection string 'Workflow' is not configured. " +
                    "Ensure SecretsManagerConfigurationProvider resolved the secret path.");

            return new SchedulerSyncService(
                connectionString,
                sp.GetRequiredService<IAmazonScheduler>(),
                sp.GetRequiredService<ILogger<SchedulerSyncService>>());
        });

        return services.BuildServiceProvider();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Reads a required environment variable and throws a descriptive exception
    /// when it is missing or empty.
    /// </summary>
    private static string GetRequiredEnvVar(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Required environment variable '{name}' is not set. " +
                "Ensure the Lambda function's CloudFormation template sets this variable.");
        return value;
    }
}
