using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace RouteGo.App.Infrastructure;

/// <summary>
/// Reads e-tickets from the private S3 bucket Ticket Generator writes them to, at
/// <c>tickets/{bookingId}.pdf</c>. The portal's role may only read: it checks an object is there
/// and signs a short-lived link, nothing else. For a ticket that is not stored yet S3 answers
/// "not found" only when the role may also list the bucket; without that permission it answers
/// "forbidden", which would be shown as storage being unavailable.
/// </summary>
public sealed class S3TicketStorage : ITicketStorage
{
    private const string KeyPrefix = "tickets/";
    private const string PdfType = "application/pdf";

    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ILogger<S3TicketStorage> _logger;

    public S3TicketStorage(IAmazonS3 s3, IConfiguration configuration, ILogger<S3TicketStorage> logger)
    {
        _s3 = s3;
        _bucket = configuration[ConfigKeys.TicketBucket]!;
        _logger = logger;
    }

    public async Task<TicketPresence> CheckAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var request = new GetObjectMetadataRequest { BucketName = _bucket, Key = KeyOf(bookingId) };
        try
        {
            await _s3.GetObjectMetadataAsync(request, cancellationToken);
            return TicketPresence.Stored;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return TicketPresence.NotStored;
        }
        catch (AmazonClientException exception)
        {
            // Not rethrown: the page tells the customer the service is unavailable, and this line is
            // where support learns why (a wrong bucket name, a missing permission, an outage).
            _logger.LogError(exception, "Ticket storage could not be read for booking {BookingId} in bucket {Bucket}", bookingId, _bucket);
            return TicketPresence.Unavailable;
        }
    }

    public string MakeLink(Guid bookingId, DateTimeOffset now)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = KeyOf(bookingId),
            Verb = HttpVerb.GET,
            Expires = now.AddMinutes(ITicketStorage.LinkMinutes).UtcDateTime,
        };
        request.ResponseHeaderOverrides.ContentType = PdfType;
        request.ResponseHeaderOverrides.ContentDisposition = $"inline; filename=\"RouteGo-ticket-{bookingId}.pdf\"";
        return _s3.GetPreSignedURL(request);
    }

    private static string KeyOf(Guid bookingId)
    {
        return $"{KeyPrefix}{bookingId}.pdf";
    }
}
