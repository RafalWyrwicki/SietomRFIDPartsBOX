using System.IO;
using Microsoft.Data.SqlClient;

namespace PartsBox;
public static class InventoryVerification
{
 public static async Task Run(Settings settings)
 {
  var db=new Database(settings.ConnectionString);await db.Initialize(false);
  var employee=await db.Login("31533344")??throw new Exception("Brak pracownika testowego.");
  var id=Guid.NewGuid();var now=DateTime.Now;
  var rows=new[]{new ScanRow("FFFFFFFF"+id.ToString("N").ToUpperInvariant(),"—","Test; cytat \" i polskie ąę","Nieznany EPC")};
  string? path=null;
  try
  {
   await db.SaveInventory(id,employee,rows);await db.SaveInventory(id,employee,rows);
   var history=await db.History(DateTime.Today.AddDays(-1),DateTime.Today.AddDays(1),employee.Id);
   if(history.Count(x=>x.Id==id)!=1)throw new Exception("Brak zapisu albo powielona kontrola w SQL.");
   path=InventoryReport.Write(InventoryReport.FileName(id,now),id,now,employee,rows);
   if(InventoryReport.Write(Path.GetFileName(path),id,now,employee,rows)!=path)throw new Exception("Ponowienie raportu zmieniło plik.");
   var bytes=File.ReadAllBytes(path);var content=File.ReadAllText(path);
   if(!bytes.Take(3).SequenceEqual(new byte[]{239,187,191})||!content.Contains(rows[0].EPC)||!content.Contains("\"Test; cytat \"\" i polskie ąę\"")||!content.Contains(employee.Barcode))throw new Exception("Niepoprawny CSV.");
   File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"inventory-test-result.txt"),"PASS: SQL, nieznany EPC, historia, brak duplikatu przy ponowieniu, CSV UTF-8 BOM, separator i cudzysłowy, dane pracownika. Dane testowe usunięto po teście. "+DateTime.Now);
  }
  finally
  {
   using var c=new SqlConnection(settings.ConnectionString);await c.OpenAsync();
   using var q=c.CreateCommand();q.CommandText="BEGIN TRANSACTION; DELETE InventoryReadings WHERE OperationId=@id; DELETE Audit WHERE Event='InventoryConfirmed' AND Details=@text; DELETE Operations WHERE Id=@id; COMMIT;";
   q.Parameters.AddWithValue("@id",id);q.Parameters.AddWithValue("@text",id.ToString());await q.ExecuteNonQueryAsync();
   if(path!=null)File.Delete(path);
  }
 }
}
