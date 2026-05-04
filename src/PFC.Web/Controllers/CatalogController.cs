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
    public async Task<IActionResult> Index(
        [FromQuery] string? q,
        [FromQuery] string? sort,
        [FromQuery] bool includePending,
        CancellationToken cancellationToken)
    {
        var dishes = new List<CatalogDishRow>();
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
                var items = ReadItems(mdoc);
                if (items.Count == 0)
                    continue;

                var title = mdoc.ContainsField("title") ? mdoc.GetValue<string>("title") : "";
                var canTranslate = status == "ready" || status == "confirmed";

                foreach (var it in items)
                {
                    var line = $"{it.Name}  {it.Price:0.##} {it.Currency}";
                    dishes.Add(new CatalogDishRow(
                        rdoc.Id,
                        name,
                        mdoc.Id,
                        title,
                        it.Name,
                        it.Price,
                        it.Currency,
                        status,
                        line,
                        canTranslate));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim();
            dishes = dishes
                .Where(d =>
                    d.ItemName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || d.RestaurantName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || d.MenuTitle.Contains(needle, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        dishes = (sort ?? "price_asc").ToLowerInvariant() switch
        {
            "price_desc" => dishes.OrderByDescending(d => d.Price).ThenBy(d => d.ItemName).ToList(),
            _ => dishes.OrderBy(d => d.Price).ThenBy(d => d.ItemName).ToList(),
        };

        ViewBag.IncludePending = includePending;
        ViewBag.Query = q ?? "";
        ViewBag.Sort = string.IsNullOrWhiteSpace(sort) ? "price_asc" : sort;
        return View(dishes);
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

    private static List<CatalogMenuItemRow> ReadItems(DocumentSnapshot mdoc)
    {
        var result = new List<CatalogMenuItemRow>();
        if (!mdoc.ContainsField("items"))
            return result;

        try
        {
            var list = mdoc.GetValue<List<Dictionary<string, object>>>("items");
            foreach (var dict in list)
            {
                if (!dict.TryGetValue("name", out var nameObj))
                    continue;
                var itemName = nameObj?.ToString();
                if (string.IsNullOrWhiteSpace(itemName))
                    continue;

                double price = 0;
                if (dict.TryGetValue("price", out var priceObj))
                {
                    price = priceObj switch
                    {
                        double d => d,
                        float f => f,
                        long l => l,
                        int i => i,
                        _ => double.TryParse(priceObj?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0,
                    };
                }

                var currency = dict.TryGetValue("currency", out var cObj) ? (cObj?.ToString() ?? "EUR") : "EUR";
                result.Add(new CatalogMenuItemRow(itemName.Trim(), price, currency));
            }
        }
        catch
        {
        }

        return result;
    }
}
