using System.Text.Json;

public class DgisRatingService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private (double Rating, int ReviewCount)? _cache;
    private DateTime _cacheTime;

    public DgisRatingService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    public async Task<(double Rating, int ReviewCount)> GetRatingAsync()
    {
        if (_cache.HasValue && DateTime.UtcNow - _cacheTime < TimeSpan.FromHours(24))
            return _cache.Value;

        var token = _config["Apify:Token"];
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Apify:Token не задан в конфигурации (user-secrets / appsettings / переменная окружения Apify__Token).");

        var url = "https://api.apify.com/v2/acts/zen-studio~2gis-reviews-scraper/run-sync-get-dataset-items";

        var body = new
        {
            startUrls = new[] { new { url = "https://2gis.ru/atyrau/firm/70000001087012933" } },
            maxReviews = 1
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

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