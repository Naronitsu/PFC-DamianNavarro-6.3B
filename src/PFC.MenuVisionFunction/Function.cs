using CloudNative.CloudEvents;
using Google.Cloud.Firestore;
using Google.Cloud.Functions.Framework;
using Google.Cloud.Vision.V1;
using Google.Events.Protobuf.Cloud.PubSub.V1;
using System.Text.Json;

namespace PFC.MenuVisionFunction;

public sealed class Function : ICloudEventFunction<MessagePublishedData>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HttpClient MetadataHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

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

    public async Task HandleAsync(CloudEvent cloudEvent, MessagePublishedData data, CancellationToken cancellationToken)
    {
        var json = data.Message?.TextData;
        if (string.IsNullOrWhiteSpace(json))
            return;

        UploadPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<UploadPayload>(json, JsonOptions);
        }
        catch
        {
            return;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.GcsUri))
            return;

        var projectId =
            Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
            ?? Environment.GetEnvironmentVariable("GCP_PROJECT")
            ?? Environment.GetEnvironmentVariable("GCLOUD_PROJECT");

        if (string.IsNullOrWhiteSpace(projectId))
            projectId = await TryProjectIdFromMetadataAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(projectId))
            throw new InvalidOperationException(
                "Set GOOGLE_CLOUD_PROJECT (or deploy with --set-env-vars GOOGLE_CLOUD_PROJECT=YOUR_PROJECT_ID).");

        var databaseId =
            Environment.GetEnvironmentVariable("FIRESTORE_DATABASE_ID")
            ?? "damian-pfc-firestore";

        var vision = ImageAnnotatorClient.Create();
        var visionResponse = await vision.BatchAnnotateImagesAsync(new[]
        {
            new AnnotateImageRequest
            {
                Image = new Image { Source = new ImageSource { GcsImageUri = payload.GcsUri } },
                Features = { new Feature { Type = Feature.Types.Type.DocumentTextDetection } },
            },
        }, cancellationToken).ConfigureAwait(false);

        var first = visionResponse.Responses.FirstOrDefault();
        if (first?.Error != null)
            throw new InvalidOperationException(first.Error.Message);

        var text = first?.FullTextAnnotation?.Text ?? "";

        var db = new FirestoreDbBuilder
        {
            ProjectId = projectId,
            DatabaseId = databaseId,
        }.Build();

        var restaurantRef = db.Collection("restaurants").Document(payload.RestaurantId);
        var menuRef = restaurantRef.Collection("menus").Document(payload.MenuId);

        await menuRef.SetAsync(new Dictionary<string, object>
        {
            ["ocrText"] = text,
            ["updatedAt"] = FieldValue.ServerTimestamp,
        }, SetOptions.MergeAll, cancellationToken).ConfigureAwait(false);

        await restaurantRef.SetAsync(new Dictionary<string, object>
        {
            ["status"] = "pending",
            ["updatedAt"] = FieldValue.ServerTimestamp,
        }, SetOptions.MergeAll, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class UploadPayload
{
    public string Bucket { get; set; } = "";
    public string ObjectName { get; set; } = "";
    public string GcsUri { get; set; } = "";
    public string RestaurantId { get; set; } = "";
    public string MenuId { get; set; } = "";
    public string ImageId { get; set; } = "";
    public string FileName { get; set; } = "";
}
