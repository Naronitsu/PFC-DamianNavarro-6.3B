using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PFC.Web.Configuration;

namespace PFC.Web.Services;

public sealed class TranslationProxyService
{
    private static readonly JsonSerializerOptions JsonDeserialize = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly FirestoreDb _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly GcpOptions _gcp;

    public TranslationProxyService(
        FirestoreDb db,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<GcpOptions> gcp)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _gcp = gcp.Value;
    }

    public async Task<string?> TranslateAsync(
        string restaurantId,
        string menuId,
        string text,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_gcp.TranslateFunctionUrl))
            throw new InvalidOperationException("Gcp:TranslateFunctionUrl is not set.");

        var menuRef = _db.Collection("restaurants").Document(restaurantId).Collection("menus").Document(menuId);
        var menuSnap = await menuRef.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!menuSnap.Exists)
            return null;

        Timestamp? ts = menuSnap.UpdateTime;
        if (menuSnap.ContainsField("updatedAt"))
            ts = menuSnap.GetValue<Timestamp>("updatedAt");

        var updatedAt = ts != null
            ? new DateTimeOffset(DateTime.SpecifyKind(ts.Value.ToDateTime(), DateTimeKind.Utc))
            : DateTimeOffset.MinValue;

        var epoch = menuSnap.ContainsField("translationEpoch") ? menuSnap.GetValue<long>("translationEpoch") : 0L;

        var cacheKey = BuildCacheKey(restaurantId, menuId, updatedAt, epoch, targetLanguage, text);
        if (_cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
            return cached;

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        using var response = await client.PostAsJsonAsync(
                _gcp.TranslateFunctionUrl,
                new { text, targetLanguage },
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<TranslateResponseDto>(
            stream,
            JsonDeserialize,
            cancellationToken).ConfigureAwait(false);

        var translated = dto?.TranslatedText ?? "";
        _cache.Set(cacheKey, translated, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12),
        });

        return translated;
    }

    private static string BuildCacheKey(
        string restaurantId,
        string menuId,
        DateTimeOffset menuUpdatedAt,
        long translationEpoch,
        string targetLanguage,
        string text)
    {
        var raw = $"{restaurantId}|{menuId}|{menuUpdatedAt.UtcTicks}|{translationEpoch}|{targetLanguage}|{text}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return $"tr:{hash}";
    }

    private sealed class TranslateResponseDto
    {
        public string TranslatedText { get; set; } = "";
    }
}
