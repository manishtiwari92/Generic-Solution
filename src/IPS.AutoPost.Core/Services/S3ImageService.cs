using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using IPS.AutoPost.Core.Models;
using Microsoft.Extensions.Logging;

namespace IPS.AutoPost.Core.Services;

/// <summary>
/// Wraps the AWS S3 SDK to provide image retrieval and file upload operations.
/// Credentials and bucket configuration are supplied per-call via <see cref="EdenredApiUrlConfig"/>
/// (loaded from RDS at startup — not from Secrets Manager).
/// </summary>
public sealed class S3ImageService
{
    private readonly ILogger<S3ImageService> _logger;

    public S3ImageService(ILogger<S3ImageService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Downloads an object from S3 and returns it as a base64-encoded string.
    /// </summary>
    /// <param name="s3Key">The S3 object key to retrieve.</param>
    /// <param name="s3Config">S3 credentials and bucket configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A base64-encoded string of the object's content, or <c>null</c> if the object
    /// could not be retrieved (e.g. key not found, access denied).
    /// Callers should treat a <c>null</c> return as image-not-found.
    /// </returns>
    public async Task<string?> GetBase64ImageAsync(
        string s3Key,
        EdenredApiUrlConfig s3Config,
        CancellationToken ct = default)
    {
        try
        {
            var credentials = new BasicAWSCredentials(s3Config.S3AccessKey, s3Config.S3SecretKey);
            var region      = RegionEndpoint.GetBySystemName(s3Config.S3Region);

            using var client = new AmazonS3Client(credentials, region);

            var request = new GetObjectRequest
            {
                BucketName = s3Config.BucketName,
                Key        = s3Key
            };

            using var response = await client.GetObjectAsync(request, ct).ConfigureAwait(false);
            using var memoryStream = new MemoryStream();

            await response.ResponseStream.CopyToAsync(memoryStream, ct).ConfigureAwait(false);

            return Convert.ToBase64String(memoryStream.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to retrieve S3 object. Bucket: {BucketName}, Key: {S3Key}",
                s3Config.BucketName,
                s3Key);

            return null;
        }
    }

    /// <summary>
    /// Uploads a local file to S3.
    /// </summary>
    /// <param name="localFilePath">Absolute or relative path to the local file to upload.</param>
    /// <param name="s3Key">The S3 object key (destination path within the bucket).</param>
    /// <param name="s3Config">S3 credentials and bucket configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="Exception">
    /// Rethrows any exception that occurs during the upload so the caller can handle or
    /// surface the failure appropriately.
    /// </exception>
    public async Task UploadFileAsync(
        string localFilePath,
        string s3Key,
        EdenredApiUrlConfig s3Config,
        CancellationToken ct = default)
    {
        try
        {
            var credentials = new BasicAWSCredentials(s3Config.S3AccessKey, s3Config.S3SecretKey);
            var region      = RegionEndpoint.GetBySystemName(s3Config.S3Region);

            using var client = new AmazonS3Client(credentials, region);

            var request = new PutObjectRequest
            {
                BucketName = s3Config.BucketName,
                Key        = s3Key,
                FilePath   = localFilePath
            };

            await client.PutObjectAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to upload file to S3. Bucket: {BucketName}, Key: {S3Key}, LocalPath: {LocalFilePath}",
                s3Config.BucketName,
                s3Key,
                localFilePath);

            throw;
        }
    }
}
