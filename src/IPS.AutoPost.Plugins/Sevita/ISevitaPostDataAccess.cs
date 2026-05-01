using System.Data;
using IPS.AutoPost.Plugins.Sevita.Models;

namespace IPS.AutoPost.Plugins.Sevita;

/// <summary>
/// Abstracts all database operations used by <see cref="SevitaPostStrategy"/>
/// so that unit tests can mock them without a real SQL Server connection.
/// </summary>
public interface ISevitaPostDataAccess
{
    /// <summary>
    /// Executes <c>GetSevitaHeaderAndDetailDataByItem</c> and returns the full
    /// header + detail DataSet for a single workitem.
    /// Parameter: @UID (bigint).
    /// Returns DataSet with 2 tables: Table[0] = header, Table[1] = detail rows.
    /// </summary>
    Task<DataSet> GetHeaderAndDetailDataAsync(long itemId, CancellationToken ct);

    /// <summary>
    /// Loads API response type configuration from <c>api_response_configuration</c>
    /// for the given job ID. Used for manual post responses.
    /// </summary>
    Task<List<PostResponseType>> GetApiResponseTypesAsync(int jobId, CancellationToken ct);

    /// <summary>
    /// Sets <c>PostInProcess = 1</c> on the header table row before calling the external API.
    /// </summary>
    Task SetPostInProcessAsync(long itemId, string headerTable, CancellationToken ct);

    /// <summary>
    /// Clears <c>PostInProcess = 0</c> on the header table row in a finally block.
    /// </summary>
    Task ClearPostInProcessAsync(long itemId, string headerTable, CancellationToken ct);

    /// <summary>
    /// Routes a workitem to a target queue by calling <c>WORKITEM_ROUTE</c>.
    /// Parameters: @itemID, @Qid, @userId, @operationType, @comment.
    /// </summary>
    Task RouteWorkitemAsync(
        long itemId,
        int targetQueueId,
        int userId,
        string operationType,
        string comment,
        CancellationToken ct);

    /// <summary>
    /// Inserts an audit log entry by calling <c>GENERALLOG_INSERT</c>.
    /// Parameters: @operationType, @sourceObject, @userID, @comments, @itemID.
    /// </summary>
    Task InsertGeneralLogAsync(
        string operationType,
        string sourceObject,
        int userId,
        string comments,
        long itemId,
        CancellationToken ct);

    /// <summary>
    /// Inserts a row into <c>sevita_posted_records_history</c>.
    /// Always called — even for image-fail early exits.
    /// </summary>
    Task SavePostHistoryAsync(PostHistory history, CancellationToken ct);

    /// <summary>
    /// Calls <c>UpdateSevitaHeaderPostFields</c> SP with @UID parameter to clear
    /// PostInProcess and update post fields on the header record.
    /// </summary>
    Task UpdateHeaderPostFieldsAsync(long itemId, CancellationToken ct);
}
