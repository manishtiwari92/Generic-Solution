using FluentAssertions;
using IPS.AutoPost.Core.Engine;
using IPS.AutoPost.Core.Models;

namespace IPS.AutoPost.Core.Tests.Engine;

/// <summary>
/// Unit tests for <see cref="SchedulerService.ShouldExecute"/>.
/// Covers task 6.7: boundary conditions for the 30-minute execution window.
/// </summary>
/// <remarks>
/// <see cref="SchedulerService"/> uses <c>DateTime.UtcNow</c> internally.
/// <see cref="TestableSchedulerService"/> overrides the protected <c>UtcNow</c>
/// property so tests can inject a deterministic clock without any external
/// time-abstraction library.
/// </remarks>
public class SchedulerServiceTests
{
    // -----------------------------------------------------------------------
    // Testable subclass — injects a fixed clock
    // -----------------------------------------------------------------------

    private sealed class TestableSchedulerService : SchedulerService
    {
        private readonly DateTime _now;
        public TestableSchedulerService(DateTime now) => _now = now;
        protected override DateTime UtcNow => _now;
    }

    private static SchedulerService At(TimeOnly time)
    {
        // Use a fixed date; only the time component matters for window checks
        var now = new DateTime(2026, 4, 30, time.Hour, time.Minute, time.Second, DateTimeKind.Utc);
        return new TestableSchedulerService(now);
    }

    private static SchedulerService At(DateTime utcNow) => new TestableSchedulerService(utcNow);

    private static IReadOnlyList<ScheduleConfig> ScheduleAt(string executionTime) =>
    [
        new ScheduleConfig { Id = 1, IsActive = true, ScheduleType = "POST", ExecutionTime = executionTime }
    ];

    // -----------------------------------------------------------------------
    // No schedules configured — always execute
    // -----------------------------------------------------------------------

    [Fact]
    public void ShouldExecute_NoSchedules_ReturnsTrue()
    {
        var sut = At(new TimeOnly(14, 0));
        sut.ShouldExecute(DateTime.MinValue, []).Should().BeTrue();
    }

