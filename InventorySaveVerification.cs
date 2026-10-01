using System.IO;
using Microsoft.Data.SqlClient;

namespace PartsBox;
public sealed partial class MainViewModel
{
 public async Task VerifyInventorySave()
 {
  if(!Demo)throw new InvalidOperationException("Test zapisu wymaga wejścia testowego.");
  var saved=new List<(Guid id,string path)>();
  try
  {
   await Boot();Card="31533344";await Invoke(LoginCommand);await Invoke(InventoryPageCommand);
   var loggedEmployee=employee;
   for(var report=0;report<2;report++)
   {
   await Invoke(StartCommand);
   Assert(!InventoryBackCommand.CanExecute(null),"Powrót dostępny podczas odczytu");
   var testId=operation;var epc="FFFFFFFF"+Guid.NewGuid().ToString("N").ToUpperInvariant();ReceiveBarcode(epc);Add(epc);
   Assert(Rows.Count==1&&!ConfirmCommand.CanExecute(null),"Nie można zapisać kontroli nieznanego EPC.");
   Directory.CreateDirectory(InventoryReport.DirectoryPath);
   var path=Path.Combine(InventoryReport.DirectoryPath,Path.ChangeExtension(inventoryFile,report==0?".csv":".xlsx"));saved.Add((testId,path));
   await Invoke(StopCommand);Assert(ConfirmCommand.CanExecute(null),"Stop nie udostępnił zapisu kontroli");
   ChooseExportPath=_=>null;await Invoke(ConfirmCommand);Assert(Rows.Count==1&&operation==testId&&!pendingSave,"Anulowanie wyboru pliku straciło odczyty");
   ChooseExportPath=_=>path;await Invoke(ConfirmCommand);
   Assert(ScanPage&&Logged&&ReferenceEquals(employee,loggedEmployee)&&File.Exists(path)&&ReportConfirmation.Contains(path)&&HasReportConfirmation,"Zapis nie zachował sesji lub potwierdzenia: "+Message);
   Assert(Rows.Count==0&&TagCount==0&&!scanning&&StartCommand.CanExecute(null)&&!ConfirmCommand.CanExecute(null)&&operation!=testId,"Brak gotowości następnej kontroli");
   if(report==0){var csv=File.ReadAllLines(path);Assert(csv.Length==2&&csv[1].Contains(epc)&&csv[1].Contains("TARC19571"),"CSV nie zawiera listy odczytów.");}
   else {using var zip=System.IO.Compression.ZipFile.OpenRead(path);using var text=new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());var xml=text.ReadToEnd();Assert(xml.Contains(epc)&&xml.Contains("TARC19571"),"Excel nie zawiera listy odczytów");}
   }
   Assert(saved[0].path!=saved[1].path,"Kolejny raport nadpisał poprzedni");
   Assert(InventoryBackCommand.CanExecute(null),"Brak powrotu po zapisanej kontroli");
   await Invoke(InventoryBackCommand);
   Assert(MenuPage&&Logged&&!CancelVisible&&ReferenceEquals(employee,loggedEmployee),"Powrót nie zachował sesji lub wyświetlił anulowanie");
   File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"inventory-save-test-result.txt"),"PASS: dwa kolejne raporty CSV bez wylogowania; potwierdzenie zapisu; wyczyszczenie listy; zatrzymany czytnik; gotowy Start; różne pliki i identyfikatory. Folder: "+InventoryReport.DirectoryPath);
  }
  finally
  {
   await Close();
   foreach(var (testId,path) in saved)
   {
    using var c=new SqlConnection(settings.ConnectionString);await c.OpenAsync();using var q=c.CreateCommand();
    q.CommandText="BEGIN TRANSACTION; DELETE InventoryReadings WHERE OperationId=@id; DELETE Audit WHERE Event='InventoryConfirmed' AND Details=@text; DELETE Operations WHERE Id=@id; COMMIT;";
    q.Parameters.AddWithValue("@id",testId);q.Parameters.AddWithValue("@text",testId.ToString());await q.ExecuteNonQueryAsync();
    if(File.Exists(path))File.Delete(path);
   }
   SqlConnection.ClearAllPools();
  }
 }
}

