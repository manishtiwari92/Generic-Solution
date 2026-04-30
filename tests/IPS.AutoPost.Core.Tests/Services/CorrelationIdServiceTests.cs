using FluentAssertions;
using IPS.AutoPost.Core.Services;

namespace IPS.AutoPost.Core.Tests.Services;

/// <summary>
/// Unit tests for <see cref="CorrelationIdService"/>.
/// Verifies ID generation, persistence within an async context, GUID format,
/// explicit ID setting, and AsyncLocal isolation between concurrent tasks.
/// </summary>
public class CorrelationIdServiceTests
{
    // -----------------------------------------------------------------------
    // GetOrCreateCorrelationId
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetOrCreateCorrelationId_ReturnsNonEmptyString()
    {
        // Arrange — run in a fresh Task.Run to get an isolated AsyncLocal context
        await Task.Run(() =>
        {
            var sut = new CorrelationIdService();
            // Seed a known value so we don't inherit a stale ID from a prior test
            sut.SetCorrelationId(Guid.NewGuid().ToString());

            // Act
            var id = sut.GetOrCreateCorrelationId();

            // Assert
            id.Should().NotBeNullOrEmpty(because: "GetOrCreateCorrelationId must always return a usable ID");
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetOrCreateCorrelationId_ReturnsSameIdOnSubsequentCalls()
    {
        await Task.Run(() =>
        {
            var sut = new CorrelationIdService();
            // Ensure a fresh context by setting a known value first
            sut.SetCorrelationId(Guid.NewGuid().ToString());

            // Act
            var first = sut.GetOrCreateCorrelationId();
            var second = sut.GetOrCreateCorrelationId();

            // Assert
            second.Should().Be(first,
                because: "the same async context must always return the same correlation ID");
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetOrCreateCorrelationId_GeneratesGuidFormat()
    {
        // Run in a fresh Task.Run so AsyncLocal starts as null, forcing auto-generation
        await Task.Run(() =>
        {
            var sut = new CorrelationIdService();
            var id = sut.GetOrCreateCorrelationId();

            // Assert
            Guid.TryParse(id, out _).Should().BeTrue(
                because: "the auto-generated correlation ID must be a valid GUID string");
        }, TestContext.Current.CancellationToken);
    }

    // -----------------------------------------------------------------------
    // SetCorrelationId
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SetCorrelationId_StoresAndReturnsId()
    {
        await Task.Run(() =>
        {
            var sut = new CorrelationIdService();
            const string expected = "my-id";

            // Act
            using var _ = sut.SetCorrelationId(expected);
            var actual = sut.GetOrCreateCorrelationId();

            // Assert
            actual.Should().Be(expected,
                because: "GetOrCreateCorrelationId must return the ID that was explicitly set");
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SetCorrelationId_ReturnsDisposable()
    {
        await Task.Run(() =>
        {
            var sut = new CorrelationIdService();

            // Act
            var disposable = sut.SetCorrelationId("some-id");

            // Assert
            disposable.Should().NotBeNull(
                because: "SetCorrelationId must return a non-null IDisposable for Serilog LogContext cleanup");

            disposable.Dispose(); // should not throw
        }, TestContext.Current.CancellationToken);
    }

    // -----------------------------------------------------------------------
    // AsyncLocal isolation between concurrent tasks
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AsyncLocal_IsolationBetweenConcurrentTasks()
    {
        // Use a barrier so both tasks are running concurrently before either reads its ID.
        var barrier = new SemaphoreSlim(0, 2);

        const string idA = "task-a-id";
        const string idB = "task-b-id";

        string? readA = null;
        string? readB = null;

        var ct = TestContext.Current.CancellationToken;

        var taskA = Task.Run(async () =>
        {
            var sut = new CorrelationIdService();
            using var _ = sut.SetCorrelationId(idA);

            // Signal ready and wait for task B to also be ready
            barrier.Release();
            await barrier.WaitAsync(ct);

            readA = sut.GetOrCreateCorrelationId();
        }, ct);

        var taskB = Task.Run(async () =>
        {
            var sut = new CorrelationIdService();
            using var _ = sut.SetCorrelationId(idB);

            // Signal ready and wait for task A to also be ready
            barrier.Release();
            await barrier.WaitAsync(ct);

            readB = sut.GetOrCreateCorrelationId();
        }, ct);

        await Task.WhenAll(taskA, taskB);

        readA.Should().Be(idA,
            because: "task A must read its own correlation ID, not task B's");
        readB.Should().Be(idB,
            because: "task B must read its own correlation ID, not task A's");
    }

    // -----------------------------------------------------------------------
    // AsyncLocal propagation to child tasks
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AsyncLocal_ChildTaskInheritsParentId()
    {
        var ct = TestContext.Current.CancellationToken;

        // Run in an isolated outer Task.Run to avoid inheriting test-runner state
        await Task.Run(async () =>
        {
            var sut = new CorrelationIdService();
            const string parentId = "parent-correlation-id";
            using var _ = sut.SetCorrelationId(parentId);

            // Act — child task inherits the parent's AsyncLocal value
            string? childId = null;
            await Task.Run(() =>
            {
                childId = sut.GetOrCreateCorrelationId();
            }, ct);

            // Assert
            childId.Should().Be(parentId,
                because: "AsyncLocal propagates its value to child async contexts");
        }, ct);
    }
}
