using IPS.AutoPost.Core.Models;

namespace IPS.AutoPost.Core.Engine;

/// <summary>
/// Determines whether a scheduled job should execute based on the last execution
/// time and the configured schedule windows.
/// </summary>
/// <remarks>
/// The 30-minute window logic: a job is eligible to run when:
/// <list type="bullet">
///   <item>At least 30 minutes have elapsed since <c>LastPostTime</c>, AND</item>
///   <item>The current UTC time falls within a 30-minute window of a configured
///         <c>execution_time</c> (HH:mm format).</item>
/// </list>
/// Jobs using <c>cron_expression</c> format are driven entirely by EventBridge
/// and do not use this window check.
/// Full implementation is in task 6.7.
/// </remarks>
public class SchedulerService
{
    private const int WindowMinutes = 30;

    /// <summary>
    /// Returns the current UTC time. Overridable in tests to inject a deterministic clock.
    /// </summary>
    protected virtual DateTime UtcNow => DateTime.UtcNow;

    /// <summary>
    /// Returns <c>true</c> when the job should execute based on the last post time
    /// and the configured schedule windows.
    /// </summary>
    /// <param name="lastPostTime">
    /// UTC timestamp of the last successful post run.
    /// <see cref="DateTime.MinValue"/> means the job has never run.
    /// </param>
    /// <param name="schedules">
    /// Active schedule configurations for this job.
    /// An empty list means no schedule is configured — the job should always execute
    /// when triggered (e.g. manual posts or cron-only jobs).
    /// </param>
    public virtual bool ShouldExecute(DateTime lastPostTime, IReadOnlyList<ScheduleConfig> schedules)
    {
        // No schedules configured — always execute (cron-driven or manual)
        if (schedules.Count == 0)
            return true;

        var now = UtcNow;

        // Must be at least WindowMinutes since the last run to prevent duplicate execution
        if (lastPostTime != DateTime.MinValue &&
            (now - lastPostTime).TotalMinutes < WindowMinutes)
        {
            return false;
        }

        // Check if current time falls within any configured execution window
        foreach (var schedule in schedules)
        {
            if (string.IsNullOrWhiteSpace(schedule.ExecutionTime))
                continue;

            if (!TimeOnly.TryParseExact(
                    schedule.ExecutionTime,
                    ["HH:mm", "H:mm", "HH:mm:ss"],
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var scheduledTime))
                continue;

            var nowTime = TimeOnly.FromDateTime(now);
            var windowStart = scheduledTime;
            var windowEnd = scheduledTime.AddMinutes(WindowMinutes);

            if (nowTime >= windowStart && nowTime < windowEnd)
                return true;
        }

        return false;
    }
}
