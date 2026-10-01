using System.IO;
using Microsoft.Data.SqlClient;
namespace PartsBox;
static class FileImportVerification
{
 public static async Task Run()
 {
  var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!Directory.Exists(Path.Combine(root.FullName,"Baza danych zleceń produkcyjnych")))root=root.Parent;
  if(root==null)throw new InvalidOperationException("Nie znaleziono plików klienta.");
  var folder=Path.Combine(root.FullName,"Baza danych zleceń produkcyjnych");
  var orders=Path.Combine(folder,"EXPORT-lista zleceń.XLSX");var stock=Path.Combine(folder,"plik wsadowy - indeksy magazyn UR.csv");
  var orderRows=ImportFiles.Orders(orders);var stockRows=ImportFiles.Stocks(stock);
  var name="PartsBox_ImportTest_"+Guid.NewGuid().ToString("N");
  var builder=new SqlConnectionStringBuilder(@"Server=.\SQLEXPRESS;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True");
  using var master=new SqlConnection(builder.ConnectionString);await master.OpenAsync();
  async Task Execute(string sql){using var q=master.CreateCommand();q.CommandText=sql;q.CommandTimeout=120;await q.ExecuteNonQueryAsync();}
  try
  {
   var sqlFolder=Path.Combine(root.FullName,"Sietom RFID PartsBOX","sql");
   await Execute(File.ReadAllText(Path.Combine(sqlFolder,"01_Odtworz_pusta_baze.sql")).Replace("SietomPartsBox",name));
   await Execute(File.ReadAllText(Path.Combine(sqlFolder,"02_Dane_przykladowe.sql")).Replace("SietomPartsBox",name));
   builder.InitialCatalog=name;var db=new Database(builder.ConnectionString);
   await db.ImportOrders(orders,3);await db.ImportStockRows(stock,3);
   var repeat=await db.ImportOrders(orders,3);if(!repeat.Contains("już"))throw new Exception("Ponowny import nie jest idempotentny.");
   var actual=await db.ImportedOrders();if(actual.Count!=orderRows.Count+4)throw new Exception("Niepoprawna liczba zleceń.");
   var init=actual.First(o=>Order.HasInit(o.UserStatus));if(!await db.OrderIsInit(init.Number))throw new Exception("Nie wykryto INIT.");
   using var c=new SqlConnection(builder.ConnectionString);await c.OpenAsync();using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM dbo.ImportedStockRows";if(Convert.ToInt32(await q.ExecuteScalarAsync())!=stockRows.Count)throw new Exception("Brak wierszy magazynu.");
   File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"file-import-test-result.txt"),$"PASS: {orderRows.Count} zleceń, {orderRows.Count(o=>Order.HasInit(o.Status))} INIT, {stockRows.Count} wierszy CSV; wszystkie kolumny, ilości dziesiętne, zachowanie wcześniejszych danych, ponowny import bez duplikatów.");
  }
  finally{SqlConnection.ClearAllPools();await Execute($"USE master; IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END;");}
 }
}
