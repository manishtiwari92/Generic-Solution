using FluentAssertions;
using FluentValidation;
using IPS.AutoPost.Core.Behaviors;
using MediatR;

namespace IPS.AutoPost.Core.Tests.Behaviors;

/// <summary>
/// Unit tests for <see cref="ValidationBehavior{TRequest,TResponse}"/>.
/// Verifies that valid commands pass through to the handler, invalid commands
/// throw <see cref="ValidationException"/> with all failures, and that the
/// behavior is a no-op when no validators are registered.
/// </summary>
public class ValidationBehaviorTests
{
    // -----------------------------------------------------------------------
    // Test request/response types
    // -----------------------------------------------------------------------

    private record ValidatableRequest(string ClientType, int JobId) : IRequest<string>;

    // -----------------------------------------------------------------------
    // Test validators
    // -----------------------------------------------------------------------

    /// <summary>Validator that requires ClientType to be non-empty and JobId > 0.</summary>
    private class ValidatableRequestValidator : AbstractValidator<ValidatableRequest>
    {
        public ValidatableRequestValidator()
        {
            RuleFor(r => r.ClientType)
                .NotEmpty()
                .WithMessage("ClientType is required.");

            RuleFor(r => r.JobId)
                .GreaterThan(0)
                .WithMessage("JobId must be greater than zero.");
        }
    }

    // -----------------------------------------------------------------------
    // No validators registered — behavior is a no-op
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_NoValidatorsRegistered_CallsNextAndReturnsResult()
    {
        // Arrange — empty validator list simulates no IValidator<T> registered in DI
        var behavior = new ValidationBehavior<ValidatableRequest, string>(
            Enumerable.Empty<IValidator<ValidatableRequest>>());

        var handlerCalled = false;
        // MediatR 14: RequestHandlerDelegate<T> is Func<CancellationToken, Task<T>>
        RequestHandlerDelegate<string> next = _ =>
        {
            handlerCalled = true;
            return Task.FromResult("handler-result");
        };

        // Act
        var result = await behavior.Handle(
            new ValidatableRequest(string.Empty, 0), // would fail validation if validators existed
            next,
            CancellationToken.None);

        // Assert — no validators means no validation, handler is called
        handlerCalled.Should().BeTrue();
        result.Should().Be("handler-result");
    }

    // -----------------------------------------------------------------------
    // Valid command passes through
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_ValidRequest_CallsNextAndReturnsResult()
    {
        // Arrange
        var validators = new IValidator<ValidatableRequest>[] { new ValidatableRequestValidator() };
        var behavior = new ValidationBehavior<ValidatableRequest, string>(validators);

        var handlerCalled = false;
        RequestHandlerDelegate<string> next = _ =>
        {
            handlerCalled = true;
            return Task.FromResult("success");
        };

        // Act
        var result = await behavior.Handle(
            new ValidatableRequest("INVITEDCLUB", 42),  // valid
            next,
            CancellationToken.None);

        // Assert
        handlerCalled.Should().BeTrue();
        result.Should().Be("success");
    }

