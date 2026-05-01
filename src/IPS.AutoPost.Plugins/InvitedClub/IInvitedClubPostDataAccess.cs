using System.Data;
using IPS.AutoPost.Plugins.InvitedClub.Models;

namespace IPS.AutoPost.Plugins.InvitedClub;

/// <summary>
/// Abstracts all database operations used by <see cref="InvitedClubPostStrategy"/>
/// so that unit tests can mock them without a real SQL Server connection.
/// </summary>
public interface IInvitedClubPostDataAccess
{
    /// <summary>
    /// Executes <c>InvitedClub_GetHeaderAndDetailData</c> and returns the full
    /// header + detail DataSet for a single workitem.
    /// Parameter: @UID (bigint).
    /// </summary>
    Task<DataSet> GetHeaderAndDetailDataAsync(long itemId, CancellationToken ct);

    /// <summary>
    /// Loads email configuration for the given config ID via
    /// <c>GetInvitedClubsEmailConfigPerJob</c>.
    /// </summary>
    Task<DataTable> GetEmailConfigAsync(int configId, CancellationToken ct);

    /// <summary>
    /// Loads API response type configuration from <c>api_response_configuration</c>
    /// for the given job ID. Used for manual post responses.
    /// </summary>
    Task<List<APIResponseType>> GetApiResponseTypesAsync(int jobId, CancellationToken ct);

    /// <summary>
    /// Sets <c>PostInProcess = 1</c> on the header table row before calling the external API.
    /// </summary>
    Task SetPostInProcessAsync(long itemId, string headerTable, CancellationToken ct);

    /// <summary>
    /// Clears <c>PostInProcess = 0</c> on the header table row in a finally block.
    /// </summary>
    Task ClearPostInProcessAsync(long itemId, string headerTable, CancellationToken ct);

    /// <summary>
    /// Updates <c>InvoiceId</c> on the header table row after a successful invoice POST (HTTP 201).
    /// </summary>
    Task UpdateInvoiceIdAsync(long itemId, string invoiceId, string headerTable, CancellationToken ct);

    /// <summary>
    /// Updates <c>AttachedDocumentId</c> on the header table row after a successful attachment POST (HTTP 201).
    /// </summary>
    Task UpdateAttachedDocumentIdAsync(long itemId, string attachedDocumentId, string headerTable, CancellationToken ct);

    /// <summary>
    /// Sets <c>GlDate = NULL</c> on the header table row when the invoice POST fails.
    /// Calls <c>UPDATE WFInvitedClubsIndexHeader SET GlDate = NULL WHERE UID = @UID</c>.
    /// </summary>
    Task UpdateGlDateValueAsync(long itemId, string headerTable, CancellationToken ct);

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
    /// Inserts a row into <c>post_to_invitedclub_history</c>.
    /// Only called when at least one Oracle Fusion API call was attempted.
    /// </summary>
    Task SavePostHistoryAsync(PostHistory history, CancellationToken ct);
}
