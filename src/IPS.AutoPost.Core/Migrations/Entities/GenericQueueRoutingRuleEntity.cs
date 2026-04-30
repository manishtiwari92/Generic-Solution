namespace IPS.AutoPost.Core.Migrations.Entities;

/// <summary>
/// EF Core entity for the <c>generic_queue_routing_rules</c> table.
/// Makes queue routing configurable instead of hardcoded per client.
/// </summary>
public class GenericQueueRoutingRuleEntity
{
    public int Id { get; set; }

    /// <summary>Foreign key to <c>generic_job_configuration.id</c>.</summary>
    public int JobConfigId { get; set; }

    /// <summary>
    /// Result type that triggers this routing rule.
    /// Values: 'SUCCESS', 'FAIL_POST', 'FAIL_IMAGE', 'DUPLICATE', 'QUESTION', 'TERMINATED'.
    /// </summary>
    public string ResultType { get; set; } = string.Empty;

    /// <summary>Target queue ID (StatusId) to route the workitem to.</summary>
    public int QueueId { get; set; }

    public bool IsActive { get; set; }

    // Navigation property
    public GenericJobConfigurationEntity JobConfiguration { get; set; } = null!;
}
