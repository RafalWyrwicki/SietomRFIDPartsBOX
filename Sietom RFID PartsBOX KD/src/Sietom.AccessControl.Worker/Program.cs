using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sietom.AccessControl;

var once = args.Contains("--once", StringComparer.Ordinal);
var validateOnly = args.Contains("--validate", StringComparer.Ordinal);
var installSql = args.Contains("--install-sql",StringComparer.Ordinal);
var checkSecret = args.Contains("--check-secret",StringComparer.Ordinal);
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Where(a => a is not ("--once" or "--validate" or "--install-sql" or "--check-secret")).ToArray(), ContentRootPath = AppContext.BaseDirectory
});
// Local file is outside version control; environment variables override it.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables("SIETOM_");
var settings = builder.Configuration.Get<Settings>() ?? throw new InvalidOperationException("Missing settings.");
if(installSql)settings.Sync.Enabled=false;
if(checkSecret)settings.Sync.Enabled=true;
settings.Validate();
if(installSql)
{
    await SqlStore.InstallSchemaAsync(settings.Sql.ConnectionString,CancellationToken.None);
    Console.WriteLine("SQL: kompletny schemat KD w SietomPartsBox zainstalowany lub zaktualizowany. Dane i historia zachowane.");
    return;
}
if(checkSecret)
{
    SqlStore.CheckProtectedSecret(settings.OAuth.ClientSecretFile);
    Console.WriteLine("Chroniony sekret poprawnie odczytany przez biezace konto Windows.");
    return;
}
if (validateOnly)
{
    Console.WriteLine($"Konfiguracja poprawna. Środowisko: {settings.Sync.Environment}, synchronizacja: {settings.Sync.Enabled}.");
    return;
}
builder.Services.AddSingleton(settings);
builder.Services.AddWindowsService(options => options.ServiceName = "SietomAccessControl");
builder.Services.AddSingleton(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
{ Timeout = Timeout.InfiniteTimeSpan });
builder.Services.AddSingleton<ApiClient>();
builder.Services.AddSingleton<SqlStore>();
builder.Services.AddSingleton<Worker>();
if (!once) builder.Services.AddHostedService(provider => provider.GetRequiredService<Worker>());
using var host = builder.Build();
if (once)
{
    if (!settings.Sync.Enabled) throw new InvalidOperationException("Enable synchronisation before --once.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try { await host.Services.GetRequiredService<Worker>().RunOnceAsync(timeout.Token); }
    catch (Exception ex) { Environment.ExitCode = Worker.OnceExitCode(ex); }
}
else await host.RunAsync();
