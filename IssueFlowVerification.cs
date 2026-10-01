using System.IO;
using Microsoft.Data.SqlClient;
using System.Windows.Threading;

namespace PartsBox;
public sealed partial class MainViewModel
{
 public async Task VerifyIssueFlow()
 {
  if(!Demo)throw new InvalidOperationException("Test wymaga wejścia testowego.");
  var tags=new[]{"FFFF"+Guid.NewGuid().ToString("N").ToUpperInvariant(),"FFFF"+Guid.NewGuid().ToString("N").ToUpperInvariant()};
  var id=Guid.Empty;var returnId=Guid.Empty;var confirmedQuantity=0;var returnedQuantity=0;
  var export=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".csv");ChooseExportPath=_=>export;
  using var c=new SqlConnection(settings.ConnectionString);await c.OpenAsync();
  async Task<int> Query(string sql)
  {
   using var q=c.CreateCommand();q.CommandText=sql;q.Parameters.AddWithValue("@id",id);q.Parameters.AddWithValue("@return",returnId);q.Parameters.AddWithValue("@a",tags[0]);q.Parameters.AddWithValue("@b",tags[1]);
   return Convert.ToInt32(await q.ExecuteScalarAsync());
  }
  try
  {
   await Boot();
   await Query("INSERT Parts(Epc,Material,Name,State) VALUES(@a,'TEST','Test pobrania A','Available'),(@b,'TEST','Test pobrania B','Available'); SELECT 2;");
   Card="31533344";await Invoke(LoginCommand);await Invoke(IssuePageCommand);await Invoke(SelectOrderCommand);id=operation;
   Assert(ScanPage&&!scanning&&!ConfirmCommand.CanExecute(null)&&!ShowScanErrors,"Zlecenie nie otworzyło gotowego ekranu");
   ReceiveBarcode(tags[0]);Assert(Rows.Count==1&&!scanning,"Skaner nie dodał części bez uruchamiania RFID");
   IssueConfirmed+=quantity=>confirmedQuantity=quantity;
   await Invoke(StartCommand);((DemoReader)reader!).Add(tags[0]);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   Assert(Rows.Count==1&&!ConfirmCommand.CanExecute(null),"Zatwierdzanie dostępne podczas odczytu");
   await Invoke(StopCommand);Assert(ConfirmCommand.CanExecute(null),"Stop nie udostępnił zatwierdzenia");
   await Invoke(StartCommand);Assert(!ConfirmCommand.CanExecute(null)&&Rows.Count==1,"Wznowienie skasowało listę lub nie zablokowało zatwierdzenia");
   ((DemoReader)reader!).Add(tags[0]);((DemoReader)reader!).Add(tags[1]);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   await Invoke(StopCommand);Assert(Rows.Count==2&&ConfirmCommand.CanExecute(null),"Brak deduplikacji lub zatwierdzenia po drugim Stop");
   await Invoke(ConfirmCommand);
   Assert(confirmedQuantity==2&&Message.Contains("2 szt."),"Nie pokazano poprawnej ilości po pobraniu: "+Message);
   Assert(await Query("SELECT COUNT(*) FROM Items i JOIN Parts p ON p.Epc=i.Epc JOIN Operations o ON o.Id=i.OperationId WHERE o.Id=@id AND o.Kind='Issue' AND o.OrderNumber IS NOT NULL AND p.State='Issued'")==2,"Nie zapisano dwóch części jako pobrane");
   Card="31533344";await Invoke(LoginCommand);await Invoke(ReturnPageCommand);
   ReturnSearch="NIEISTNIEJACE-ZLECENIE";Assert(!FilteredReceipts.Any(),"Filtr zwrotu nie działa");ReturnSearch="";
   SelectedReceipt=Receipts.Single(r=>r.Id==id);var exportCalls=0;ChooseExportPath=_=>{exportCalls++;return null;};await Invoke(SelectReturnCommand);returnId=operation;
   Assert(exportCalls==0&&!File.Exists(export),"Rozpoczęcie zwrotu uruchomiło eksport");
   Assert(ScanPage&&!scanning&&!ShowScanErrors&&!ConfirmCommand.CanExecute(null),"Zwrot uruchomił odczyt lub udostępnił puste zatwierdzenie");
   ReceiveBarcode(tags[0]);Assert(Rows.Count==1,"Skaner nie dodał części do zwrotu");
   ReturnConfirmed+=quantity=>returnedQuantity=quantity;
   await Invoke(StartCommand);((DemoReader)reader!).Add(tags[0]);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   Assert(!ConfirmCommand.CanExecute(null),"Zwrot można zatwierdzić podczas odczytu");await Invoke(StopCommand);Assert(ConfirmCommand.CanExecute(null),"Stop nie udostępnił zwrotu");
   await Invoke(StartCommand);((DemoReader)reader!).Add(tags[1]);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);await Invoke(StopCommand);await Invoke(ConfirmCommand);
   Assert(exportCalls==1&&Logged&&ScanPage&&Rows.Count==2&&await Query("SELECT COUNT(*) FROM Operations WHERE Id=@return")==0,"Anulowanie eksportu nie zachowało zwrotu");
   ChooseExportPath=_=>export;await Invoke(ConfirmCommand);
   Assert(File.ReadAllLines(export).Length==3&&File.ReadAllText(export).Contains(returnId.ToString())&&File.ReadAllText(export).Contains(tags[0]),"Raport nie zawiera zatwierdzonego zwrotu");
   Assert(returnedQuantity==2&&!Logged&&LoginPage,"Brak potwierdzenia zwrotu lub wylogowania");
   Assert(await Query("SELECT COUNT(*) FROM Items i JOIN Parts p ON p.Epc=i.Epc JOIN Operations o ON o.Id=i.OperationId WHERE o.Id=@return AND o.SourceId=@id AND o.Kind='Return' AND p.State='ReturnPending'")==2,"Nie zapisano zwrotu dwóch części");
   File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"issue-flow-test-result.txt"),"PASS: pobranie i zwrot: wybór bez odczytu; Start/Stop/Start/Stop; zatwierdzenie tylko po Stop; potwierdzenia 2 szt.; SQL Issue/Return/Items, SourceId, Issued/ReturnPending; wylogowanie po zwrocie. Dane testowe usuwane po teście.");
  }
  finally
  {
   if(File.Exists(export))File.Delete(export);
   Rows.Clear();pendingSave=false;await Close();
   await Query("BEGIN TRANSACTION; DELETE Outbox WHERE OperationId IN(@id,@return); DELETE Items WHERE OperationId IN(@id,@return); DELETE Audit WHERE Event='Confirmed' AND Details IN(CONVERT(nvarchar(36),@id),CONVERT(nvarchar(36),@return)); DELETE Operations WHERE Id=@return; DELETE Operations WHERE Id=@id; DELETE Parts WHERE Epc IN(@a,@b); COMMIT; SELECT 0;");
  }
 }
}
