namespace IPS.AutoPost.Core.Migrations.Entities;

/// <summary>
/// EF Core entity for the <c>generic_auth_configuration</c> table.
/// Stores credentials per job, replacing embedded auth in legacy config tables.
/// Supports MDS-style multi-credential scenarios via <see cref="AuthKey"/>.
/// </summary>
public class GenericAuthConfigurationEntity
{
    public int Id { get; set; }

    /// <summary>Foreign key to <c>generic_job_configuration.id</c>.</summary>
    public int JobConfigId { get; set; }

    /// <summary>Auth purpose: 'POST', 'DOWNLOAD', or 'CREDS_BY_COMCODE'.</summary>
    public string AuthPurpose { get; set; } = "POST";

    /// <summary>
    /// Optional discriminator key (e.g. company code length for MDS multi-credential).
    /// </summary>
    public string? AuthKey { get; set; }

    /// <summary>Auth type: 'BASIC', 'OAUTH', 'APIKEY', or 'SOAP'.</summary>
    public string AuthType { get; set; } = string.Empty;

    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ApiKey { get; set; }
    public string? TokenUrl { get; set; }

    /// <summary>AWS Secrets Manager ARN for this credential set.</summary>
    public string? SecretArn { get; set; }

    /// <summary>Extra parameters as JSON.</summary>
    public string? ExtraJson { get; set; }

    // Navigation property
    public GenericJobConfigurationEntity JobConfiguration { get; set; } = null!;
}
