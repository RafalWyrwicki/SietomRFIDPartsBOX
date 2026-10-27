using Microsoft.Data.SqlClient;
using Sietom.AccessControl;
using System.Text.RegularExpressions;
using Xunit;

public sealed class LocalSqlFactAttribute : FactAttribute
{
    public LocalSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SIETOM_TEST_SQL")))
            Skip = "Set SIETOM_TEST_SQL to a local test SQL instance; isolated databases are created and removed.";
    }
}

public sealed class PartsBoxSqlTests : IAsyncLifetime
{
    private readonly string databaseName = "PartsBoxKD_Test_" + Guid.NewGuid().ToString("N");
    private string master = "", connectionString = "";

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("SIETOM_TEST_SQL")!;
        if (!(server.StartsWith(@".\", StringComparison.Ordinal) || server.StartsWith(@"localhost\", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Tests accept only a local SQL instance, never the client's server.");
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server, InitialCatalog = "master", IntegratedSecurity = true,
            Encrypt = SqlConnectionEncryptOption.Optional, Pooling = false, ConnectTimeout = 5
        };
        master = builder.ConnectionString;
        await Execute(master, $"CREATE DATABASE [{databaseName}]");
        builder.InitialCatalog = databaseName; connectionString = builder.ConnectionString;
        await Execute(connectionString, """
            CREATE TABLE dbo.Employees(Id bigint PRIMARY KEY,Name nvarchar(150) NOT NULL,
                SiteCode int NOT NULL,CardNumber bigint NOT NULL,Active bit NOT NULL,UNIQUE(SiteCode,CardNumber));
            CREATE TABLE dbo.LocalKeyboardCards(ReaderNumber nvarchar(40) PRIMARY KEY,
                EmployeeId bigint REFERENCES dbo.Employees(Id),Barcode nvarchar(40),Position nvarchar(250));
            CREATE TABLE dbo.Operations(Id uniqueidentifier PRIMARY KEY,EmployeeId bigint REFERENCES dbo.Employees(Id));
            INSERT dbo.Employees VALUES(10,N'Kowalski Jan',240,100,1),(20,N'Julia Nowak',240,200,1),(30,N'Manualny Użytkownik',240,300,1);
            INSERT dbo.LocalKeyboardCards VALUES(N'123456',10,N'TARC10',N'Mechanik'),(N'234567',20,N'TARC20',N'Technik');
            INSERT dbo.Operations VALUES(NEWID(),10);
            """);
    }

    public async Task DisposeAsync()
    {
        if (master.Length == 0) return;
        await Execute(master, $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END");
    }

    private async Task Migrate()
    {
        await SqlStore.InstallSchemaAsync(connectionString,default);
    }

    private static async Task Execute(string cs, string sql)
    {
        await using var connection = new SqlConnection(cs); await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 20 }; await command.ExecuteNonQueryAsync();
    }

