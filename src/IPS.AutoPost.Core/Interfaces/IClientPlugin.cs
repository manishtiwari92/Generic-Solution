using IPS.AutoPost.Core.Models;

namespace IPS.AutoPost.Core.Interfaces;

/// <summary>
/// Contract that every client plugin must implement.
/// The Core_Engine calls these methods — all client-specific ERP logic lives here.
/// Adding a new client requires only a new class implementing this interface; no Core changes needed.
/// </summary>
public interface IClientPlugin
{
    /// <summary>
    /// Unique identifier that matches the <c>client_type</c> column in
    /// <c>generic_job_configuration</c> (e.g. "INVITEDCLUB", "SEVITA").
    /// Case-insensitive comparison is used by <see cref="Engine.PluginRegistry"/>.
    /// </summary>
    string ClientType { get; }

    /// <summary>
    /// Called ONCE before the workitem loop begins for a batch.
    /// Use for batch-level pre-loading that is too expensive to repeat per workitem.
    /// <example>
    /// Sevita loads <c>ValidIds</c> (VendorIds + EmployeeIds) from the database here
    /// so every workitem in the batch can validate against the same in-memory set.
    /// </example>
    /// Default implementation is a no-op — plugins that need no pre-loading do not
    /// need to override this method.
    /// </summary>
    Task OnBeforePostAsync(GenericJobConfig config, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Processes all workitems in a batch.
    /// Called by <c>AutoPostOrchestrator.ExecutePostBatchAsync</c> after
    /// <see cref="OnBeforePostAsync"/> completes.
    /// The plugin is responsible for:
    /// <list type="bullet">
    ///   <item>Setting <c>PostInProcess = 1</c> before calling the external API.</item>
    ///   <item>Routing each workitem to the correct queue via <c>WORKITEM_ROUTE</c>.</item>
    ///   <item>Writing client-specific history records.</item>
    ///   <item>Clearing <c>PostInProcess</c> in a <c>finally</c> block via
    ///         <see cref="ClearPostInProcessAsync"/>.</item>
    /// </list>
    /// </summary>
    Task<PostBatchResult> ExecutePostAsync(
        GenericJobConfig config,
        PostContext context,
        CancellationToken ct);

    /// <summary>
    /// Downloads feed data (vendor lists, COA, supplier addresses, etc.).
    /// Default implementation returns <see cref="FeedResult.NotApplicable()"/> so that
    /// plugins with no feed download step (e.g. <c>SevitaPlugin</c>) do not need to
    /// override this method. The Core_Engine skips feed processing when
    /// <c>FeedResult.IsApplicable == false</c>.
    /// </summary>
    Task<FeedResult> ExecuteFeedDownloadAsync(
        GenericJobConfig config,
        FeedContext context,
        CancellationToken ct)
        => Task.FromResult(FeedResult.NotApplicable());

    /// <summary>
    /// Clears the <c>PostInProcess</c> flag after a workitem finishes processing
    /// (success or failure). Always called from a <c>finally</c> block.
    /// <para>
    /// Default: executes <c>UPDATE {header_table} SET PostInProcess=0 WHERE UID=@uid</c>
    /// directly via <see cref="IRoutingRepository.ClearPostInProcessAsync"/>.
    /// </para>
    /// <para>
    /// Override for clients that use a stored procedure instead of a direct UPDATE.
    /// Example: Sevita overrides this to call <c>UpdateSevitaHeaderPostFields(@UID)</c>.
    /// </para>
    /// </summary>
    Task ClearPostInProcessAsync(
        long itemId,
        GenericJobConfig config,
        IRoutingRepository routingRepo,
        CancellationToken ct)
        => routingRepo.ClearPostInProcessAsync(itemId, config.HeaderTable, ct);
}
