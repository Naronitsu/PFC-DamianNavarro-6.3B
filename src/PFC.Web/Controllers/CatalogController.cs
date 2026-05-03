using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using PFC.Web.Models;
using PFC.Web.Services;
using PFC.Web.ViewModels;

namespace PFC.Web.Controllers;

public sealed class CatalogController : Controller
{
    private readonly FirestoreDb _db;
    private readonly TranslationProxyService _translate;

    public CatalogController(FirestoreDb db, TranslationProxyService translate)
    {
        _db = db;
        _translate = translate;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] bool includePending, CancellationToken cancellationToken)
    {
        var rows = new List<CatalogMenuRow>();
        var restaurants = await _db.Collection("restaurants").GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        foreach (var rdoc in restaurants.Documents)
        {
            var status = rdoc.ContainsField("status") ? rdoc.GetValue<string>("status") : "";
            var name = rdoc.ContainsField("name") ? rdoc.GetValue<string>("name") : "";

            var allowed =
                status == "ready"
                || status == "confirmed"
                || (includePending && (status == "pending" || string.IsNullOrEmpty(status)));

            if (!allowed)
                continue;

            var menus = await rdoc.Reference.Collection("menus").GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            foreach (var mdoc in menus.Documents)
            {
                var ocr = mdoc.ContainsField("ocrText") ? mdoc.GetValue<string>("ocrText") : "";
                if (string.IsNullOrWhiteSpace(ocr))
                    continue;

                var title = mdoc.ContainsField("title") ? mdoc.GetValue<string>("title") : "";

                rows.Add(new CatalogMenuRow(
                    rdoc.Id,
                    name,
                    mdoc.Id,
                    title,
                    ocr,
                    status));
            }
        }

        ViewBag.IncludePending = includePending;
        return View(rows);
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Translate([FromBody] TranslateMenuRequest body, CancellationToken cancellationToken)
    {
        if (body is null
            || string.IsNullOrWhiteSpace(body.RestaurantId)
            || string.IsNullOrWhiteSpace(body.MenuId)
            || string.IsNullOrWhiteSpace(body.Text)
            || string.IsNullOrWhiteSpace(body.TargetLanguage))
            return BadRequest();

        var result = await _translate.TranslateAsync(
            body.RestaurantId.Trim(),
            body.MenuId.Trim(),
            body.Text.Trim(),
            body.TargetLanguage.Trim(),
            cancellationToken).ConfigureAwait(false);

        if (result is null)
            return StatusCode(502);

        return Json(new { translatedText = result });
    }
}