    private async Task<T> Scalar<T>(string sql)
    {
        await using var connection = new SqlConnection(connectionString); await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private async Task Apply(string environment, CardUser[] cards, double limit = .20, bool empty = false, bool restricted = false,CardImportSnapshot? snapshot=null)
    {
        var settings = new Settings { Sync = new() { Environment = environment, MaxDeactivationFraction = limit, AllowEmptySnapshot = empty }, Sql = new() { ConnectionString = connectionString } };
        var store = new SqlStore(settings);
        await using var connection = await store.AcquireAsync(default) ?? throw new InvalidOperationException("Lock unavailable.");
        if (restricted)
        {
            using var impersonate = new SqlCommand("EXECUTE AS USER=N'SyncTest'", connection);
            await impersonate.ExecuteNonQueryAsync();
        }
        var run = await store.BeginAsync(connection, default);
        try { await store.ApplyAsync(connection, run, cards, default,snapshot); }
        catch { await store.FailAsync(connection, run, "IntegrationTestFailure", default); throw; }
    }

    private static CardUser Jan(long card) => new(1000, "Jan", "Kowalski", 240, card);
    private static CardUser Julia(long card = 200) => new(2000, "Julia", "Nowak", 240, card);

    [LocalSqlFact]
    public async Task ExistingLocalDatabaseStillAllowsLegacyLogin()
    {
        var db = new PartsBox.Database(connectionString);
        Assert.Equal(10L, (await db.Login("240,100"))!.Id);
        Assert.Equal("TARC10", (await db.Login("123456"))!.Barcode);
        Assert.Null(await db.Login("240,999"));
    }

    [LocalSqlFact]
    public async Task IncompleteKdMigrationCannotFallBackToLegacyAuthentication()
    {
        await Migrate();
        await Execute(connectionString, "DROP PROCEDURE dbo.FindAuthorizedEmployee");
        var db = new PartsBox.Database(connectionString);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Login("240,100"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Login("123456"));
    }

    [LocalSqlFact]
    public async Task SignedApiNumbersSurviveSqlUpgradeAndCardLogin()
    {
        await Migrate();
        var upgrade=(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"04_Integracja_KD.sql")))
            .Replace("USE SietomPartsBox;",$"USE [{databaseName}];");
        var batches=Regex.Split(upgrade,@"^GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase);
        var oldProcedure=batches.Single(b=>b.Contains("CREATE OR ALTER PROCEDURE dbo.ApplySnapshot"))
            .Replace("WHERE LEN(FirstName+N' '+LastName)>150)","WHERE SiteCode<0 OR CardNumber<0 OR EmployeeId<0 OR LEN(FirstName+N' '+LastName)>150)");
        await Execute(connectionString,oldProcedure);
        CardUser[] cards=[new(long.MinValue,"Test","Signed",int.MinValue,long.MinValue)];
        Assert.Equal(51000,(await Assert.ThrowsAsync<SqlException>(()=>Apply("PRD",cards))).Number);
        foreach(var batch in batches)if(!string.IsNullOrWhiteSpace(batch))await Execute(connectionString,batch);
        await Apply("PRD",cards);
        var db=new PartsBox.Database(connectionString);
        var employee=await db.Login($"{int.MinValue},{long.MinValue}");
        Assert.NotNull(employee);
        Assert.True(employee!.Id>0);
        Assert.Equal(long.MinValue,await Scalar<long>("SELECT ApiEmployeeId FROM dbo.EmployeeApiLinks"));
        Assert.Equal(int.MinValue,await Scalar<int>("SELECT SiteCode FROM dbo.Employees WHERE Id="+employee.Id));
    }

    [LocalSqlFact]
    public async Task DisputedCardsAreRevokedWhileUnambiguousCardsAreCommitted()
    {
        await Migrate();
        await Execute(connectionString,"CREATE USER SyncTest WITHOUT LOGIN; ALTER ROLE SietomSyncWriter ADD MEMBER SyncTest;");
        await Apply("PRD",[Jan(100),Julia()]);
        var db=new PartsBox.Database(connectionString);
        var report=new CardImportSnapshot([Jan(101),Julia()],[new(240,100)],4,0);
        await Apply("PRD",report.Cards.ToArray(),restricted:true,snapshot:report);
        Assert.Null(await db.Login("240,100"));
        Assert.Null(await db.Login("123456"));
        Assert.Equal(10L,(await db.Login("240,101"))!.Id);
        Assert.Equal(20L,(await db.Login("240,200"))!.Id);
        Assert.Equal(1,await Scalar<int>("SELECT COUNT(*) FROM dbo.AccessCardConflicts WHERE Environment='PRD' AND ResolvedUtc IS NULL"));
        Assert.Equal(1,await Scalar<int>("SELECT ConflictingCardCount FROM dbo.AccessCardSyncRuns WHERE RunId=(SELECT LastSuccessfulRunId FROM dbo.AccessCardSyncState WHERE Environment='PRD')"));
        Assert.Equal(4,await Scalar<int>("SELECT SourceRecordCount FROM dbo.AccessCardSyncRuns WHERE RunId=(SELECT LastSuccessfulRunId FROM dbo.AccessCardSyncState WHERE Environment='PRD')"));
        var allDisputed=new CardImportSnapshot([],[new(240,101),new(240,200)],4,0);
        await Apply("PRD",[],snapshot:allDisputed);
        Assert.Null(await db.Login("240,101"));
        Assert.Null(await db.Login("240,200"));
        Assert.Equal(10L,await Scalar<long>("SELECT EmployeeId FROM dbo.Operations"));
        await Apply("PRD",[Jan(101),Julia()]);
        Assert.NotNull(await db.Login("240,101"));
        Assert.Equal(0,await Scalar<int>("SELECT COUNT(*) FROM dbo.AccessCardConflicts WHERE Environment='PRD' AND ResolvedUtc IS NULL"));
    }

