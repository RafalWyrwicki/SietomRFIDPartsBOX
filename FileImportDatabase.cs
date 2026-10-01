using System.Data;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
namespace PartsBox;
public sealed partial class Database
{
 public async Task EnsureImportSchema()
 {
  using var stream=typeof(Database).Assembly.GetManifestResourceStream("PartsBox.ImportSchema.sql")!;using var text=new StreamReader(stream);
  using var c=await Open();using var q=Cmd(c,await text.ReadToEndAsync());await q.ExecuteNonQueryAsync();
 }
 public async Task<string> ImportOrders(string path,long employee)
 {
  var data=ImportFiles.Orders(path);await EnsureImportSchema();using var c=await Open();using var t=c.BeginTransaction(IsolationLevel.Serializable);
  var id=Guid.NewGuid();if(!await RegisterImport(c,t,id,"Orders",path,data.Count,employee)){t.Rollback();return "Ten plik zleceń został już zaimportowany.";}
  using(var q=Cmd(c,"CREATE TABLE #Orders(Number nvarchar(50),Description nvarchar(1000),CreatedDate date,UserStatus nvarchar(120),Equipment nvarchar(80),EquipmentDescription nvarchar(500),LocationDescription nvarchar(500),FunctionalLocation nvarchar(150));",t))await q.ExecuteNonQueryAsync();
  var table=new DataTable();foreach(var name in new[]{"Number","Description","CreatedDate","UserStatus","Equipment","EquipmentDescription","LocationDescription","FunctionalLocation"})table.Columns.Add(name,name=="CreatedDate"?typeof(DateTime):typeof(string));
  foreach(var r in data)table.Rows.Add(r.Number,r.Description,r.Date,r.Status,r.Equipment,r.EquipmentDescription,r.LocationDescription,r.Location);
  await Bulk(c,t,"#Orders",table);
  using(var q=Cmd(c,"""
   -- Plik jest pełnym zasileniem listy. Zlecenia nieobecne w nowym pliku
   -- pozostają w historii, ale nie są już dostępne operatorowi.
   UPDATE dbo.Orders SET Active=0
   WHERE Active=1 AND NOT EXISTS(SELECT 1 FROM #Orders n WHERE n.Number=dbo.Orders.Number);
   UPDATE o SET Description=s.Description,CreatedDate=s.CreatedDate,UserStatus=s.UserStatus,Equipment=s.Equipment,EquipmentDescription=s.EquipmentDescription,LocationDescription=s.LocationDescription,FunctionalLocation=s.FunctionalLocation,ImportId=@id,Active=1 FROM dbo.Orders o JOIN #Orders s ON s.Number=o.Number;
   INSERT dbo.Orders(Number,Description,Active,CreatedDate,UserStatus,Equipment,EquipmentDescription,LocationDescription,FunctionalLocation,ImportId)
   SELECT s.Number,s.Description,1,s.CreatedDate,s.UserStatus,s.Equipment,s.EquipmentDescription,s.LocationDescription,s.FunctionalLocation,@id FROM #Orders s WHERE NOT EXISTS(SELECT 1 FROM dbo.Orders o WHERE o.Number=s.Number);
   INSERT dbo.Audit(EmployeeId,Event,Details) VALUES(@employee,'OrdersImported',CONVERT(nvarchar(36),@id));
   """,t,("@id",id),("@employee",employee)))await q.ExecuteNonQueryAsync();
  t.Commit();return $"Zaimportowano {data.Count} zleceń. Zlecenia INIT pozostają widoczne, ale nie można na nie pobierać części.";
 }
 public async Task<string> ImportStockRows(string path,long employee)
 {
  var data=ImportFiles.Stocks(path);await EnsureImportSchema();using var c=await Open();using var t=c.BeginTransaction(IsolationLevel.Serializable);
  var id=Guid.NewGuid();if(!await RegisterImport(c,t,id,"Stock",path,data.Count,employee)){t.Rollback();return "Ten plik magazynowy został już zaimportowany.";}
  var table=new DataTable();table.Columns.Add("ImportId",typeof(Guid));table.Columns.Add("RowNumber",typeof(int));foreach(var name in new[]{"Material","Description","Unit","InventoryNumber","StorageLocation","Plant","Epc","Barcode"})table.Columns.Add(name,typeof(string));table.Columns.Add("Quantity",typeof(decimal));
  foreach(var r in data)table.Rows.Add(id,r.Row,r.Material,r.Description,r.Unit,r.InventoryNumber,r.StorageLocation,r.Plant,r.Epc,r.Barcode,r.Quantity);
  await Bulk(c,t,"dbo.ImportedStockRows",table);
  using(var q=Cmd(c,"""
   -- CSV jest pełnym zasileniem magazynu. Usuwamy poprzednie wiersze
   -- źródłowe i zerujemy ilości, zanim wstawimy aktualne agregaty.
   DELETE s FROM dbo.ImportedStockRows s JOIN dbo.FileImports f ON f.Id=s.ImportId WHERE f.Kind=N'Stock' AND s.ImportId<>@id;
   DELETE FROM dbo.FileImports WHERE Kind=N'Stock' AND Id<>@id;
   UPDATE dbo.Parts SET StockQuantity=0,State=CASE WHEN State IN (N'Issued',N'ReturnPending') THEN State ELSE N'Unavailable' END;
   SELECT Epc,MAX(Material) Material,MAX(Description) Description,MAX(Barcode) Barcode,SUM(Quantity) Quantity
   INTO #groupedStock
   FROM dbo.ImportedStockRows WHERE ImportId=@id GROUP BY Epc;
   UPDATE p SET Material=g.Material,Name=LEFT(g.Description,250),StockQuantity=g.Quantity,Barcode=g.Barcode,
     State=CASE WHEN p.State IN (N'Issued',N'ReturnPending') THEN p.State ELSE N'Available' END
   FROM dbo.Parts p JOIN #groupedStock g ON g.Epc=p.Epc;
   INSERT dbo.Parts(Epc,Material,Name,State,StockQuantity,Barcode)
   SELECT g.Epc,g.Material,LEFT(g.Description,250),N'Available',g.Quantity,g.Barcode
   FROM #groupedStock g WHERE NOT EXISTS(SELECT 1 FROM dbo.Parts p WHERE p.Epc=g.Epc);
   INSERT dbo.Audit(EmployeeId,Event,Details) VALUES(@employee,'StockFileImported',CONVERT(nvarchar(36),@id));
   """,t,("@id",id),("@employee",employee)))await q.ExecuteNonQueryAsync();
  t.Commit();return $"Wczytano {data.Count} wierszy źródłowych magazynu.";
 }
 static async Task Bulk(SqlConnection c,SqlTransaction t,string destination,DataTable data)
 {
  using var bulk=new SqlBulkCopy(c,SqlBulkCopyOptions.CheckConstraints,t){DestinationTableName=destination,BulkCopyTimeout=120};foreach(DataColumn col in data.Columns)bulk.ColumnMappings.Add(col.ColumnName,col.ColumnName);await bulk.WriteToServerAsync(data);
 }
 static async Task<bool> RegisterImport(SqlConnection c,SqlTransaction t,Guid id,string kind,string path,int rows,long employee)
 {
  var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
  using var q=Cmd(c,"IF EXISTS(SELECT 1 FROM dbo.FileImports WITH(UPDLOCK,HOLDLOCK) WHERE Kind=@kind AND FileHash=@hash) SELECT 0; ELSE BEGIN INSERT dbo.FileImports(Id,Kind,FileName,FileHash,[RowCount],EmployeeId) VALUES(@id,@kind,@file,@hash,@rows,@employee); SELECT 1; END",t,("@id",id),("@kind",kind),("@file",Path.GetFileName(path)),("@hash",hash),("@rows",rows),("@employee",employee));return Convert.ToInt32(await q.ExecuteScalarAsync())==1;
 }
 public async Task<List<Order>> ImportedOrders()
 {
  using var c=await Open();using(var check=Cmd(c,"SELECT COL_LENGTH('dbo.Orders','CreatedDate')"))if(await check.ExecuteScalarAsync() is DBNull)return await Orders();
  using var q=Cmd(c,"SELECT Number,Description,CreatedDate,UserStatus,Equipment,EquipmentDescription,LocationDescription,FunctionalLocation FROM dbo.Orders WHERE Active=1 ORDER BY CreatedDate DESC,Number");using var r=await q.ExecuteReaderAsync();var result=new List<Order>();
  while(await r.ReadAsync())result.Add(new(r.GetString(0),r.GetString(1),r.IsDBNull(2)?null:r.GetDateTime(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7)));return result;
 }
 public async Task<bool> OrderIsInit(string number)
 {
  using var c=await Open();using(var check=Cmd(c,"SELECT COL_LENGTH('dbo.Orders','UserStatus')"))if(await check.ExecuteScalarAsync() is DBNull)return false;
  using var q=Cmd(c,"SELECT UserStatus FROM dbo.Orders WHERE Number=@n",null,("@n",number));var status=await q.ExecuteScalarAsync() as string??"";return Order.HasInit(status);
 }
}