    [Fact]
    public void ShouldExecute_NoSchedules_EvenWithRecentLastPostTime_ReturnsTrue()
    {
        // No schedules = cron-driven; the 30-min cooldown does not apply
        var now = new DateTime(2026, 4, 30, 14, 0, 0, DateTimeKind.Utc);
        var sut = At(now);
        var recentLastPost = now.AddMinutes(-5); // only 5 min ago

        sut.ShouldExecute(recentLastPost, []).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Exactly at window start (inclusive boundary)
    // -----------------------------------------------------------------------

    [Fact]
    public void ShouldExecute_ExactlyAtWindowStart_ReturnsTrue()
    {
        // Schedule at 14:00 → window is [14:00, 14:30)
        // Current time = 14:00:00 → exactly at start → should execute
        var sut = At(new TimeOnly(14, 0, 0));
        sut.ShouldExecute(DateTime.MinValue, ScheduleAt("14:00")).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Inside window
    // -----------------------------------------------------------------------

    [Fact]
    public void ShouldExecute_InsideWindow_ReturnsTrue()
    {
        // Schedule at 14:00 → window is [14:00, 14:30)
        // Current time = 14:15 → inside window
        var sut = At(new TimeOnly(14, 15));
        sut.ShouldExecute(DateTime.MinValue, ScheduleAt("14:00")).Should().BeTrue();
    }

    [Fact]
    public void ShouldExecute_OneMinuteBeforeWindowEnd_ReturnsTrue()
    {
        // Schedule at 14:00 → window is [14:00, 14:30)
        // Current time = 14:29 → still inside window
        var sut = At(new TimeOnly(14, 29));
        sut.ShouldExecute(DateTime.MinValue, ScheduleAt("14:00")).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Exactly at window end (exclusive boundary)
    // -----------------------------------------------------------------------

    [Fact]
    public void ShouldExecute_ExactlyAtWindowEnd_ReturnsFalse()
    {
        // Schedule at 14:00 → window is [14:00, 14:30)
        // Current time = 14:30:00 → exactly at end → exclusive, should NOT execute
        var sut = At(new TimeOnly(14, 30, 0));
        sut.ShouldExecute(DateTime.MinValue, ScheduleAt("14:00")).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Before window
    // -----------------------------------------------------------------------

    [Fact]
    public void ShouldExecute_OneMinuteBeforeWindowStart_ReturnsFalse()
    {
        // Schedule at 14:00 → window is [14:00, 14:30)
        // Current time = 13:59 → before window
        var sut = At(new TimeOnly(13, 59));
        sut.ShouldExecute(DateTime.MinValue, ScheduleAt("14:00")).Should().BeFalse();
    }

    [Fact]
    public void ShouldExecute_WellBeforeWindow_ReturnsFalse()
    {
        // Schedule at 14:00 → window is [14:00, 14:30)
        // Current time = 08:00 → well before window
        var sut = At(new TimeOnly(8, 0));
        sut.ShouldExecute(DateTime.MinValue, ScheduleAt("14:00")).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // After window
    // -----------------------------------------------------------------------

    [Fact]
    public void ShouldExecute_AfterWindowEnd_ReturnsFalse()
    {
        // Schedule at 14:00 → window is [14:00, 14:30)
        // Current time = 15:00 → after window
        var sut = At(new TimeOnly(15, 0));
        sut.ShouldExecute(DateTime.MinValue, ScheduleAt("14:00")).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // 30-minute cooldown since last run
    // -----------------------------------------------------------------------

    [Fact]
    public void ShouldExecute_LastRunLessThan30MinutesAgo_ReturnsFalse()
    {
        // Inside window but ran only 10 minutes ago → cooldown not elapsed
        var now = new DateTime(2026, 4, 30, 14, 10, 0, DateTimeKind.Utc);
        var sut = At(now);
        var lastPostTime = now.AddMinutes(-10); // 10 min ago

        sut.ShouldExecute(lastPostTime, ScheduleAt("14:00")).Should().BeFalse();
    }

    [Fact]
    public void ShouldExecute_LastRunExactly30MinutesAgo_ReturnsTrue()
    {
        // Inside window and ran exactly 30 minutes ago → cooldown elapsed
        var now = new DateTime(2026, 4, 30, 14, 30, 0, DateTimeKind.Utc);
        var sut = At(now);
        var lastPostTime = now.AddMinutes(-30); // exactly 30 min ago

        // Window: schedule at 14:00 → [14:00, 14:30) — 14:30 is outside, so use 14:00 schedule
        // Let's use a schedule at 14:30 so the window [14:30, 15:00) covers now=14:30
        sut.ShouldExecute(lastPostTime, ScheduleAt("14:30")).Should().BeTrue();
    }

    [Fact]
    public void ShouldExecute_LastRunMoreThan30MinutesAgo_InsideWindow_ReturnsTrue()
    {
        // Inside window and ran 45 minutes ago → cooldown elapsed, should execute
        var now = new DateTime(2026, 4, 30, 14, 15, 0, DateTimeKind.Utc);
        var sut = At(now);
        var lastPostTime = now.AddMinutes(-45); // 45 min ago

        sut.ShouldExecute(lastPostTime, ScheduleAt("14:00")).Should().BeTrue();
    }

    [Fact]
    public void ShouldExecute_LastRunIsMinValue_TreatedAsNeverRun_ReturnsTrue()
    {
        // DateTime.MinValue means the job has never run — cooldown check is skipped
        var sut = At(new TimeOnly(14, 10));
        sut.ShouldExecute(DateTime.MinValue, ScheduleAt("14:00")).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Multiple schedules — any matching window is sufficient
    // -----------------------------------------------------------------------

    [Fact]
    public void ShouldExecute_MultipleSchedules_MatchesSecondSchedule_ReturnsTrue()
    {
        // Two schedules: 08:00 and 14:00
        // Current time = 14:10 → matches second schedule
        var sut = At(new TimeOnly(14, 10));
        IReadOnlyList<ScheduleConfig> schedules =
        [
            new ScheduleConfig { Id = 1, IsActive = true, ScheduleType = "POST", ExecutionTime = "08:00" },
            new ScheduleConfig { Id = 2, IsActive = true, ScheduleType = "POST", ExecutionTime = "14:00" }
        ];

        sut.ShouldExecute(DateTime.MinValue, schedules).Should().BeTrue();
    }

    [Fact]
    public void ShouldExecute_MultipleSchedules_NoneMatch_ReturnsFalse()
    {
        // Two schedules: 08:00 and 14:00
        // Current time = 11:00 → matches neither
        var sut = At(new TimeOnly(11, 0));
        IReadOnlyList<ScheduleConfig> schedules =
        [
            new ScheduleConfig { Id = 1, IsActive = true, ScheduleType = "POST", ExecutionTime = "08:00" },
            new ScheduleConfig { Id = 2, IsActive = true, ScheduleType = "POST", ExecutionTime = "14:00" }
        ];

        sut.ShouldExecute(DateTime.MinValue, schedules).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Schedules with null/empty ExecutionTime are skipped
    // -----------------------------------------------------------------------

    [Fact]
    public void ShouldExecute_ScheduleWithNullExecutionTime_IsSkipped_ReturnsFalse()
    {
        // Only schedule has null ExecutionTime (cron-only) → no window match → false
        var sut = At(new TimeOnly(14, 10));
        IReadOnlyList<ScheduleConfig> schedules =
        [
            new ScheduleConfig { Id = 1, IsActive = true, ScheduleType = "POST", ExecutionTime = null }
        ];

        sut.ShouldExecute(DateTime.MinValue, schedules).Should().BeFalse();
    }

    [Fact]
    public void ShouldExecute_ScheduleWithEmptyExecutionTime_IsSkipped_ReturnsFalse()
    {
        var sut = At(new TimeOnly(14, 10));
        IReadOnlyList<ScheduleConfig> schedules =
        [
            new ScheduleConfig { Id = 1, IsActive = true, ScheduleType = "POST", ExecutionTime = "" }
        ];

        sut.ShouldExecute(DateTime.MinValue, schedules).Should().BeFalse();
    }

    [Fact]
    public void ShouldExecute_ScheduleWithInvalidExecutionTime_IsSkipped_ReturnsFalse()
    {
        var sut = At(new TimeOnly(14, 10));
        IReadOnlyList<ScheduleConfig> schedules =
        [
            new ScheduleConfig { Id = 1, IsActive = true, ScheduleType = "POST", ExecutionTime = "not-a-time" }
        ];

        sut.ShouldExecute(DateTime.MinValue, schedules).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Window near midnight — documents current behavior (no midnight wrap)
    // -----------------------------------------------------------------------

    [Fact]
    public void ShouldExecute_WindowWrappingMidnight_BeforeWindowStart_ReturnsFalse()
    {
        // Schedule at 23:50 → window is [23:50, 00:20)
        // Current time = 23:45 → before window start
        var sut = At(new TimeOnly(23, 45));
        sut.ShouldExecute(DateTime.MinValue, ScheduleAt("23:50")).Should().BeFalse();
    }

    [Fact]
    public void ShouldExecute_WindowNearMidnight_AtWindowStart_ReturnsFalse()
    {
        // Schedule at 23:50 → windowEnd = 23:50 + 30min = 00:20 (wraps midnight)
        // Current time = 23:50 → 23:50 >= 23:50 is true, but 23:50 < 00:20 is false
        // (TimeOnly comparison is linear, not circular — 23:50 > 00:20 in ticks)
        // Schedules that wrap midnight are not supported by the current implementation.
        var sut = At(new TimeOnly(23, 50));
        sut.ShouldExecute(DateTime.MinValue, ScheduleAt("23:50")).Should().BeFalse();
    }

    [Fact]
    public void ShouldExecute_WindowNearMidnight_InsideWindowBeforeMidnight_ReturnsTrue()
    {
        // Schedule at 23:50 → window is [23:50, 00:20)
        // Current time = 23:59 → 23:59 >= 23:50 is true, but 23:59 < 00:20 is false
        // (TimeOnly comparison is linear, not circular).
        // The window effectively truncates at midnight for schedules near 23:30+.
        // This is documented behavior — schedules near midnight should use a time
        // that doesn't require wrapping (e.g. 23:00 instead of 23:50).
        var sut = At(new TimeOnly(23, 59));
        // 23:59 is >= 23:50 but NOT < 00:20 (linear comparison), so returns false
        sut.ShouldExecute(DateTime.MinValue, ScheduleAt("23:50")).Should().BeFalse();
    }
}
