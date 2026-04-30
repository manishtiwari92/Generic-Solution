using FluentValidation;
using MediatR;

namespace IPS.AutoPost.Core.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs all registered <see cref="IValidator{T}"/>
/// instances for the incoming request before the handler executes.
/// Throws <see cref="ValidationException"/> when any validation rule fails.
/// </summary>
/// <remarks>
/// Registered in <c>ServiceCollectionExtensions</c> as an open-generic behavior:
/// <code>
/// services.AddScoped(typeof(IPipelineBehavior&lt;,&gt;), typeof(ValidationBehavior&lt;,&gt;));
/// </code>
/// Validators are registered automatically via:
/// <code>
/// services.AddValidatorsFromAssembly(typeof(ExecutePostCommand).Assembly);
/// </code>
/// When no validator is registered for a request type, the behavior is a no-op
/// and the handler is called directly.
/// </remarks>
/// <typeparam name="TRequest">The MediatR request type.</typeparam>
/// <typeparam name="TResponse">The MediatR response type.</typeparam>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    /// <summary>
    /// Runs all registered validators for <typeparamref name="TRequest"/>.
    /// Collects all failures across all validators before throwing so the caller
    /// receives the complete list of validation errors in a single exception.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Thrown when one or more validation rules fail.
    /// The exception contains all <see cref="FluentValidation.Results.ValidationFailure"/>
    /// instances from every registered validator.
    /// </exception>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next(cancellationToken);

        var context = new ValidationContext<TRequest>(request);

        var failures = _validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next(cancellationToken);
    }
}
