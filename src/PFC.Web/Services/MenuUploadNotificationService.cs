using System.Text.Json;
using Google.Cloud.Firestore;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Options;
using PFC.Web.Configuration;

namespace PFC.Web.Services;

public sealed class MenuUploadNotificationService
{
    private readonly FirestoreDb _db;
    private readonly PublisherClient _publisher;
    private readonly GcpOptions _gcp;

    public MenuUploadNotificationService(FirestoreDb db, PublisherClient publisher, IOptions<GcpOptions> gcp)
    {
        _db = db;
        _publisher = publisher;
        _gcp = gcp.Value;
    }

    public async Task<string> RecordAndPublishAsync(
        string restaurantId,
        string menuId,
        string objectName,
        string fileName,
        string restaurantName,
        string menuTitle,
        CancellationToken cancellationToken)
    {
        var imageId = Guid.NewGuid().ToString("N");
        var bucket = _gcp.StorageBucket;
        var gcsUri = $"gs://{bucket}/{objectName}";
        var normalizedMenuTitle = menuTitle.Trim();
        var normalizedRestaurantName = restaurantName.Trim();

        var restaurantRef = _db.Collection("restaurants").Document(restaurantId);
        await restaurantRef.SetAsync(new Dictionary<string, object>
        {
            ["name"] = normalizedRestaurantName,
            ["updatedAt"] = FieldValue.ServerTimestamp,
        }, SetOptions.MergeAll, cancellationToken).ConfigureAwait(false);

        var matchingRestaurants = await _db.Collection("restaurants")
            .WhereEqualTo("name", normalizedRestaurantName)
            .GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var deactivateBatch = _db.StartBatch();
        foreach (var rdoc in matchingRestaurants.Documents)
        {
            var menus = await rdoc.Reference.Collection("menus")
                .WhereEqualTo("title", normalizedMenuTitle)
                .GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

            foreach (var existing in menus.Documents)
            {
                if (rdoc.Id == restaurantId && existing.Id == menuId)
                    continue;

                var currentlyActive = !existing.ContainsField("isActive") || existing.GetValue<bool>("isActive");
                if (!currentlyActive)
                    continue;

                deactivateBatch.Set(existing.Reference, new Dictionary<string, object>
                {
                    ["isActive"] = false,
                    ["deactivatedAt"] = FieldValue.ServerTimestamp,
                    ["updatedAt"] = FieldValue.ServerTimestamp,
                }, SetOptions.MergeAll);
            }
        }
        await deactivateBatch.CommitAsync(cancellationToken).ConfigureAwait(false);

        var menuRef = restaurantRef.Collection("menus").Document(menuId);
        await menuRef.SetAsync(new Dictionary<string, object>
        {
            ["title"] = normalizedMenuTitle,
            ["isActive"] = true,
            ["activatedAt"] = FieldValue.ServerTimestamp,
            ["updatedAt"] = FieldValue.ServerTimestamp,
            ["translationEpoch"] = FieldValue.Increment(1L),
        }, SetOptions.MergeAll, cancellationToken).ConfigureAwait(false);

        var imageRef = menuRef.Collection("images").Document(imageId);
        await imageRef.SetAsync(new Dictionary<string, object>
        {
            ["bucket"] = bucket,
            ["objectName"] = objectName,
            ["gcsUri"] = gcsUri,
            ["fileName"] = fileName,
            ["uploadedAt"] = FieldValue.ServerTimestamp,
        }, SetOptions.Overwrite, cancellationToken).ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(new
        {
            bucket,
            objectName,
            gcsUri,
            restaurantId,
            menuId,
            imageId,
            fileName,
        });

        await _publisher.PublishAsync(payload, System.Text.Encoding.UTF8).ConfigureAwait(false);
        return imageId;
    }
}
