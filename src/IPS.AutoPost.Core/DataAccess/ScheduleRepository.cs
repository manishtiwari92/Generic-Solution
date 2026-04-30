using System.Data;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace IPS.AutoPost.Core.DataAccess;

/// <summary>
/// Loads execution schedule configuration from <c>generic_execution_schedule</c>
/// using <see cref="SqlHelper"/>.
/// Calls the <c>GetExecutionSchedule</c> stored procedure with the exact parameter
/// names used by the legacy Windows Service implementations.
/// </summary>
public class ScheduleRepository : IScheduleRepository
{
    private readonly string _connectionString;

    public ScheduleRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Workflow")
            ?? throw new InvalidOperationException(
                "Connection string 'Workflow' is not configured. " +
                "Ensure SecretsManagerConfigurationProvider has resolved the /IPS/Common/{env}/Database/Workflow secret.");
    }

    // -----------------------------------------------------------------------
    // GetSchedulesAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <c>GetExecutionSchedule</c> with <c>@file_creation_config_id</c> and
    /// <c>@job_id</c> — the exact parameter names used by the legacy Windows Service
    /// implementations. Returns an empty list when no schedules are configured.
    /// </remarks>
    public async Task<IReadOnlyList<ScheduleConfig>> GetSchedulesAsync(
        int jobConfigId,
        int jobId,
        CancellationToken ct = default)
    {
        var ds = await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            "GetExecutionSchedule",
            ct,
            SqlHelper.Param("@file_creation_config_id", SqlDbType.Int, jobConfigId),
            SqlHelper.Param("@job_id",                  SqlDbType.Int, jobId));

        if (ds.Tables.Count == 0)
            return Array.Empty<ScheduleConfig>();

        var results = new List<ScheduleConfig>(ds.Tables[0].Rows.Count);
        foreach (DataRow row in ds.Tables[0].Rows)
        {
            results.Add(MapRow(row, jobConfigId, jobId));
        }

        return results;
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps a DataRow from the <c>GetExecutionSchedule</c> result set to a
    /// <see cref="ScheduleConfig"/> model.
    /// </summary>
    private static ScheduleConfig MapRow(DataRow row, int jobConfigId, int jobId)
    {
        return new ScheduleConfig
        {
            // id may not be returned by the legacy SP — default to 0 if absent
            Id            = row.Table.Columns.Contains("id")
                                ? Convert.ToInt32(row["id"])
                                : 0,
            JobConfigId   = jobConfigId,
            JobId         = jobId,
            ScheduleType  = row.Table.Columns.Contains("schedule_type")
                                ? row["schedule_type"] as string ?? string.Empty
                                : string.Empty,
            ExecutionTime = row.Table.Columns.Contains("execution_time")
                                ? row["execution_time"] as string
                                : null,
            CronExpression = row.Table.Columns.Contains("cron_expression")
                                ? row["cron_expression"] as string
                                : null,
            IsActive      = row.Table.Columns.Contains("is_active")
                                && Convert.ToBoolean(row["is_active"])
        };
    }
}
