using System.Net.Http;
using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PFC.Web.Configuration;
using PFC.Web.Filters;
using PFC.Web.Services;

namespace PFC.Web.Controllers;

[ServiceFilter(typeof(RequireGoogleForUploadFilter))]
public sealed class UploadController : Controller
{
    private readonly UrlSigner _signer;
    private readonly GcpOptions _gcp;
    private readonly MenuUploadNotificationService _menuUploads;

    public UploadController(UrlSigner signer, IOptions<GcpOptions> gcp, MenuUploadNotificationService menuUploads)
    {
        _signer = signer;
        _gcp = gcp.Value;
        _menuUploads = menuUploads;
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewBag.RestaurantId = Guid.NewGuid().ToString("N");
        ViewBag.MenuId = Guid.NewGuid().ToString("N");
        return View();
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Complete([FromBody] CompleteUploadRequest body, CancellationToken cancellationToken)
    {
        if (body is null
            || string.IsNullOrWhiteSpace(body.RestaurantId)
            || string.IsNullOrWhiteSpace(body.MenuId)
            || string.IsNullOrWhiteSpace(body.ObjectName))
            return BadRequest();

        var restaurantName = string.IsNullOrWhiteSpace(body.RestaurantName) ? "Restaurant" : body.RestaurantName.Trim();
        var menuTitle = string.IsNullOrWhiteSpace(body.MenuTitle) ? "Menu" : body.MenuTitle.Trim();
        var fileName = string.IsNullOrWhiteSpace(body.FileName) ? "file" : Path.GetFileName(body.FileName.Trim());

        var imageId = await _menuUploads.RecordAndPublishAsync(
            body.RestaurantId.Trim(),
            body.MenuId.Trim(),
            body.ObjectName.Trim(),
            fileName,
            restaurantName,
            menuTitle,
            cancellationToken).ConfigureAwait(false);

        return Json(new { imageId });
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

public sealed record CompleteUploadRequest(
    string RestaurantId,
    string MenuId,
    string ObjectName,
    string FileName,
    string RestaurantName,
    string MenuTitle);
