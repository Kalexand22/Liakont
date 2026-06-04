namespace Liakont.Modules.Archive.Stores.S3;

using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

/// <summary>
/// Implémentation AWSSDK de <see cref="IS3BlobClient"/> au-dessus de <see cref="IAmazonS3"/> (ADR-0009).
/// Seule classe qui dépend du SDK ; elle n'est exercée que par un test de staging sur un backend réel
/// (Amazon/MinIO/OVH…), hors CI (blueprint §9). Quand l'Object Lock est demandé, l'objet est verrouillé en
/// mode CONFORMITÉ pour la durée de rétention fiscale — protection NATIVE en plus de la chaîne de hashes.
/// </summary>
public sealed class AwsS3BlobClient : IS3BlobClient
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;
    private readonly int _retentionYears;

    public AwsS3BlobClient(IAmazonS3 s3, IOptions<S3ArchiveStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _s3 = s3;
        _bucketName = options.Value.BucketName;
        _retentionYears = options.Value.ObjectLockRetentionYears;
    }

    public async Task PutAsync(string key, byte[] content, bool applyObjectLock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var stream = new MemoryStream(content);
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = stream,
            AutoCloseStream = false,
        };

        if (applyObjectLock)
        {
            request.ObjectLockMode = ObjectLockMode.Compliance;
            request.ObjectLockRetainUntilDate = DateTime.UtcNow.AddYears(_retentionYears);
        }

        await _s3.PutObjectAsync(request, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = _bucketName, Key = key },
                cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<byte[]?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            using GetObjectResponse response = await _s3.GetObjectAsync(
                new GetObjectRequest { BucketName = _bucketName, Key = key },
                cancellationToken);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
