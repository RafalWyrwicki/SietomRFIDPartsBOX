using System.Data;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace Sietom.AccessControl;

public sealed class SqlStore(Settings settings)
{
    public static async Task InstallSchemaAsync(string connectionString,CancellationToken ct)
    {
        var builder=new SqlConnectionStringBuilder(connectionString);
        var localTest=builder.InitialCatalog.StartsWith("PartsBoxKD_Test_",StringComparison.Ordinal)
            && (builder.DataSource.StartsWith(@".\",StringComparison.Ordinal)||builder.DataSource.StartsWith(@"localhost\",StringComparison.OrdinalIgnoreCase));
        if(builder.InitialCatalog!="SietomPartsBox"&&!localTest)
            throw new InvalidOperationException("SQL installer requires existing SietomPartsBox.");
        using var stream=typeof(SqlStore).Assembly.GetManifestResourceStream("Sietom.AccessControl.Schema.sql")
            ?? throw new InvalidOperationException("SQL installation script is missing.");
        using var reader=new StreamReader(stream);
        var script=(await reader.ReadToEndAsync(ct)).Replace("USE SietomPartsBox;","USE ["+builder.InitialCatalog.Replace("]","]]")+"];");
        await using var connection=new SqlConnection(connectionString);await connection.OpenAsync(ct);
        foreach(var batch in Regex.Split(script,@"^GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))
        {
            if(string.IsNullOrWhiteSpace(batch))continue;
            using var command=new SqlCommand(batch,connection){CommandTimeout=120};await command.ExecuteNonQueryAsync(ct);
        }
    }

    public static void CheckProtectedSecret(string path)
    {
        var plain=ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser);
        try {if(plain.Length==0)throw new InvalidOperationException("Protected secret is empty.");}
        finally {CryptographicOperations.ZeroMemory(plain);}
    }

    public async Task<SqlConnection?> AcquireAsync(CancellationToken ct)
    {
        // Session locks must die with the physical connection, including crash/error paths.
        var connectionString = new SqlConnectionStringBuilder(settings.Sql.ConnectionString) { Pooling = false };
        var connection = new SqlConnection(connectionString.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            using var command = new SqlCommand("dbo.AcquireSyncLock", connection)
            { CommandType = CommandType.StoredProcedure, CommandTimeout = settings.Sql.CommandTimeoutSeconds };
            command.Parameters.Add("@Environment", SqlDbType.VarChar, 3).Value = settings.Sync.Environment;
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
            if (result >= 0) return connection;
            await connection.DisposeAsync();
            return null;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<Guid> BeginAsync(SqlConnection connection, CancellationToken ct)
    {
        using var command = Command("dbo.BeginSync", connection);
        return (Guid)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task ApplyAsync(SqlConnection connection, Guid runId, IReadOnlyList<CardUser> cards, CancellationToken ct,CardImportSnapshot? snapshot=null)
    {
        using var table = new DataTable();
        table.Columns.Add("EmployeeId", typeof(long));
        table.Columns.Add("FirstName", typeof(string));
        table.Columns.Add("LastName", typeof(string));
        table.Columns.Add("SiteCode", typeof(int));
        table.Columns.Add("CardNumber", typeof(long));
        foreach (var card in cards) table.Rows.Add(card.EmployeeId, card.FirstName, card.LastName, card.SiteCode, card.CardNumber);
        using var command = Command("dbo.ApplySnapshot", connection);
        command.Parameters.Add("@RunId", SqlDbType.UniqueIdentifier).Value = runId;
        command.Parameters.Add(new SqlParameter("@Cards", SqlDbType.Structured) { TypeName = "dbo.CardSnapshot", Value = table });
        command.Parameters.Add("@AllowEmpty", SqlDbType.Bit).Value = settings.Sync.AllowEmptySnapshot;
        command.Parameters.Add("@MaxDeactivationFraction", SqlDbType.Float).Value = settings.Sync.MaxDeactivationFraction;
        using var conflicts=new DataTable();
        conflicts.Columns.Add("SiteCode",typeof(int));conflicts.Columns.Add("CardNumber",typeof(long));
        foreach(var conflict in snapshot?.Conflicts ?? Array.Empty<CardConflict>())conflicts.Rows.Add(conflict.SiteCode,conflict.CardNumber);
        command.Parameters.Add(new SqlParameter("@Conflicts",SqlDbType.Structured){TypeName="dbo.CardConflictKey",Value=conflicts});
        command.Parameters.Add("@SourceRecordCount",SqlDbType.Int).Value=snapshot?.SourceRecordCount??cards.Count;
        command.Parameters.Add("@IdenticalDuplicateCount",SqlDbType.Int).Value=snapshot?.IdenticalDuplicateCount??0;
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task FailAsync(SqlConnection connection, Guid runId, string code, CancellationToken ct)
    {
        using var command = Command("dbo.FailSync", connection);
        command.Parameters.Add("@RunId", SqlDbType.UniqueIdentifier).Value = runId;
        command.Parameters.Add("@ErrorCode", SqlDbType.VarChar, 64).Value = code;
        await command.ExecuteNonQueryAsync(ct);
    }

    private SqlCommand Command(string name, SqlConnection connection)
    {
        var command = new SqlCommand(name, connection)
        { CommandType = CommandType.StoredProcedure, CommandTimeout = settings.Sql.CommandTimeoutSeconds };
        command.Parameters.Add("@Environment", SqlDbType.VarChar, 3).Value = settings.Sync.Environment;
        return command;
    }
}
