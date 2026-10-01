using System.IO;
namespace PartsBox;
public static class SelfTest
{
 public static async Task Cards(Settings settings)
 {
  var db=new Database(settings.ConnectionString);await db.Initialize(settings.DemoMode);
  var artur=await db.Login("31533344");var julia=await db.Login("31534052\r\n");
  if(artur is null||artur.Name!="Jabłoński Artur"||artur.Barcode!="TARC19571"||artur.Position!="Zastępca Managera Obszaru Produkcyjnego")throw new Exception("Pierwsza karta: niezgodne dane.");
  if(julia is null||julia.Name!="Wywrot Julia"||julia.Barcode!="TARC19687"||julia.Position!="Stażystka w Dziale Zapewnienia Jakości"||julia.Id==artur.Id)throw new Exception("Druga karta: niezgodne dane.");
  if(await db.Login("99999999")!=null||await db.Login("031533344")!=null)throw new Exception("Nieznana karta zaakceptowana.");
  await db.Initialize(settings.DemoMode);if((await db.Login("31533344"))?.Id!=artur.Id)throw new Exception("Inicjalizacja zmieniła przypisanie.");
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"card-test-result.txt"),"PASS: obie karty, nazwiska, kody kreskowe, stanowiska, CR/LF, nieznany numer, wiodące zero, ponowna inicjalizacja. "+DateTime.Now);
 }
 public static async Task Run(Settings settings)
 {
  var db=new Database(settings.ConnectionString);await db.Initialize(true);
  var user=await db.Login("240,12345")??throw new Exception("Login failed");
  if(await db.Login("240,999999")!=null)throw new Exception("Unknown card accepted");
  var testEpc="TEST"+Guid.NewGuid().ToString("N");
  using(var c=new Microsoft.Data.SqlClient.SqlConnection(settings.ConnectionString)){await c.OpenAsync();using var q=c.CreateCommand();q.CommandText="INSERT Parts VALUES(@e,'TEST','Test integracyjny','Available')";q.Parameters.AddWithValue("@e",testEpc);await q.ExecuteNonQueryAsync();}
  var tag=new Part(testEpc,"TEST","Test integracyjny","Available");
  var id=Guid.NewGuid();await db.Save(id,"Issue",user,"400012345",null,[tag.Epc]);await db.Save(id,"Issue",user,"400012345",null,[tag.Epc]);
  try{await db.Save(Guid.NewGuid(),"Issue",user,"400012345",null,[tag.Epc]);throw new Exception("Double issue accepted");}catch(InvalidOperationException){}
  try{await db.Save(Guid.NewGuid(),"Inventory",user,null,null,["FFFFFFFF"]);throw new Exception("Unknown EPC accepted");}catch(InvalidOperationException){}
  await db.Save(Guid.NewGuid(),"Return",user,null,id,[tag.Epc]);
  if((await db.Parts())[tag.Epc].State!="ReturnPending")throw new Exception("Return state invalid");
  var history=await db.History(DateTime.Today,DateTime.Today.AddDays(1),user.Id);if(history.Count(x=>x.Id==id)!=1)throw new Exception("Idempotency failed");
  var export=Path.Combine(AppContext.BaseDirectory,"test-report.xlsx");ExcelReport.Write(export,history);using(var zip=System.IO.Compression.ZipFile.OpenRead(export)){if(zip.GetEntry("xl/worksheets/sheet1.xml")==null)throw new Exception("XLSX invalid");}
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"self-test-result.txt"),"PASS: SQL initialization, login, unknown card, issue, idempotency, duplicate issue rejection, unknown EPC rollback, return linkage, pending return, persisted history, XLSX export.\n"+DateTime.Now+"\nTest EPC: "+testEpc);
 }
}
