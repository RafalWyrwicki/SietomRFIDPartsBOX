using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sietom.AccessControl;

public sealed class ApiClient(HttpClient http, Settings settings)
{
    private string? token;
    private DateTimeOffset expiresUtc;

    public async Task<IReadOnlyList<CardUser>> DownloadAsync(CancellationToken ct)
        => (await DownloadImportAsync(ct)).Cards;

    public async Task<CardImportSnapshot> DownloadImportAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var bearer = await GetTokenAsync(ct);
            using var response = await SendAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, settings.ApiUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString());
                return request;
            }, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                token = null;
                continue;
            }
            RequireSuccess(response, "Api");
            var totals = response.Headers.TryGetValues("X-Total-Count", out var values) ? values.ToArray() : [];
            if (totals.Length != 1 ||
                !long.TryParse(totals[0], NumberStyles.None, CultureInfo.InvariantCulture, out var total))
                throw new SyncException("MissingOrInvalidTotalCount");
            if (response.Content.Headers.ContentType?.MediaType != "application/json")
                throw new SyncException("InvalidContentType");
            var body = await ReadBoundedAsync(response, settings.Sync.MaxResponseBytes, ct);
            return Snapshot.ParseImport(body, total, settings.Sync.AllowEmptySnapshot);
        }
        throw new SyncException("ApiHttp401");
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (token is not null && expiresUtc > DateTimeOffset.UtcNow) return token;
        var protectedBytes = await File.ReadAllBytesAsync(settings.OAuth.ClientSecretFile, ct);
        var secretBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        string secret;
        try { secret = Encoding.UTF8.GetString(secretBytes); }
        finally { CryptographicOperations.ZeroMemory(secretBytes); }
        using var response = await SendAsync(() =>
        {
            var values = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials", ["scope"] = settings.OAuth.Scope
            };
            var request = new HttpRequestMessage(HttpMethod.Post, settings.OAuth.TokenUrl);
            if (settings.OAuth.ClientAuthentication == "Basic")
            {
                var credentials = $"{WebUtility.UrlEncode(settings.OAuth.ClientId)}:{WebUtility.UrlEncode(secret)}";
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
            }
            else
            {
                values["client_id"] = settings.OAuth.ClientId;
                values["client_secret"] = secret;
            }
            request.Content = new FormUrlEncodedContent(values);
            return request;
        }, ct);
        RequireSuccess(response, "Token");
        try
        {
            using var json = JsonDocument.Parse(await ReadBoundedAsync(response, 65536, ct));
            var root = json.RootElement;
            var value = root.GetProperty("access_token").GetString();
            if (string.IsNullOrWhiteSpace(value) || !string.Equals(root.GetProperty("token_type").GetString(),
                "Bearer", StringComparison.OrdinalIgnoreCase)) throw new SyncException("InvalidTokenResponse");
            var lifetime = root.GetProperty("expires_in").GetInt32();
            if (lifetime is < 1 or > 86400) throw new SyncException("InvalidTokenLifetime");
            token = value;
            expiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, lifetime - 30));
            return value;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new SyncException("InvalidTokenResponse"); }
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> factory, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.Sync.RequestTimeoutSeconds));
            try
            {
                using var request = factory();
                // Buffering is bounded below; timeout covers headers AND full body.
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var bytes = await ReadBoundedAsync(response, settings.Sync.MaxResponseBytes, timeout.Token);
                var headers = response.Content.Headers.ToArray();
                response.Content.Dispose();
                response.Content = new ByteArrayContent(bytes);
                foreach (var header in headers) response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (!retryable || attempt >= 2) return response;
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested) && attempt < 2)
            { response?.Dispose(); response = null; }
            catch { response?.Dispose(); throw; }
            var retryAfter = response?.Headers.RetryAfter;
            var delay = retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow)
                ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
            response?.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 60)), ct);
        }
    }

    private static void RequireSuccess(HttpResponseMessage response, string prefix)
    {
        if (!response.IsSuccessStatusCode) throw new SyncException($"{prefix}Http{(int)response.StatusCode}");
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int limit, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > limit) throw new SyncException("ResponseTooLarge");
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (memory.Length + read > limit) throw new SyncException("ResponseTooLarge");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }
}
