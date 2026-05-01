using IPS.AutoPost.Core.Engine;
using IPS.AutoPost.Plugins.InvitedClub;
using IPS.AutoPost.Plugins.Sevita;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.AutoPost.Plugins;

/// <summary>
/// Registers all client plugins into the <see cref="PluginRegistry"/> at application startup.
/// <para>
/// Call <see cref="RegisterAll"/> from the host's DI setup (Program.cs) after all plugin
/// dependencies have been registered with the service container.
/// </para>
/// <example>
/// <code>
/// // In Program.cs / Startup.cs:
/// services.AddInvitedClubPlugin();
/// // ... register other plugin dependencies ...
///
/// // After building the host:
/// using var scope = host.Services.CreateScope();
/// var registry = scope.ServiceProvider.GetRequiredService&lt;PluginRegistry&gt;();
/// PluginRegistration.RegisterAll(scope.ServiceProvider, registry);
/// </code>
/// </example>
/// </summary>
public static class PluginRegistration
{
    /// <summary>
    /// Registers all known plugins into the provided <paramref name="registry"/>.
    /// <para>
    /// Currently registered plugins:
    /// <list type="bullet">
    ///   <item><c>INVITEDCLUB</c> — <see cref="InvitedClubPlugin"/></item>
    ///   <item><c>SEVITA</c> — <see cref="SevitaPlugin"/></item>
    /// </list>
    /// </para>
    /// <para>
    /// To add a new plugin: create the plugin class in <c>IPS.AutoPost.Plugins</c>,
    /// register its dependencies via an extension method, then add one
    /// <c>registry.Register(...)</c> call here. No changes to <c>IPS.AutoPost.Core</c>
    /// are required.
    /// </para>
    /// </summary>
    /// <param name="serviceProvider">
    /// The DI service provider used to resolve plugin instances and their dependencies.
    /// </param>
    /// <param name="registry">
    /// The <see cref="PluginRegistry"/> to populate.
    /// </param>
    public static void RegisterAll(IServiceProvider serviceProvider, PluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(registry);

        // -----------------------------------------------------------------------
        // 13.4 Register InvitedClubPlugin
        // client_type = "INVITEDCLUB"
        // -----------------------------------------------------------------------
        registry.Register(serviceProvider.GetRequiredService<InvitedClubPlugin>());

        // -----------------------------------------------------------------------
        // 17.4 Register SevitaPlugin
        // client_type = "SEVITA"
        // -----------------------------------------------------------------------
        registry.Register(serviceProvider.GetRequiredService<SevitaPlugin>());

        // -----------------------------------------------------------------------
        // Future plugins are registered here:
        // registry.Register(serviceProvider.GetRequiredService<MediaPlugin>());
        // -----------------------------------------------------------------------
    }

    /// <summary>
    /// Registers all InvitedClub plugin services into the DI container.
    /// Call this from Program.cs before building the host.
    /// </summary>
    public static IServiceCollection AddInvitedClubPlugin(this IServiceCollection services)
    {
        services.AddScoped<IPS.AutoPost.Plugins.InvitedClub.IInvitedClubPostDataAccess,
                           IPS.AutoPost.Plugins.InvitedClub.SqlInvitedClubPostDataAccess>();

        services.AddScoped<IPS.AutoPost.Plugins.InvitedClub.IInvitedClubRetryDataAccess,
                           IPS.AutoPost.Plugins.InvitedClub.SqlInvitedClubRetryDataAccess>();

        services.AddScoped<IPS.AutoPost.Plugins.InvitedClub.IInvitedClubFeedDataAccess,
                           IPS.AutoPost.Plugins.InvitedClub.SqlInvitedClubFeedDataAccess>();

        services.AddScoped<InvitedClubRetryService>();
        services.AddScoped<InvitedClubPostStrategy>();
        services.AddScoped<InvitedClubFeedStrategy>();
        services.AddScoped<InvitedClubPlugin>();

        return services;
    }

    /// <summary>
    /// Registers all Sevita plugin services into the DI container.
    /// Call this from Program.cs before building the host.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="connectionString">
    /// The SQL Server connection string for the Workflow database.
    /// Resolved by the host from IConfiguration before calling this method.
    /// </param>
    public static IServiceCollection AddSevitaPlugin(
        this IServiceCollection services,
        string connectionString)
    {
        // SqlSevitaPostDataAccess requires the connection string at construction time.
        services.AddScoped<ISevitaPostDataAccess>(_ =>
            new SqlSevitaPostDataAccess(connectionString));

        services.AddScoped<SevitaTokenService>();
        services.AddScoped<SevitaValidationService>();
        services.AddScoped<SevitaPostStrategy>();
        services.AddScoped<SevitaPlugin>();

        return services;
    }
}
