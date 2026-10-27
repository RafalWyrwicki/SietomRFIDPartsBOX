using System.Net;
using System.Security.Cryptography;
using System.Text;
using Sietom.AccessControl;
using Xunit;

public sealed class ApiClientTests : IDisposable
{
    private readonly string secretPath = Path.Combine(Path.GetTempPath(), $"sietom-test-{Guid.NewGuid():N}.secret");
    private readonly Settings settings;
    private const string Cards = """[{"employeeId":1578,"firstName":"Jan","lastName":"Kowalski","siteCode":240,"cardNumber":12345}]""";

    public ApiClientTests()
    {
        File.WriteAllBytes(secretPath, ProtectedData.Protect(Encoding.UTF8.GetBytes("synthetic-test-secret"),
            null, DataProtectionScope.CurrentUser));
        settings = new Settings
        {
            OAuth = new OAuthSettings
            {
                TokenUrl = "https://sso.tarczynski.pl/realms/software/protocol/openid-connect/token",
                ClientId = "synthetic-test-client", ClientSecretFile = secretPath
            }
        };
    }

    [Fact]
    public void InstallerCanCheckSecretWithoutConnectingToApiOrSql()
    {
        SqlStore.CheckProtectedSecret(secretPath);
    }

    [Fact]
    public async Task UsesClientCredentialsAndCachesToken()
    {
        var tokenRequests = 0;
        using var handler = new Handler(async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                tokenRequests++;
                var form = await request.Content!.ReadAsStringAsync();
                Assert.Contains("grant_type=client_credentials", form);
                Assert.Contains("scope=access-control.active-card-users.list", form);
                Assert.Contains("client_secret=synthetic-test-secret", form);
                return Token();
            }
            Assert.Equal(settings.ApiUri, request.RequestUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("synthetic-token", request.Headers.Authorization?.Parameter);
            return List();
        });
        using var http = new HttpClient(handler);
        var api = new ApiClient(http, settings);
        Assert.Single(await api.DownloadAsync(default));
        Assert.Single(await api.DownloadAsync(default));
        Assert.Equal(1, tokenRequests);
    }

    [Fact]
    public async Task UnauthorizedRefreshesExactlyOnce()
    {
        var tokenRequests = 0;
        var listRequests = 0;
        using var handler = new Handler(request =>
        {
            if (request.Method == HttpMethod.Post) { tokenRequests++; return Task.FromResult(Token()); }
            listRequests++;
            return Task.FromResult(listRequests == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : List());
        });
        using var http = new HttpClient(handler);
        Assert.Single(await new ApiClient(http, settings).DownloadAsync(default));
        Assert.Equal(2, tokenRequests);
        Assert.Equal(2, listRequests);
    }

    [Fact]
    public async Task ForbiddenDoesNotRetryOrExposeResponseBody()
    {
        var requests = 0;
        using var handler = new Handler(request =>
        {
            if (request.Method == HttpMethod.Post) return Task.FromResult(Token());
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                { Content = new StringContent("sensitive-upstream-detail") });
        });
        using var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<SyncException>(() => new ApiClient(http, settings).DownloadAsync(default));
        Assert.Equal("ApiHttp403", ex.Message);
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(null, "MissingOrInvalidTotalCount")]
    [InlineData("2", "SnapshotCountMismatch")]
    [InlineData("invalid", "MissingOrInvalidTotalCount")]
    public async Task IncompleteResponseIsRejected(string? count, string code)
    {
        using var handler = new Handler(request => Task.FromResult(request.Method == HttpMethod.Post ? Token() : List(count)));
        using var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<SyncException>(() => new ApiClient(http, settings).DownloadAsync(default));
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public async Task OversizedBodyIsRejected()
    {
        settings.Sync.MaxResponseBytes = 1024;
        using var handler = new Handler(request => Task.FromResult(request.Method == HttpMethod.Post ? Token() :
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 1025)) }));
        using var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<SyncException>(() => new ApiClient(http, settings).DownloadAsync(default));
        Assert.Equal("ResponseTooLarge", ex.Code);
    }

    [Fact]
    public async Task TemporaryServerFailureIsRetried()
    {
        var requests = 0;
        using var handler = new Handler(request =>
        {
            if (request.Method == HttpMethod.Post) return Task.FromResult(Token());
            requests++;
            if (requests > 1) return Task.FromResult(List());
            var unavailable = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            unavailable.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return Task.FromResult(unavailable);
        });
        using var http = new HttpClient(handler);
        Assert.Single(await new ApiClient(http, settings).DownloadAsync(default));
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task CallerCancellationStopsRequests()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("Must not issue request"));
        using var http = new HttpClient(handler);
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ApiClient(http, settings).DownloadAsync(ct.Token));
    }

    private static HttpResponseMessage Token() => new(HttpStatusCode.OK)
    { Content = new StringContent("""{"access_token":"synthetic-token","token_type":"Bearer","expires_in":300}""", Encoding.UTF8, "application/json") };

    private static HttpResponseMessage List(string? count = "1")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(Cards, Encoding.UTF8, "application/json") };
        if (count is not null) response.Headers.Add("X-Total-Count", count);
        return response;
    }

    public void Dispose() => File.Delete(secretPath);

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request);
    }
}
