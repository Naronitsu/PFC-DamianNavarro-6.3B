using System.Net.Http;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PFC.Web.Configuration;

namespace PFC.Web.Controllers;

public sealed class UploadController : Controller
{
    private readonly UrlSigner _signer;
    private readonly GcpOptions _gcp;

    public UploadController(UrlSigner signer, IOptions<GcpOptions> gcp)
    {
        _signer = signer;
        _gcp = gcp.Value;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Sign([FromBody] SignUploadRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.FileName) || string.IsNullOrWhiteSpace(body.ContentType))
            return BadRequest();

        var safeName = Path.GetFileName(body.FileName);
        if (string.IsNullOrEmpty(safeName))
            return BadRequest();

        var objectName = $"uploads/{Guid.NewGuid():N}-{safeName}";
        var bucket = _gcp.StorageBucket;
        if (string.IsNullOrWhiteSpace(bucket))
            return StatusCode(500);

        var template = UrlSigner.RequestTemplate
            .FromBucket(bucket)
            .WithObjectName(objectName)
            .WithHttpMethod(HttpMethod.Put)
            .WithContentHeaders(new[]
            {
                new KeyValuePair<string, IEnumerable<string>>("Content-Type", new[] { body.ContentType }),
            });

        var options = UrlSigner.Options
            .FromDuration(TimeSpan.FromMinutes(15))
            .WithSigningVersion(SigningVersion.V4);

        var uploadUrl = await _signer.SignAsync(template, options, cancellationToken).ConfigureAwait(false);
        return Json(new { uploadUrl, objectName });
    }
}

public sealed record SignUploadRequest(string FileName, string ContentType);
