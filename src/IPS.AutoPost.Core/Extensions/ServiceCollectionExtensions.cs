using FluentValidation;
using IPS.AutoPost.Core.Behaviors;
using IPS.AutoPost.Core.DataAccess;
using IPS.AutoPost.Core.Engine;
using IPS.AutoPost.Core.Handlers;
using IPS.AutoPost.Core.Interfaces;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.AutoPost.Core.Extensions;

/// <summary>
/// Extension methods for registering all IPS.AutoPost.Core services into the
/// dependency injection container.
/// Called from <c>Program.cs</c> in both <c>FeedWorker</c> and <c>PostWorker</c>
/// host projects, and from the <c>Api</c> project.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers MediatR commands, handlers, pipeline behaviors, FluentValidation
    /// validators, and core engine services.
    /// </summary>
    /// <remarks>
    /// Pipeline behavior registration order matters — behaviors execute in the order
    /// they are registered:
    /// <list type="number">
    ///   <item><c>LoggingBehavior</c> — logs command start/end with CorrelationId.</item>
    ///   <item><c>ValidationBehavior</c> — validates the command before the handler runs.</item>
    /// </list>
    /// </remarks>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddAutoPostCoreServices(this IServiceCollection services)
    {
        // -----------------------------------------------------------------------
        // MediatR — registers all IRequestHandler<,> implementations in this assembly
        // -----------------------------------------------------------------------
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ExecutePostHandler).Assembly));

        // -----------------------------------------------------------------------
        // Pipeline behaviors — registered in execution order
        // LoggingBehavior runs first (outermost), ValidationBehavior runs second
        // -----------------------------------------------------------------------
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        // -----------------------------------------------------------------------
        // FluentValidation — scans this assembly for all IValidator<T> implementations
        // -----------------------------------------------------------------------
        services.AddValidatorsFromAssembly(typeof(ExecutePostHandler).Assembly);

        // -----------------------------------------------------------------------
        // Core engine services
        // -----------------------------------------------------------------------
        services.AddScoped<AutoPostOrchestrator>();
        services.AddSingleton<PluginRegistry>();
        services.AddScoped<SchedulerService>();

        // -----------------------------------------------------------------------
        // Repository implementations
        // -----------------------------------------------------------------------
        services.AddScoped<IConfigurationRepository, ConfigurationRepository>();
        services.AddScoped<IWorkitemRepository, WorkitemRepository>();
        services.AddScoped<IRoutingRepository, RoutingRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IScheduleRepository, ScheduleRepository>();

        return services;
    }
}
