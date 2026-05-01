using System.Data;

namespace IPS.AutoPost.Plugins.InvitedClub;

/// <summary>
/// Abstracts all database operations used by <see cref="InvitedClubRetryService"/>
/// so that unit tests can mock them without a real SQL Server connection.
/// </summary>
public interface IInvitedClubRetryDataAccess
{
    /// <summary>
    /// Executes <c>InvitedClub_GetFailedImagesData</c> and returns the result DataTable.
    /// Parameters: @HeaderTable (varchar), @ImagePostRetryLimit (int), @InvitedFailPostQueueId (bigint).
    /// </summary>
    Task<DataTable> GetFailedImagesDataAsync(
        string headerTable,
        int imagePostRetryLimit,
        int invitedFailPostQueueId,
        CancellationToken ct);

    /// <summary>
    /// Updates <c>AttachedDocumentId</c> on the header table row after a successful
    /// attachment retry POST (HTTP 201).
    /// </summary>
    Task UpdateAttachedDocumentIdAsync(
        long itemId,
        string attachedDocumentId,
        string headerTable,
        CancellationToken ct);

    /// <summary>
    /// Increments <c>ImagePostRetryCount</c> on the header table row.
    /// Always called regardless of success or failure.
    /// </summary>
    Task IncrementImagePostRetryCountAsync(
        long itemId,
        string headerTable,
        CancellationToken ct);

    /// <summary>
    /// Routes a workitem to a target queue by calling <c>WORKITEM_ROUTE</c>.
    /// </summary>
    Task RouteWorkitemAsync(
        long itemId,
        int targetQueueId,
        int userId,
        string operationType,
        string comment,
        CancellationToken ct);
}