    [LocalSqlFact]
    public async Task SharedDatabasePreservesHistoryAndAuthorizesOnlyCurrentCards()
    {
        await Migrate(); await Migrate(); // Idempotent installation.
        await Execute(connectionString, """
            CREATE USER SyncTest WITHOUT LOGIN; ALTER ROLE SietomSyncWriter ADD MEMBER SyncTest;
            CREATE USER ReaderTest WITHOUT LOGIN; ALTER ROLE SietomCardReader ADD MEMBER ReaderTest;
            """);
        var db = new PartsBox.Database(connectionString);
        Assert.Equal(10L, (await db.Login("123456"))!.Id);
        await Apply("STG", [new(9999, "Test", "Staging", 240, 100)]);
        Assert.Equal("Kowalski Jan", await Scalar<string>("SELECT Name FROM dbo.Employees WHERE Id=10"));
        Assert.Equal(0, await Scalar<int>("SELECT COUNT(*) FROM dbo.EmployeeApiLinks"));

        await Apply("PRD", [Jan(100), Jan(101), Julia()], restricted: true);
        await Apply("PRD", [Jan(100), Jan(101), Julia()], restricted: true);
        Assert.Equal(3, await Scalar<int>("SELECT COUNT(*) FROM dbo.Employees"));
        Assert.Equal(10L, (await db.Login("240,101"))!.Id);
        Assert.Equal("TARC10", (await db.Login("123456"))!.Barcode);
        Assert.Null(await db.Login("240,300")); // Unmanaged employee remains in history, not in the PRD allow-list.
        Assert.True(await Scalar<bool>("SELECT Active FROM dbo.Employees WHERE Id=30"));
        await Execute(connectionString, "EXECUTE AS USER=N'ReaderTest'; EXEC dbo.FindAuthorizedEmployee @ReaderNumber=N'123456'; REVERT;");

        var limitError = await Assert.ThrowsAsync<SqlException>(() => Apply("PRD", [Jan(101), Julia()]));
        Assert.Equal(51002, limitError.Number);
        Assert.NotNull(await db.Login("240,100")); // Rejected snapshot did not change authentication.
        await Apply("PRD", [Jan(101), Julia()], limit: .5);
        Assert.Null(await db.Login("240,100"));
        Assert.Null(await db.Login("123456")); // A raw alias cannot use the employee's replacement card.
        Assert.Equal(101L, await Scalar<long>("SELECT CardNumber FROM dbo.Employees WHERE Id=10"));

        await Apply("PRD", [Julia(101), Julia()]); // Reassign the card to a different API person.
        Assert.Equal(20L, (await db.Login("240,101"))!.Id);
        Assert.False(await Scalar<bool>("SELECT Active FROM dbo.Employees WHERE Id=10"));
        Assert.Equal(10L, await Scalar<long>("SELECT EmployeeId FROM dbo.Operations"));
        var piotr = new CardUser(3000, "Piotr", "Lewandowski", 240, 100);
        await Apply("PRD", [Julia(101), Julia(), piotr]);
        var piotrId = (await db.Login("240,100"))!.Id;
        Assert.NotEqual(3000L, piotrId); // API IDs never overwrite local IDs.
        var reassignedAlias = (await db.Login("123456"))!;
        Assert.Equal(piotrId, reassignedAlias.Id);
        Assert.Equal("", reassignedAlias.Barcode); // Old owner's local details must not follow the card.

        var conflicting = new CardUser(4000, "Inny", "Pracownik", 240, 300);
        var mappingError = await Assert.ThrowsAsync<SqlException>(() => Apply("PRD", [Julia(101), Julia(), piotr, conflicting]));
        Assert.Equal(51003, mappingError.Number);
        Assert.Equal(0, await Scalar<int>("SELECT COUNT(*) FROM dbo.EmployeeApiLinks WHERE ApiEmployeeId=4000"));
        Assert.Equal(3, await Scalar<int>("SELECT COUNT(*) FROM dbo.AccessCardUsers WHERE Environment='PRD' AND Active=1"));
        await Execute(connectionString, "UPDATE dbo.AccessCardSyncState SET LastSuccessfulUtc=DATEADD(MINUTE,-11,SYSUTCDATETIME()) WHERE Environment='PRD'");
        await Execute(connectionString, """
            CREATE OR ALTER VIEW dbo.vAuthorizedCards AS
                SELECT u.SiteCode,u.CardNumber,l.LocalEmployeeId AS EmployeeId,u.EmployeeId AS ApiEmployeeId,
                    u.FirstName,u.LastName,e.Name,u.LastSynchronizedUtc
                FROM dbo.AccessCardUsers u JOIN dbo.AccessCardSyncState s ON s.Environment=u.Environment
                    JOIN dbo.EmployeeApiLinks l ON l.ApiEmployeeId=u.EmployeeId JOIN dbo.Employees e ON e.Id=l.LocalEmployeeId
                WHERE u.Environment='PRD' AND u.Active=1 AND e.Active=1
                    AND s.LastSuccessfulUtc >= DATEADD(MINUTE,-10,SYSUTCDATETIME());
            """);
        Assert.Null(await db.Login("240,100")); // Reproduce the previous server policy, then upgrade it.
        var upgrade = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"04_Integracja_KD.sql")))
            .Replace("USE SietomPartsBox;",$"USE [{databaseName}];");
        foreach(var batch in Regex.Split(upgrade,@"^GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))
            if(batch.Contains("CREATE OR ALTER VIEW dbo.vAuthorizedCards")||batch.Contains("CREATE OR ALTER PROCEDURE dbo.GetCardSyncStatus"))await Execute(connectionString,batch);
        Assert.Equal(piotrId,(await db.Login("240,100"))!.Id);
        Assert.Equal(piotrId,(await db.Login("123456"))!.Id);
        Assert.True((await db.CardSyncStatus()).Stale);
        Assert.Null(await db.Login("240,999"));
        Assert.False(await Scalar<bool>("SELECT Active FROM dbo.Employees WHERE Id=10"));
        await Apply("PRD", [Julia(101), Julia(), piotr]);
        Assert.NotNull(await db.Login("240,100"));
        Assert.False((await db.CardSyncStatus()).Stale);
        var emptyError = await Assert.ThrowsAsync<SqlException>(() => Apply("PRD", [], limit: 1));
        Assert.Equal(51001, emptyError.Number);
        Assert.NotNull(await db.Login("240,100"));
        await Apply("PRD", [], limit: 1, empty: true);
        Assert.Null(await db.Login("240,100"));
        Assert.Equal(0, await Scalar<int>("SELECT COUNT(*) FROM dbo.Employees e JOIN dbo.EmployeeApiLinks l ON l.LocalEmployeeId=e.Id WHERE e.Active=1"));
        Assert.Equal(10L, await Scalar<long>("SELECT EmployeeId FROM dbo.Operations"));
    }
}
