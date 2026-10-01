using Microsoft.Data.SqlClient;
using System.Data;

namespace PartsBox;
public sealed partial class Database(string connectionString)
{
 async Task<SqlConnection> Open() { var c=new SqlConnection(connectionString); try { await c.OpenAsync(); return c; } catch { c.Dispose(); throw; } }
 static SqlCommand Cmd(SqlConnection c,string sql,SqlTransaction? t=null,params (string,object?)[] args) { var q=new SqlCommand(sql,c,t); foreach(var (n,v) in args) q.Parameters.AddWithValue(n,v??DBNull.Value); return q; }
 public async Task Initialize(bool demo)
 {
  var b=new SqlConnectionStringBuilder(connectionString); var name=b.InitialCatalog;
  if(name!="SietomPartsBox_Development" || !new[]{@".\SQLEXPRESS",@"localhost\SQLEXPRESS",@"(local)\SQLEXPRESS"}.Contains(b.DataSource,StringComparer.OrdinalIgnoreCase))
  {
   using var existing=await Open();
   using var validate=Cmd(existing,"""
    SELECT name FROM (VALUES ('Employees'),('LocalKeyboardCards'),('Orders'),('OrderParts'),('Parts'),('Operations'),('Items'),('InventoryReadings'),('Audit'),('Outbox')) required(name)
    WHERE OBJECT_ID(N'dbo.'+name,N'U') IS NULL;
    """);
   using var result=await validate.ExecuteReaderAsync();var missing=new List<string>();
   while(await result.ReadAsync())missing.Add(result.GetString(0));
   if(missing.Count>0)throw new InvalidOperationException("Brak tabel bazy: "+string.Join(", ",missing)+". Administrator musi wdrożyć schemat z katalogu sql.");
   return;
  }
  b.InitialCatalog="master";
  using(var c=new SqlConnection(b.ConnectionString)) { await c.OpenAsync(); using var q=Cmd(c,"IF DB_ID(N'SietomPartsBox_Development') IS NULL CREATE DATABASE [SietomPartsBox_Development]"); await q.ExecuteNonQueryAsync(); }
  using var db=await Open();
  using var schema=Cmd(db,"""
  IF OBJECT_ID('Employees') IS NULL BEGIN
   CREATE TABLE Employees(Id bigint PRIMARY KEY, Name nvarchar(150) NOT NULL, SiteCode int NOT NULL, CardNumber bigint NOT NULL, Active bit NOT NULL, UNIQUE(SiteCode,CardNumber));
   CREATE TABLE Orders(Number nvarchar(50) PRIMARY KEY, Description nvarchar(250) NOT NULL, Active bit NOT NULL);
   CREATE TABLE Parts(Epc nvarchar(128) PRIMARY KEY, Material nvarchar(80) NOT NULL, Name nvarchar(250) NOT NULL, State nvarchar(30) NOT NULL, StockQuantity decimal(18,3) NOT NULL CONSTRAINT DF_Parts_StockQuantity DEFAULT 0, Barcode nvarchar(128) NOT NULL CONSTRAINT DF_Parts_Barcode DEFAULT N'');
   CREATE TABLE Operations(Id uniqueidentifier PRIMARY KEY, Kind nvarchar(20) NOT NULL, EmployeeId bigint NOT NULL REFERENCES Employees(Id), OrderNumber nvarchar(50) NULL REFERENCES Orders(Number), SourceId uniqueidentifier NULL REFERENCES Operations(Id), CreatedUtc datetime2 NOT NULL, Settlement nvarchar(60) NOT NULL);
   CREATE TABLE Items(OperationId uniqueidentifier REFERENCES Operations(Id), Epc nvarchar(128) REFERENCES Parts(Epc), Material nvarchar(80) NOT NULL, Name nvarchar(250) NOT NULL, PRIMARY KEY(OperationId,Epc));
   CREATE TABLE Audit(Id bigint IDENTITY PRIMARY KEY, OccurredUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME(), EmployeeId bigint NULL, Event nvarchar(40) NOT NULL, Details nvarchar(max) NOT NULL);
   CREATE TABLE Outbox(OperationId uniqueidentifier PRIMARY KEY REFERENCES Operations(Id), Status nvarchar(30) NOT NULL, CreatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
  END
  """); await schema.ExecuteNonQueryAsync();
  if(demo) { using var seed=Cmd(db,"""
   IF NOT EXISTS(SELECT 1 FROM Employees) BEGIN
   INSERT Employees VALUES(1,N'Jan Kowalski — DEMO',240,12345,1),(2,N'Anna Nowak — DEMO',240,54321,1);
   INSERT Orders VALUES(N'400012345',N'Wymiana łożysk — DEMO',1),(N'400012388',N'Przegląd napędu — DEMO',1);
   INSERT Parts VALUES(N'303400000000000000000001',N'MAT-10025',N'Łożysko 6204','Available'),(N'303400000000000000000002',N'MAT-10025',N'Łożysko 6204','Available'),(N'303400000000000000000003',N'MAT-11010',N'Czujnik indukcyjny','Available');
   END
   """); await seed.ExecuteNonQueryAsync(); }
  using(var localSchema=Cmd(db,"""
   IF COL_LENGTH('dbo.Parts','StockQuantity') IS NULL ALTER TABLE dbo.Parts ADD StockQuantity decimal(18,3) NOT NULL CONSTRAINT DF_Parts_StockQuantity DEFAULT 0;
   IF COL_LENGTH('dbo.Parts','Barcode') IS NULL ALTER TABLE dbo.Parts ADD Barcode nvarchar(128) NOT NULL CONSTRAINT DF_Parts_Barcode DEFAULT N'';
   IF OBJECT_ID('InventoryReadings') IS NULL CREATE TABLE InventoryReadings(OperationId uniqueidentifier NOT NULL REFERENCES Operations(Id),Epc nvarchar(128) NOT NULL,Material nvarchar(80) NOT NULL,Name nvarchar(250) NOT NULL,Status nvarchar(60) NOT NULL,PRIMARY KEY(OperationId,Epc));
   IF OBJECT_ID('LocalKeyboardCards') IS NULL
    CREATE TABLE LocalKeyboardCards(ReaderNumber nvarchar(40) PRIMARY KEY, EmployeeId bigint NOT NULL REFERENCES Employees(Id), Barcode nvarchar(40) NOT NULL UNIQUE, Position nvarchar(250) NOT NULL);
   UPDATE Employees SET Name=N'Jan Kowalski' WHERE Id=1 AND Name=N'Jan Kowalski — DEMO';
   UPDATE Employees SET Name=N'Anna Nowak' WHERE Id=2 AND Name=N'Anna Nowak — DEMO';
   UPDATE Orders SET Description=N'Wymiana łożysk' WHERE Number=N'400012345' AND Description=N'Wymiana łożysk — DEMO';
   UPDATE Orders SET Description=N'Przegląd napędu' WHERE Number=N'400012388' AND Description=N'Przegląd napędu — DEMO';
   """))await localSchema.ExecuteNonQueryAsync();
  using var localTransaction=db.BeginTransaction(IsolationLevel.Serializable);
  foreach(var card in new[]{("240,38032","TARC19571","Jabłoński Artur","Zastępca Managera Obszaru Produkcyjnego"),("240,38386","TARC19687","Wywrot Julia","Stażystka w Dziale Zapewnienia Jakości")})
  {
   using var register=Cmd(db,"""
    IF NOT EXISTS(SELECT 1 FROM LocalKeyboardCards WITH(UPDLOCK,HOLDLOCK) WHERE ReaderNumber=@number)
    BEGIN
     DECLARE @id bigint;
     SELECT @id=Id FROM Employees WHERE SiteCode=240 AND CardNumber=@numeric;
     IF @id IS NULL BEGIN
      SELECT @id=COALESCE(MAX(Id),0)+1 FROM Employees WITH(UPDLOCK,HOLDLOCK);
      INSERT Employees VALUES(@id,@name,240,@numeric,1);
     END;
     INSERT LocalKeyboardCards VALUES(@number,@id,@barcode,@position);
    END;
   """,localTransaction,("@number",card.Item1),("@numeric",long.Parse(card.Item1.Split(',')[1])),("@barcode",card.Item2),("@name",card.Item3),("@position",card.Item4));
   await register.ExecuteNonQueryAsync();
  }
  localTransaction.Commit();
 }
 public async Task<Employee?> Login(string card)
 {
  card=card.Trim();
  if(card.Length is >0 and <=40 && card.All(char.IsAsciiDigit))
  {
   using var local=await Open();using var query=Cmd(local,"SELECT e.Id,e.Name,k.Barcode,k.Position FROM LocalKeyboardCards k JOIN Employees e ON e.Id=k.EmployeeId WHERE k.ReaderNumber=@number AND e.Active=1",null,("@number",card));
   using var data=await query.ExecuteReaderAsync();return await data.ReadAsync()?new(data.GetInt64(0),data.GetString(1),data.GetString(2),data.GetString(3)):null;
  }
  var p=card.Split(','); if(p.Length!=2 || !int.TryParse(p[0],out var site)||!long.TryParse(p[1],out var number)||site<0||number<0) throw new InvalidOperationException("Przyłóż kartę do czytnika albo wpisz jej numer i wybierz Zaloguj.");
  using var c=await Open(); using var q=Cmd(c,"SELECT Id,Name FROM Employees WHERE SiteCode=@s AND CardNumber=@n AND Active=1",null,("@s",site),("@n",number)); using var r=await q.ExecuteReaderAsync(); return await r.ReadAsync()?new(r.GetInt64(0),r.GetString(1)):null;
 }
 public async Task<List<Order>> Orders() { using var c=await Open(); using var q=Cmd(c,"SELECT Number,Description FROM Orders WHERE Active=1 ORDER BY Number"); using var r=await q.ExecuteReaderAsync(); var a=new List<Order>(); while(await r.ReadAsync())a.Add(new(r.GetString(0),r.GetString(1)));return a; }
 public async Task<Dictionary<string,Part>> Parts() { using var c=await Open();using var q=Cmd(c,"SELECT Epc,Material,Name,State FROM Parts");using var r=await q.ExecuteReaderAsync();var a=new Dictionary<string,Part>(StringComparer.OrdinalIgnoreCase);while(await r.ReadAsync())a.Add(r.GetString(0),new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3)));return a; }
 public async Task<List<Receipt>> Receipts() { using var c=await Open();using var q=Cmd(c,"SELECT Id,OrderNumber,CreatedUtc FROM Operations WHERE Kind='Issue' ORDER BY CreatedUtc DESC");using var r=await q.ExecuteReaderAsync();var a=new List<Receipt>();while(await r.ReadAsync())a.Add(new(r.GetGuid(0),r.GetString(1),DateTime.SpecifyKind(r.GetDateTime(2),DateTimeKind.Utc).ToLocalTime()));return a; }
 public async Task<List<HistoryRow>> ReceiptExport(Guid id)
 {
  using var c=await Open();using var q=Cmd(c,"SELECT o.Id,o.CreatedUtc,e.Name,o.OrderNumber,i.Epc,i.Material,i.Name,o.Settlement FROM Operations o JOIN Employees e ON e.Id=o.EmployeeId JOIN Items i ON i.OperationId=o.Id WHERE o.Id=@id AND o.Kind='Issue' ORDER BY i.Epc",null,("@id",id));
  using var r=await q.ExecuteReaderAsync();var rows=new List<HistoryRow>();
  while(await r.ReadAsync())rows.Add(new(r.GetGuid(0),DateTime.SpecifyKind(r.GetDateTime(1),DateTimeKind.Utc).ToLocalTime(),"Pobranie",r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7)));
  return rows;
 }
 public async Task Audit(long? employee,string action,string details) { using var c=await Open();using var q=Cmd(c,"INSERT Audit(EmployeeId,Event,Details) VALUES(@e,@a,@d)",null,("@e",employee),("@a",action),("@d",details));await q.ExecuteNonQueryAsync(); }
 public async Task Save(Guid id,string kind,Employee employee,string? order,Guid? source,IReadOnlyList<string> epcs)
 {
  if(epcs.Count==0||epcs.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=epcs.Count)throw new InvalidOperationException("Pusta lista lub powtórzone EPC.");
  if(kind is not ("Issue" or "Return" or "Inventory"))throw new InvalidOperationException("Nieprawidłowy typ operacji.");
  using var c=await Open();using var t=c.BeginTransaction(IsolationLevel.Serializable);
  if(kind=="Issue")
  {
   using var guard=Cmd(c,"""
    IF COL_LENGTH('dbo.Orders','UserStatus') IS NOT NULL
    EXEC sp_executesql N'IF EXISTS(SELECT 1 FROM dbo.Orders WITH(UPDLOCK,HOLDLOCK) WHERE Number=@number AND CHARINDEX(N'' INIT '',N'' ''+UPPER(REPLACE(UserStatus,CHAR(9),N'' ''))+N'' '')>0) THROW 50001,N''Zlecenie nieaktywne. Proszę uruchomić przekazanie do wykonania'',1;',N'@number nvarchar(50)',@number=@n;
    """,t,("@n",order));await guard.ExecuteNonQueryAsync();
  }
  using(var check=Cmd(c,"SELECT COUNT(*) FROM Operations WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id",t,("@id",id))) if((int)(await check.ExecuteScalarAsync())!>0){t.Commit();return;}
  using(var active=Cmd(c,"SELECT COUNT(*) FROM Employees WHERE Id=@id AND Active=1",t,("@id",employee.Id)))if((int)(await active.ExecuteScalarAsync())!!=1)throw new InvalidOperationException("Pracownik nieaktywny.");
  if(kind=="Issue") { using var q=Cmd(c,"SELECT COUNT(*) FROM Orders WHERE Number=@n AND Active=1",t,("@n",order));if((int)(await q.ExecuteScalarAsync())!!=1)throw new InvalidOperationException("Wybierz aktywne zlecenie."); }
  if(kind=="Return") {using var q=Cmd(c,"SELECT OrderNumber FROM Operations WHERE Id=@id AND Kind='Issue'",t,("@id",source));order=await q.ExecuteScalarAsync() as string??throw new InvalidOperationException("Wybierz pierwotne pobranie.");}
  var settlement=kind=="Issue"?"Lokalnie — SAP niepodłączony":kind=="Return"?"Do ręcznego rozliczenia":"Raport inwentaryzacyjny";
  using(var q=Cmd(c,"INSERT Operations VALUES(@id,@k,@e,@o,@s,SYSUTCDATETIME(),@st)",t,("@id",id),("@k",kind),("@e",employee.Id),("@o",order),("@s",source),("@st",settlement)))await q.ExecuteNonQueryAsync();
  foreach(var epc in epcs.OrderBy(x=>x,StringComparer.Ordinal))
  {
   using var q=Cmd(c,"SELECT State FROM Parts WITH(UPDLOCK,HOLDLOCK) WHERE Epc=@epc",t,("@epc",epc));var state=await q.ExecuteScalarAsync() as string??throw new InvalidOperationException($"Nieznany EPC: {epc}");
   if(kind=="Issue"&&state!="Available")throw new InvalidOperationException($"Część niedostępna: {epc}");
   if(kind=="Return") { using var v=Cmd(c,"SELECT COUNT(*) FROM Items WHERE OperationId=@s AND Epc=@epc",t,("@s",source),("@epc",epc));if(state!="Issued"||(int)(await v.ExecuteScalarAsync())!!=1)throw new InvalidOperationException($"EPC nie może być zwrócony z tego pobrania: {epc}"); }
   // Starsze bazy miały obowiązkową kolumnę Items.Status. Obsługujemy oba warianty schematu.
   var itemStatus=kind=="Issue"?"Issued":kind=="Return"?"Returned":"Available";
   using var item=Cmd(c,"""
    IF COL_LENGTH('dbo.Items','Tid') IS NOT NULL AND COL_LENGTH('dbo.Items','Status') IS NOT NULL
      INSERT Items(OperationId,Epc,Tid,Material,Name,Status) SELECT @id,Epc,N'',Material,Name,@itemStatus FROM Parts WHERE Epc=@epc;
    ELSE IF COL_LENGTH('dbo.Items','Tid') IS NOT NULL
      INSERT Items(OperationId,Epc,Tid,Material,Name) SELECT @id,Epc,N'',Material,Name FROM Parts WHERE Epc=@epc;
    ELSE IF COL_LENGTH('dbo.Items','Status') IS NOT NULL
      INSERT Items(OperationId,Epc,Material,Name,Status) SELECT @id,Epc,Material,Name,@itemStatus FROM Parts WHERE Epc=@epc;
    ELSE
      INSERT Items(OperationId,Epc,Material,Name) SELECT @id,Epc,Material,Name FROM Parts WHERE Epc=@epc;
    """,t,("@id",id),("@epc",epc),("@itemStatus",itemStatus));await item.ExecuteNonQueryAsync();
   if(kind!="Inventory") {using var update=Cmd(c,"UPDATE Parts SET State=@s WHERE Epc=@epc",t,("@s",kind=="Issue"?"Issued":"ReturnPending"),("@epc",epc));await update.ExecuteNonQueryAsync();}
  }
  if(kind=="Issue") {using var q=Cmd(c,"INSERT Outbox(OperationId,Status) VALUES(@id,'Disabled')",t,("@id",id));await q.ExecuteNonQueryAsync();}
  using(var q=Cmd(c,"INSERT Audit(EmployeeId,Event,Details) VALUES(@e,'Confirmed',@d)",t,("@e",employee.Id),("@d",id.ToString())))await q.ExecuteNonQueryAsync();
  t.Commit();
 }
 public async Task<List<HistoryRow>> History(DateTime from,DateTime until,long employee)
 {
  using var c=await Open();using var q=Cmd(c,"SELECT o.Id,o.CreatedUtc,o.Kind,e.Name,COALESCE(o.OrderNumber,''),i.Epc,i.Material,i.Name,o.Settlement FROM Operations o JOIN Employees e ON e.Id=o.EmployeeId JOIN (SELECT OperationId,Epc,Material,Name FROM Items UNION ALL SELECT OperationId,Epc,Material,Name FROM InventoryReadings) i ON i.OperationId=o.Id WHERE o.CreatedUtc>=@f AND o.CreatedUtc<@u AND o.EmployeeId=@e ORDER BY o.CreatedUtc DESC",null,("@f",from.ToUniversalTime()),("@u",until.ToUniversalTime()),("@e",employee));using var r=await q.ExecuteReaderAsync();var a=new List<HistoryRow>();while(await r.ReadAsync())a.Add(new(r.GetGuid(0),DateTime.SpecifyKind(r.GetDateTime(1),DateTimeKind.Utc).ToLocalTime(),r.GetString(2) switch {"Issue"=>"Pobranie","Return"=>"Zwrot",_=>"Inwentaryzacja"},r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8)));return a;
 }
}
