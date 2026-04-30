using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IPS.AutoPost.Core.Migrations;

/// <summary>
/// Design-time factory for AutoPostDatabaseContext.
/// Used exclusively by EF Core CLI tools (dotnet ef migrations add, dotnet ef database update).
/// NOT used at runtime — the real DbContext is registered in DI with the actual connection string.
/// </summary>
public class AutoPostDatabaseContextFactory : IDesignTimeDbContextFactory<AutoPostDatabaseContext>
{
    public AutoPostDatabaseContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AutoPostDatabaseContext>();

        // Design-time only: use a placeholder connection string.
        // The actual connection string is injected at runtime via DI / Secrets Manager.
        optionsBuilder.UseSqlServer(
            "Server=(local);Database=Workflow;Trusted_Connection=True;TrustServerCertificate=True;",
            sqlOptions =>
            {
                sqlOptions.MigrationsAssembly("IPS.AutoPost.Core");
                sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "dbo");
            });

        return new AutoPostDatabaseContext(optionsBuilder.Options);
    }
}
