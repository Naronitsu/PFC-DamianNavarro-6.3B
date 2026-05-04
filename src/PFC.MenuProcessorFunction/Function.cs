using System.Net.Mime;
using System.Text.Json;
using Google.Cloud.Firestore;
using Google.Cloud.Functions.Framework;
using Microsoft.AspNetCore.Http;

namespace PFC.MenuProcessorFunction;

public sealed class Function : IHttpFunction
{
    private static readonly HttpClient MetadataHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    public async Task HandleAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        var expected = Environment.GetEnvironmentVariable("PROCESSOR_SHARED_SECRET");
        if (!string.IsNullOrEmpty(expected))
        {
            if (!context.Request.Headers.TryGetValue("X-Pfc-Processor-Secret", out var sent)
                || sent.Count == 0
                || sent[0] != expected)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        var projectId =
            Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
            ?? Environment.GetEnvironmentVariable("GCP_PROJECT")
            ?? Environment.GetEnvironmentVariable("GCLOUD_PROJECT");

        if (string.IsNullOrWhiteSpace(projectId))
            projectId = await TryProjectIdFromMetadataAsync(context.RequestAborted).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(projectId))
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = MediaTypeNames.Text.Plain;
            await context.Response.WriteAsync(
                "Set GOOGLE_CLOUD_PROJECT or run on GCP with metadata.",
                context.RequestAborted).ConfigureAwait(false);
            return;
        }

        var databaseId =
            Environment.GetEnvironmentVariable("FIRESTORE_DATABASE_ID")
            ?? "damian-pfc-firestore";

        var doneStatus = Environment.GetEnvironmentVariable("RESTAURANT_DONE_STATUS")?.Trim();
        if (string.IsNullOrEmpty(doneStatus) || (doneStatus != "ready" && doneStatus != "confirmed"))
            doneStatus = "ready";

        var db = new FirestoreDbBuilder
        {
            ProjectId = projectId,
            DatabaseId = databaseId,
        }.Build();

        var pendingSnap = await db.Collection("restaurants")
            .WhereEqualTo("status", "pending")
            .GetSnapshotAsync(context.RequestAborted)
            .ConfigureAwait(false);

        var restaurantsProcessed = 0;
        var menusUpdated = 0;

        foreach (var rdoc in pendingSnap.Documents)
        {
            var menusSnap = await rdoc.Reference.Collection("menus")
                .GetSnapshotAsync(context.RequestAborted)
                .ConfigureAwait(false);

            var touchedMenu = false;

            foreach (var mdoc in menusSnap.Documents)
            {
                var ocr = mdoc.ContainsField("ocrText") ? mdoc.GetValue<string>("ocrText") : "";
                if (string.IsNullOrWhiteSpace(ocr))
                    continue;

                touchedMenu = true;
                var parsed = MenuOcrParser.Parse(ocr);
                var items = parsed.Select(p => new Dictionary<string, object>
                {
                    ["name"] = p.Name,
                    ["price"] = p.Price,
                    ["currency"] = p.Currency,
                }).ToList();

                await mdoc.Reference.SetAsync(
                    new Dictionary<string, object>
                    {
                        ["items"] = items,
                        ["updatedAt"] = FieldValue.ServerTimestamp,
                    },
                    SetOptions.MergeAll,
                    context.RequestAborted).ConfigureAwait(false);

                menusUpdated++;
            }

            if (touchedMenu)
            {
                await rdoc.Reference.SetAsync(
                    new Dictionary<string, object>
                    {
                        ["status"] = doneStatus,
                        ["updatedAt"] = FieldValue.ServerTimestamp,
                    },
                    SetOptions.MergeAll,
                    context.RequestAborted).ConfigureAwait(false);

                restaurantsProcessed++;
            }
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = MediaTypeNames.Application.Json;
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new
            {
                restaurantsProcessed,
                menusUpdated,
                doneStatus,
            }),
            context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task<string?> TryProjectIdFromMetadataAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get,
                "http://metadata.google.internal/computeMetadata/v1/project/project-id");
            req.Headers.TryAddWithoutValidation("Metadata-Flavor", "Google");
            using var resp = await MetadataHttp.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            var id = (await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
            return string.IsNullOrEmpty(id) ? null : id;
        }
        catch
        {
            return null;
        }
    }
}
