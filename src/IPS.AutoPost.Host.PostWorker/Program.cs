// IPS.AutoPost.Host.PostWorker — Program.cs
// Full DI wiring implemented in Task 19.2.
// Task 7.5: Apply EF Core migrations at startup to create the 10 generic tables.
using IPS.AutoPost.Core.Migrations;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// Register AutoPostDatabaseContext for migration auto-apply at startup.
// The connection string is resolved from IConfiguration (populated by
// SecretsManagerConfigurationProvider in Task 19.2).
// For now, register with a placeholder that will be replaced in Task 19.2.
builder.Services.AddDbContext<AutoPostDatabaseContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Workflow")
        ?? throw new InvalidOperationException(
            "Connection string 'Workflow' not found. " +
            "Ensure SecretsManagerConfigurationProvider is configured.");

    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.MigrationsAssembly("IPS.AutoPost.Core");
        sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "dbo");
        sqlOptions.CommandTimeout(120); // 2-minute timeout for migration DDL
    });
});

var host = builder.Build();

// Auto-apply EF Core migrations at startup.
// This creates the 10 generic tables if they do not exist.
// Safe to run on every startup — EF Core tracks applied migrations in __EFMigrationsHistory.
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

await host.RunAsync();
