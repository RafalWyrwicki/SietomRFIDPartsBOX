using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Data.SqlClient;

namespace PartsBox;
public sealed partial class MainViewModel
{
 readonly List<DispatcherTimer> timers=[];
 bool sqlReady,rfidReady,cardReady,shutdownRequested,closed,checking,readerFailed,sqlInitialized;
 string sqlError="Trwa sprawdzanie SQL.",readerError="Trwa łączenie FX7500.",cardError="Trwa sprawdzanie czytnika kart.";
 public bool Ready=>!shutdownRequested&&sqlReady&&rfidReady&&cardReady&&!settings.RequireSapConnection;
 public bool ConnectionProblem=>!Ready&&!shutdownRequested;
 public string ConnectionMessage=>"Stanowisko zablokowane. "+string.Join(" ",new[]{!sqlReady?sqlError:"",!rfidReady?readerError:"",!cardReady?cardError:"",settings.RequireSapConnection?"SAP jest wymagany, ale integracja nie została skonfigurowana.":""}.Where(s=>s.Length>0))+" Usuń przyczynę i wybierz Ponów połączenie. Odczyt po awarii wymaga wyczyszczenia i ponownego skanowania.";
 public string SapStatus=>settings.RequireSapConnection?"SAP: brak połączenia":"SAP: tryb lokalny";
 public Brush SapColor=>settings.RequireSapConnection?Brushes.Salmon:Brushes.Goldenrod;
 public Brush CardColor=>cardReady?Brushes.LightGreen:Brushes.Salmon;
 public Command ReconnectCommand {get;private set;}=null!;
 void SetupConnections()
 {
  ReconnectCommand=new(()=>Run(async()=>{await CheckConnections(true);}),()=>!busy&&!checking&&!shutdownRequested&&!scanning);
  var health=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};
  health.Tick+=async(_,_)=>{if(!busy&&!checking&&!shutdownRequested)await CheckConnections(false);};
  timers.Add(health);health.Start();
 }
 async Task CheckConnections(bool reconnect,bool initialize=false)
 {
  if(checking||shutdownRequested)return;checking=true;
  try
  {
   await Task.WhenAll(CheckSql(initialize),CheckReader(reconnect),CheckCard());
   if(!Ready&&scanning){faulted=true;try{await Stop();}catch(Exception ex){LogCloseError(ex);}Message="Praca jest wstrzymana do przywrócenia wymaganych połączeń.";}
   else if(!Ready)Message="Praca jest wstrzymana do przywrócenia wymaganych połączeń.";
   else if(reconnect||Message.StartsWith("Praca jest wstrzymana"))Message=Logged?(faulted?"Połączenia przywrócone. Wyczyść listę i ponów odczyt.":"Połączenia gotowe. Możesz kontynuować."):"Stanowisko gotowe. Przyłóż kartę do czytnika.";
  }
  finally{checking=false;UpdateScreen();Changed(nameof(Editing));System.Windows.Input.CommandManager.InvalidateRequerySuggested();}
 }
 async Task CheckSql(bool initialize)
 {
  try{if(initialize||!sqlInitialized){await db.Initialize(settings.DemoMode);sqlInitialized=true;}else await db.Ping();sqlReady=true;Sql="SQL: połączony";sqlError="";}
  catch(Exception ex){sqlReady=false;Sql="SQL: brak połączenia";sqlError="Brak połączenia z bazą SQL. Sprawdź sieć i usługę SQL Server.";LogCloseError(ex);}
 }
 async Task CheckCard()
 {
  try{
   // Czytnik RDR-80531BKU emuluje klawiaturę, ale Windows nie udostępnia
   // poprawnego identyfikatora USB. W trybie AUTO potwierdzeniem działania
   // jest obsługa wejścia klawiaturowego, więc nie blokujemy stanowiska.
   cardReady=Demo||string.Equals(settings.CardReaderDeviceId,"AUTO",StringComparison.OrdinalIgnoreCase)||await Task.Run(()=>CardReaderPresence.IsPresent(settings.CardReaderDeviceId));
   cardError=cardReady?"":"Czytnik kart jest odłączony lub niezidentyfikowany. Sprawdź przewód USB.";
  }
  catch(Exception ex){cardReady=false;cardError="Nie można sprawdzić czytnika kart. Sprawdź konfigurację USB.";LogCloseError(ex);}
 }
 async Task CheckReader(bool reconnect)
 {
  try
  {
   if(Demo){if(reader==null){reader=new DemoReader();AttachTags(reader);}rfidReady=true;return;}
   if(!readerFailed&&reader is ZebraReader {IsConnected:true}){rfidReady=true;return;}
   rfidReady=false;
   if(!reconnect){readerError="Brak połączenia z FX7500. Sprawdź zasilanie, sieć i adres czytnika.";return;}
   if(reader!=null){try{await Task.Run(reader.Dispose);}catch{}reader=null;}
   reader=await ZebraReader.Open(settings);
   AttachTags(reader);readerFailed=false;
   var connectedReader=reader;
   reader.Fault+=error=>Application.Current.Dispatcher.BeginInvoke(()=>
   {
    if(shutdownRequested||!ReferenceEquals(reader,connectedReader))return;
    rfidReady=false;readerFailed=true;readerError=error;faulted=ScanPage;Message=ConnectionMessage;UpdateScreen();
    if(scanning&&!checking&&!busy)_=CheckConnections(false);
   });
   rfidReady=true;readerError="";
  }
  catch(Exception ex){rfidReady=false;readerError="Brak połączenia z FX7500. Sprawdź zasilanie, sieć i adres czytnika.";LogCloseError(ex);}
 }
 void AttachTags(IReader source)
 {
  source.Tag+=tag=>{var epoch=generation;Application.Current.Dispatcher.BeginInvoke(()=>{if(!shutdownRequested&&ReferenceEquals(source,reader)&&epoch==generation&&scanning&&Ready)Add(tag);});};
 }
 static void LogCloseError(Exception ex)
 {
  try{File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"connection-errors.log"),DateTime.Now.ToString("O")+" "+ex+System.Environment.NewLine);}catch{}
 }
 public async Task ShutdownConnections()
 {
  if(closed)return;
  shutdownRequested=true;foreach(var timer in timers)timer.Stop();UpdateScreen();
  while(busy||checking)await Task.Delay(50);
  // Preserve an unfinished operation before releasing its session, including an uncertain SQL save.
  if(Rows.Count>0||pendingSave)
  {
   try
   {
    var folder=Path.Combine(AppContext.BaseDirectory,"odzyskiwanie");Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder,operation+".json"),JsonSerializer.Serialize(new{operation,Mode,employee,pendingSave,inventoryCreated,inventoryFile,Rows},new JsonSerializerOptions{WriteIndented=true}));
   }
   catch{shutdownRequested=false;foreach(var timer in timers)timer.Start();throw;}
  }
  try{await Stop();}catch(Exception ex){LogCloseError(ex);}
  try{if(reader!=null)await Task.Run(reader.Dispose);}catch(Exception ex){LogCloseError(ex);}
  finally{reader=null;rfidReady=false;generation++;}
  try{if(employee!=null)await db.Audit(employee.Id,"ApplicationClosed",operation.ToString());}catch(Exception ex){LogCloseError(ex);}
  finally
  {
   employee=null;Card="";Rows.Clear();History.Clear();Orders.Clear();Receipts.Clear();
   SqlConnection.ClearAllPools();sqlReady=false;cardReady=false;closed=true;
  }
 }
}

public sealed partial class Database
{
 public async Task Ping(){using var c=await Open();using var q=c.CreateCommand();q.CommandText="SELECT 1";q.CommandTimeout=3;await q.ExecuteScalarAsync();}
}

