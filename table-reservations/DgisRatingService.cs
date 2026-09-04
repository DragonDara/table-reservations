using System.Text.Json;

using table_reservations.Services.Tenancy;

public class DgisRatingService
{
    private readonly HttpClient _http;
    private readonly TenantContext _tenant;
    private (double Rating, int ReviewCount)? _cache;
    private DateTime _cacheTime;

    public DgisRatingService(HttpClient http, TenantContext tenant)
    {
        _http = http;
        _tenant = tenant;
    }

    public async Task<(double Rating, int ReviewCount)> GetRatingAsync()
    {
        if (_cache.HasValue && DateTime.UtcNow - _cacheTime < TimeSpan.FromHours(24))
            return _cache.Value;

        var organization = _tenant.Organization
            ?? throw new InvalidOperationException("Tenant must be resolved before loading a rating.");
        var options = organization.Rating;
        if (!options.Enabled)
            throw new InvalidOperationException($"Ratings are not enabled for organization '{organization.Id}'.");
        if (string.IsNullOrWhiteSpace(options.ApifyToken)
            || string.IsNullOrWhiteSpace(options.PlaceUrl))
            throw new InvalidOperationException($"Rating settings are incomplete for organization '{organization.Id}'.");

        var url = "https://api.apify.com/v2/acts/zen-studio~2gis-reviews-scraper/run-sync-get-dataset-items";

        var body = new
        {
            startUrls = new[] { new { url = options.PlaceUrl } },
            maxReviews = 1
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApifyToken);

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Apify returned {(int)response.StatusCode}: {json}");
        }

        using var doc = JsonDocument.Parse(json);
        JsonElement firstItem;

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            if (doc.RootElement.GetArrayLength() == 0)
                throw new InvalidOperationException("Dataset is empty");
            firstItem = doc.RootElement[0];
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
            {
                firstItem = items[0];
            }
            else
            {
                throw new InvalidOperationException("Unexpected JSON structure: " + json);
            }
        }
        else
        {
            throw new InvalidOperationException("Unexpected JSON root kind: " + doc.RootElement.ValueKind);
        }

        var rating = firstItem.GetProperty("placeRating").GetDouble();
        var reviewCount = firstItem.GetProperty("placeReviewCount").GetInt32();

        _cache = (rating, reviewCount);
        _cacheTime = DateTime.UtcNow;

        return _cache.Value;
    }
}