    [Fact]
    public async Task Handle_ValidRequest_DoesNotThrow()
    {
        // Arrange
        var validators = new IValidator<ValidatableRequest>[] { new ValidatableRequestValidator() };
        var behavior = new ValidationBehavior<ValidatableRequest, string>(validators);

        RequestHandlerDelegate<string> next = _ => Task.FromResult("ok");

        // Act
        var act = async () => await behavior.Handle(
            new ValidatableRequest("SEVITA", 1),
            next,
            CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    // -----------------------------------------------------------------------
    // Invalid command throws ValidationException
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_InvalidRequest_ThrowsValidationException()
    {
        // Arrange
        var validators = new IValidator<ValidatableRequest>[] { new ValidatableRequestValidator() };
        var behavior = new ValidationBehavior<ValidatableRequest, string>(validators);

        RequestHandlerDelegate<string> next = _ => Task.FromResult("should-not-reach");

        // Act — both fields invalid
        var act = async () => await behavior.Handle(
            new ValidatableRequest(string.Empty, 0),
            next,
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_InvalidRequest_HandlerIsNotCalled()
    {
        // Arrange
        var validators = new IValidator<ValidatableRequest>[] { new ValidatableRequestValidator() };
        var behavior = new ValidationBehavior<ValidatableRequest, string>(validators);

        var handlerCalled = false;
        RequestHandlerDelegate<string> next = _ =>
        {
            handlerCalled = true;
            return Task.FromResult("should-not-reach");
        };

        // Act
        try
        {
            await behavior.Handle(
                new ValidatableRequest(string.Empty, -1),
                next,
                CancellationToken.None);
        }
        catch (ValidationException) { /* expected */ }

        // Assert — handler must not be called when validation fails
        handlerCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_InvalidRequest_ExceptionContainsAllFailures()
    {
        // Arrange — both ClientType and JobId are invalid
        var validators = new IValidator<ValidatableRequest>[] { new ValidatableRequestValidator() };
        var behavior = new ValidationBehavior<ValidatableRequest, string>(validators);

        RequestHandlerDelegate<string> next = _ => Task.FromResult("unreachable");

        // Act
        var act = async () => await behavior.Handle(
            new ValidatableRequest(string.Empty, 0),
            next,
            CancellationToken.None);

        // Assert — exception must contain both failures
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().HaveCount(2,
            because: "both ClientType and JobId validation rules should fail");
    }

    [Fact]
    public async Task Handle_PartiallyInvalidRequest_ThrowsWithCorrectFailure()
    {
        // Arrange — only JobId is invalid
        var validators = new IValidator<ValidatableRequest>[] { new ValidatableRequestValidator() };
        var behavior = new ValidationBehavior<ValidatableRequest, string>(validators);

        RequestHandlerDelegate<string> next = _ => Task.FromResult("unreachable");

        // Act
        var act = async () => await behavior.Handle(
            new ValidatableRequest("INVITEDCLUB", 0),  // ClientType valid, JobId invalid
            next,
            CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().HaveCount(1);
        exception.Which.Errors.Single().PropertyName.Should().Be("JobId");
        exception.Which.Errors.Single().ErrorMessage.Should().Be("JobId must be greater than zero.");
    }

    // -----------------------------------------------------------------------
    // Multiple validators — all are run, all failures collected
    // -----------------------------------------------------------------------

    private class AdditionalValidator : AbstractValidator<ValidatableRequest>
    {
        public AdditionalValidator()
        {
            RuleFor(r => r.ClientType)
                .Must(ct => ct == "INVITEDCLUB" || ct == "SEVITA")
                .WithMessage("ClientType must be INVITEDCLUB or SEVITA.");
        }
    }

    [Fact]
    public async Task Handle_MultipleValidators_AllFailuresCollected()
    {
        // Arrange — two validators, both produce failures for the same request
        var validators = new IValidator<ValidatableRequest>[]
        {
            new ValidatableRequestValidator(),
            new AdditionalValidator()
        };
        var behavior = new ValidationBehavior<ValidatableRequest, string>(validators);

        RequestHandlerDelegate<string> next = _ => Task.FromResult("unreachable");

        // Act — ClientType empty (fails both validators), JobId 0 (fails first validator)
        var act = async () => await behavior.Handle(
            new ValidatableRequest(string.Empty, 0),
            next,
            CancellationToken.None);

        // Assert — failures from both validators are collected
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().HaveCountGreaterThan(2,
            because: "failures from all registered validators must be collected");
    }

    [Fact]
    public async Task Handle_MultipleValidators_ValidRequest_PassesThrough()
    {
        // Arrange
        var validators = new IValidator<ValidatableRequest>[]
        {
            new ValidatableRequestValidator(),
            new AdditionalValidator()
        };
        var behavior = new ValidationBehavior<ValidatableRequest, string>(validators);

        RequestHandlerDelegate<string> next = _ => Task.FromResult("passed");

        // Act — valid for both validators
        var result = await behavior.Handle(
            new ValidatableRequest("INVITEDCLUB", 5),
            next,
            CancellationToken.None);

        // Assert
        result.Should().Be("passed");
    }

    // -----------------------------------------------------------------------
    // Result pass-through when valid
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_ValidRequest_ReturnsHandlerResultUnmodified()
    {
        // Arrange
        var validators = new IValidator<ValidatableRequest>[] { new ValidatableRequestValidator() };
        var behavior = new ValidationBehavior<ValidatableRequest, string>(validators);

        const string expected = "handler-output-value";
        RequestHandlerDelegate<string> next = _ => Task.FromResult(expected);

        // Act
        var result = await behavior.Handle(
            new ValidatableRequest("INVITEDCLUB", 1),
            next,
            CancellationToken.None);

        // Assert — behavior must not modify the handler's return value
        result.Should().Be(expected);
    }
}
