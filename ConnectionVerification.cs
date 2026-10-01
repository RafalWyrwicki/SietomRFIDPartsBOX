using System.IO;
using Microsoft.Data.SqlClient;

namespace PartsBox;
public sealed partial class MainViewModel
{
 public async Task VerifyConnections(Func<string,Task> capture)
 {
  var lines=new List<string>();
  await Boot();Assert(Ready&&!scanning&&reader is ZebraReader {IsConnected:true},"Brak gotowości po starcie: "+ConnectionMessage);
  await capture("15-polaczenia-gotowe");lines.Add("PASS: automatyczne połączenie FX7500 bez odczytu, SQL i właściwy czytnik USB dostępne.");
  var expectedId=settings.CardReaderDeviceId;
  try
  {
   settings.CardReaderDeviceId="NIEISTNIEJACY_CZYTNIK_TEST";await CheckConnections(false);
   Assert(!Ready&&!LoginCommand.CanExecute(null)&&!cardReady,"Brak czytnika nie blokuje logowania");await capture("16-brak-czytnika-kart");
  }
  finally{settings.CardReaderDeviceId=expectedId;await CheckConnections(false);}
  settings.RequireSapConnection=true;await CheckConnections(false);Assert(!Ready&&!LoginCommand.CanExecute(null),"Wymagany SAP nie blokuje pracy");await capture("17-wymagany-SAP");settings.RequireSapConnection=false;
  await CheckConnections(false);Assert(Ready,"Nie przywrócono gotowości");
  lines.Add("PASS: brak skonfigurowanego czytnika USB i wymagany, niedostępny SAP blokują logowanie; przywrócenie konfiguracji odblokowuje.");
  var originalReader=reader;Card="31533344";await Invoke(LoginCommand);await Invoke(InventoryPageCommand);await Invoke(StartCommand);
  Assert(scanning&&ReferenceEquals(originalReader,reader),"Start otworzył nowe połączenie");await Task.Delay(500);await Invoke(StopCommand);await Invoke(StartCommand);Assert(ReferenceEquals(originalReader,reader),"Wznowienie otworzyło nowe połączenie");await Invoke(StopCommand);await Invoke(ClearCommand);
  lines.Add("PASS: Start i wznowienie używają istniejącego połączenia; Stop i Wyczyść działają.");
  await Task.Run(reader!.Dispose);await CheckConnections(false);Assert(!Ready&&!StartCommand.CanExecute(null),"Utrata RFID nie blokuje pracy");await capture("18-brak-RFID");
  await Invoke(ReconnectCommand);Assert(Ready,"Ponowienie RFID nie przywróciło połączenia");
  lines.Add("PASS: utrata połączenia RFID blokuje pracę, Ponów połączenie przywraca gotowość.");
  await Close();await Close();Assert(reader==null&&!Logged&&closed&&timers.All(t=>!t.IsEnabled),"Zamykanie nie zwolniło sesji");
  lines.Add("PASS: zamknięcie zwalnia RFID, sesję pracownika, timery i pule SQL; ponowne zamknięcie jest bezpieczne.");
  var invalid=new SqlConnectionStringBuilder(settings.ConnectionString){DataSource="tcp:127.0.0.1,1",ConnectTimeout=1};
  var broken=new MainViewModel(new Settings{ConnectionString=invalid.ConnectionString,DemoMode=true});
  try{await broken.Boot();Assert(!broken.Ready&&!broken.LoginCommand.CanExecute(null)&&!broken.sqlReady,"Brak SQL nie blokuje pracy");}
  finally{await broken.Close();}
  lines.Add("PASS: niedostępny SQL blokuje logowanie, aplikację można zamknąć mimo awarii SQL.");
  var recovery=new MainViewModel(new Settings{ConnectionString=settings.ConnectionString,DemoMode=true});
  string? recoveryPath=null;
  try
  {
   await recovery.Boot();recovery.Card="31533344";await recovery.Invoke(recovery.LoginCommand);await recovery.Invoke(recovery.InventoryPageCommand);await recovery.Invoke(recovery.StartCommand);
   recovery.Add("FFFFFFFF12345678");
   recoveryPath=Path.Combine(AppContext.BaseDirectory,"odzyskiwanie",recovery.operation+".json");
   await recovery.Close();
   Assert(!recovery.scanning&&recovery.reader==null&&File.ReadAllText(recoveryPath).Contains("FFFFFFFF12345678"),"Nie zabezpieczono niezapisanej listy");
  }
  finally{await recovery.Close();if(recoveryPath!=null&&File.Exists(recoveryPath))File.Delete(recoveryPath);}
  lines.Add("PASS: zamknięcie podczas odczytu zatrzymuje go i zabezpiecza niezapisane EPC; własny plik testowy usunięty.");
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"connections-test-result.txt"),lines);
 }
}
