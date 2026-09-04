using System;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace table_reservations.Pos;

// Базовый HTTP-клиент для всех POS-адаптеров (iiko, Paloma, r_keeper и т.д.)
// Инкапсулирует общую логику GET/POST запросов и обработку ошибок,
public abstract class ApiClient
{
    protected readonly HttpClient Http;

    protected ApiClient(HttpClient http)
    {
        Http = http;
    }

    protected async Task<(bool Success, TResponse? Data, string? Error)> GetAsync<TResponse>(string url, CancellationToken ct)
    {
        try
        {
            var response = await Http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                return (false, default, $"HTTP {(int)response.StatusCode}:{body}");
            }

            var jsonString = await response.Content.ReadAsStringAsync(ct);
            var data = JsonSerializer.Deserialize<TResponse>(jsonString);
            return (true, data, null);
        }
        catch (Exception ex)
        {
            return (false, default, $"Ошибка соединения: {ex.Message}");
        }
    }

    protected async Task<(bool Success, TResponse? Data, string? Error)> PostAsync<TRequest, TResponse>(string url, TRequest body, CancellationToken ct)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(url, body, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                return (false, default, $"HTTP {(int)response.StatusCode}:{errorBody}");
            }

            var data = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct);
            return (true, data, null);
        }
        catch (Exception ex)
        {
            return (false, default, $"Ошибка соединения:{ex.Message}");
        }
    }
}