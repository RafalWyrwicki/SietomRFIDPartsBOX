using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sietom.AccessControl;

public sealed class Worker(Settings settings, ApiClient api, SqlStore store, ILogger<Worker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Sync.Enabled)
        {
            log.LogWarning("Synchronizacja wyłączona. Uzupełnij konfigurację i ustaw Sync:Enabled.");
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError("Nie udało się rozpocząć synchronizacji: {Code}", ErrorCode(ex)); }
            try { await Task.Delay(TimeSpan.FromSeconds(settings.Sync.IntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var connection = await store.AcquireAsync(ct);
        if (connection is null)
        {
            log.LogInformation("Synchronizacja pominięta: inna instancja posiada blokadę.");
            return;
        }
        var runId = await store.BeginAsync(connection, ct);
        try
        {
            var snapshot = await api.DownloadImportAsync(ct);
            await store.ApplyAsync(connection, runId, snapshot.Cards, ct,snapshot);
            log.LogInformation("Synchronizacja {RunId} {Environment}: {Count} kart. Rekordy API: {SourceCount}.", runId, settings.Sync.Environment, snapshot.Cards.Count,snapshot.SourceRecordCount);
            if(snapshot.Conflicts.Count>0)
                log.LogWarning("Pominięto i zablokowano {Count} spornych kart. Pozostałe karty zapisano. Raport: dbo.AccessCardConflicts.",snapshot.Conflicts.Count);
        }
        catch (Exception ex)
        {
            var code = ErrorCode(ex);
            // Separate short timeout allows recording graceful shutdown without leaking exception payloads.
            using var auditTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await store.FailAsync(connection, runId, code, auditTimeout.Token); }
            catch { log.LogError("Nie można zapisać wyniku przebiegu {RunId}.", runId); }
            log.LogError("Synchronizacja {RunId} zakończona błędem: {Code}", runId, code);
            if (ex is SyncException { Diagnostic: not null } validation)
                log.LogError("Walidacja odpowiedzi API: {Diagnostic}", validation.Diagnostic);
            throw;
        }
    }

    // --once: 2 means API/network unavailable; install the service and retry in its normal loop.
    // Configuration, permissions, SQL and invalid data still stop installation with code 1.
    public static int OnceExitCode(Exception ex)
    {
        static bool RetryableStatus(int status)=>status is 408 or 429 || status is >=500 and <=599;
        if(ex is OperationCanceledException)return 2;
        if(ex is HttpRequestException http)return http.StatusCode is null||RetryableStatus((int)http.StatusCode)?2:1;
        if(ex is SyncException sync)
        {
            var prefix=sync.Code.StartsWith("ApiHttp",StringComparison.Ordinal)?7:
                sync.Code.StartsWith("TokenHttp",StringComparison.Ordinal)?9:0;
            if(prefix>0&&int.TryParse(sync.Code.AsSpan(prefix),out var status)&&RetryableStatus(status))return 2;
        }
        return 1;
    }

    private static string ErrorCode(Exception ex) => ex switch
    {
        SyncException sync => sync.Code,
        SqlException sql when sql.Number == 51002 => "DeactivationLimitExceeded",
        SqlException sql => $"Sql{sql.Number}",
        OperationCanceledException => "CancelledOrTimeout",
        HttpRequestException => "NetworkFailure",
        System.Security.Cryptography.CryptographicException => "SecretDecryptionFailure",
        _ => "UnexpectedFailure"
    };
}
