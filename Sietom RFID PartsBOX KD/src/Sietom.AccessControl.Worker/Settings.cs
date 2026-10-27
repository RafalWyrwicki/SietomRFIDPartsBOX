using Microsoft.Data.SqlClient;

namespace Sietom.AccessControl;

public sealed class Settings
{
    public SyncSettings Sync { get; set; } = new();
    public OAuthSettings OAuth { get; set; } = new();
    public SqlSettings Sql { get; set; } = new();

    public Uri ApiUri => new(Sync.Environment == "PRD"
        ? "https://integration-api.tarczynski.pl/external/api/v1/access-control/active-card-users/list"
        : "https://integration-api-stg.tarczynski.pl/external/api/v1/access-control/active-card-users/list");

    public void Validate()
    {
        if (Sync.Environment is not ("STG" or "PRD") || Sync.IntervalSeconds < 10 ||
            Sync.RequestTimeoutSeconds is < 1 or > 300 || Sync.MaxResponseBytes is < 1024 or > 67108864 ||
            Sync.MaxDeactivationFraction is < 0 or > 1 || Sql.CommandTimeoutSeconds is < 1 or > 300)
            throw new InvalidOperationException("Invalid synchronisation settings.");
        if (OAuth.TokenUrl != "https://sso.tarczynski.pl/realms/software/protocol/openid-connect/token" ||
            OAuth.ClientAuthentication is not ("Post" or "Basic") ||
            OAuth.Scope != "access-control.active-card-users.list")
            throw new InvalidOperationException("Invalid OAuth settings.");
        if (Sync.Enabled && (string.IsNullOrWhiteSpace(OAuth.ClientId) ||
            string.IsNullOrWhiteSpace(OAuth.ClientSecretFile) || !File.Exists(OAuth.ClientSecretFile)))
            throw new InvalidOperationException("Configure client ID and protected client secret file.");
        var sql = new SqlConnectionStringBuilder(Sql.ConnectionString);
        if (sql.InitialCatalog != "SietomPartsBox" || !sql.IntegratedSecurity ||
            string.IsNullOrWhiteSpace(sql.DataSource) || sql.ConnectTimeout < 1)
            throw new InvalidOperationException("SQL requires SietomPartsBox, Windows authentication and a finite connection timeout.");
    }
}

public sealed class SyncSettings
{
    public bool Enabled { get; set; }
    public string Environment { get; set; } = "STG";
    public int IntervalSeconds { get; set; } = 120;
    public int RequestTimeoutSeconds { get; set; } = 30;
    public int MaxResponseBytes { get; set; } = 16777216;
    public bool AllowEmptySnapshot { get; set; }
    public double MaxDeactivationFraction { get; set; } = 0.20;
}

public sealed class OAuthSettings
{
    public string TokenUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecretFile { get; set; } = "";
    public string Scope { get; set; } = "access-control.active-card-users.list";
    public string ClientAuthentication { get; set; } = "Post";
}

public sealed class SqlSettings
{
    public string ConnectionString { get; set; } = "";
    public int CommandTimeoutSeconds { get; set; } = 60;
}
