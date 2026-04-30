namespace IPS.AutoPost.Core.Models;

/// <summary>
/// S3 credentials and asset API URL loaded from the <c>EdenredApiUrlConfig</c> table
/// at application startup via:
/// <code>SELECT AssetApiUrl, BucketName, S3AccessKey, S3SecretKey, S3Region FROM EdenredApiUrlConfig</code>
/// These credentials are NOT read from Secrets Manager — they are stored in RDS.
/// Used to initialise the <c>S3Utility</c> wrapper for invoice image retrieval.
/// </summary>
public class EdenredApiUrlConfig
{
    /// <summary>
    /// URL of the Edenred asset API (used for image retrieval in some legacy flows).
    /// </summary>
    public string AssetApiUrl { get; set; } = string.Empty;

    /// <summary>S3 bucket name where invoice images (PDFs/TIFFs) are stored.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>AWS access key ID for S3 image retrieval.</summary>
    public string S3AccessKey { get; set; } = string.Empty;

    /// <summary>AWS secret access key for S3 image retrieval.</summary>
    public string S3SecretKey { get; set; } = string.Empty;

    /// <summary>AWS region where the S3 bucket is located (e.g. "us-east-1").</summary>
    public string S3Region { get; set; } = string.Empty;
}
