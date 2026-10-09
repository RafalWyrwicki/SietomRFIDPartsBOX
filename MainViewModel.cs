using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace PartsBox;
public sealed partial class MainViewModel : Observable
{
 readonly Database db; readonly Settings settings; IReader? reader;
 Dictionary<string,Part> parts=new(StringComparer.OrdinalIgnoreCase);
 Employee? employee; Guid operation; int generation; bool busy,scanning,faulted;
 DateTime lastActivity=DateTime.UtcNow; bool pendingSave;
 DateTime inventoryCreated; string inventoryFile="";
 string reportConfirmation="";
 internal Func<string,string?> ChooseExportPath=OperationExport.ChoosePath;
 public string ReportConfirmation=>reportConfirmation;
 public bool HasReportConfirmation=>ScanPage&&reportConfirmation.Length>0;
 public event Action<int>? IssueConfirmed;
 public event Action<int>? ReturnConfirmed;
 public Command StopCommand {get;private set;}=null!;
 string message="Uruchamianie…",card="",epc="",mode="Pobranie",search="";
 string sql="SQL: niepołączony",rfid="RFID: nieaktywny";
 Order? selectedOrder;Receipt? selectedReceipt;
 public ObservableCollection<Order> Orders {get;}=[];
 public ObservableCollection<Receipt> Receipts {get;}=[];
 public ObservableCollection<ScanRow> Rows {get;}=[];
 public ObservableCollection<HistoryRow> History {get;}=[];
 public string[] Modes {get;}=["Pobranie","Zwrot","Inwentaryzacja"];
 public string Mode {get=>mode;set{if(scanning)return;Set(ref mode,value);}}
 public string Card {get=>card;set=>Set(ref card,value);}
 public string EPC {get=>epc;set=>Set(ref epc,value);}
 public string Search {get=>search;set{Set(ref search,value);Changed(nameof(FilteredOrders));}}
 bool numericKeyboardVisible;
 bool numericKeyboardForReturn;
 public bool NumericKeyboardVisible { get=>numericKeyboardVisible; private set=>Set(ref numericKeyboardVisible,value); }
 public void ShowNumericKeyboard(bool forReturn=false){numericKeyboardForReturn=forReturn;NumericKeyboardVisible=true;}
 public void HideNumericKeyboard()=>NumericKeyboardVisible=false;
 public void NumericKey(string key)
 {
  var value=numericKeyboardForReturn?ReturnSearch:Search;
  if(key=="⌫"){if(value.Length>0)value=value[..^1];}
  else if(key=="Wyczyść")value="";
  else if(value.Length<11&&key.Length==1&&char.IsDigit(key[0]))value+=key;
  if(numericKeyboardForReturn)ReturnSearch=value;else Search=value;
 }
 string orderLocation="Wszystkie";
 public string OrderLocation {get=>orderLocation;set{Set(ref orderLocation,value);SelectedOrder=null;Changed(nameof(FilteredOrders));}}
 public IEnumerable<string> OrderLocations=>new[]{"Wszystkie"}.Concat(Orders.Select(o=>o.FunctionalLocation.Trim()).Where(s=>s.Length>0).Select(s=>s.Length>13?s[..13]:s).Distinct().Order());
 public IEnumerable<Order> FilteredOrders=>Orders.Where(o=>o.Number.Contains(Search.Trim(),StringComparison.OrdinalIgnoreCase)&&(OrderLocation=="Wszystkie"||o.FunctionalLocation.StartsWith(OrderLocation,StringComparison.OrdinalIgnoreCase)));
 public Order? SelectedOrder {get=>selectedOrder;set{if(!scanning)Set(ref selectedOrder,value);}}
 public Receipt? SelectedReceipt {get=>selectedReceipt;set{if(!scanning)Set(ref selectedReceipt,value);}}
 public DateTime From {get;set;}=DateTime.Today.AddDays(-7);
 public DateTime Until {get;set;}=DateTime.Today;
 public string Message {get=>message;private set=>Set(ref message,value);}
 public string Sql {get=>sql;private set=>Set(ref sql,value);}
 public string Rfid {get=>rfid;private set=>Set(ref rfid,value);}
 public string Identity=>employee is null?"Przyłóż kartę pracowniczą":$"Witaj, {employee.Name}";
 public string Environment=>settings.DemoMode?"TRYB DEMONSTRACYJNY • dane testowe • brak wysyłki SAP":"TRYB SPRZĘTOWY • lokalna baza rozwojowa • brak wysyłki SAP";
 public bool Logged=>employee!=null;
 public bool Editing=>Logged&&Ready&&!scanning&&!busy&&!checking&&!pendingSave;
 public bool Demo=>settings.DemoMode;
 public string CardReaderIdentifier=>settings.CardReaderDeviceId;
 public bool CanReceiveBarcode=>Logged&&ScanPage&&Ready&&!busy&&!pendingSave&&!CancelVisible&&!faulted&&!shutdownRequested;
 public void ReceiveBarcode(string epc)
 {
  if(!CanReceiveBarcode)return;
  Touch();var count=Rows.Count;Add(epc);
  if(Rows.Count>count)Message="Dodano EPC ze skanera ręcznego. Możesz zeskanować kolejną etykietę.";
  else if(Rows.Any(r=>r.EPC.Equals(epc.Trim(),StringComparison.OrdinalIgnoreCase)))Message="Ten EPC jest już na liście — nie dodano drugiej sztuki.";
 }
 public string Count=>$"{Rows.Count} EPC  /  {Rows.Count(x=>x.Status!="OK")} błędów";
 public Command LoginCommand {get;} public Command StartCommand {get;} public Command ClearCommand {get;} public Command CancelCommand {get;} public Command ConfirmCommand {get;} public Command AddCommand {get;} public Command DemoCommand {get;} public Command RefreshCommand {get;} public Command ExportCommand {get;} public Command LogoutCommand {get;} public Command InitializeCommand {get;}
 public MainViewModel(Settings config)
 {
  settings=config;db=new(config.ConnectionString);InitializeNavigation();SetupConnections();
  InitializeCommand=new(()=>Run(Initialize));
  LoginCommand=new(()=>Run(Login),()=>!Logged&&Ready&&!busy&&!checking);
  StartCommand=new(()=>Run(Start),()=>Editing&&!CancelVisible&&!faulted);
  StopCommand=new(()=>Run(Stop),()=>scanning&&!busy&&!CancelVisible);
  ClearCommand=new(()=>Run(Clear),()=>ScanPage&&!busy&&!pendingSave&&!CancelVisible);
  CancelCommand=new(()=>{CancelVisible=true;return Task.CompletedTask;},()=>Logged&&!busy&&!pendingSave);
  ConfirmCommand=new(()=>Run(Confirm),()=>ScanPage&&Ready&&!faulted&&!busy&&!checking&&!CancelVisible&&!scanning&&Rows.Count>0&&(Kind=="Inventory"||Rows.All(x=>x.Status=="OK")));
  AddCommand=new(()=>Run(()=>{Add(EPC);EPC="";return Task.CompletedTask;}),()=>Demo&&scanning&&!busy);
  DemoCommand=new(()=>Run(()=>{var available=parts.Values.Where(p=>p.Epc.All(Uri.IsHexDigit)&&(Kind=="Issue"?p.State=="Available":Kind=="Return"?p.State=="Issued":true)).Take(2).ToArray();foreach(var p in available)Add(p.Epc);if(available.Length==0)Message="Brak dostępnych części testowych. Zwroty oczekują na ręczne rozliczenie.";return Task.CompletedTask;}),()=>Demo&&scanning&&!busy);
  RefreshCommand=new(()=>Run(Refresh),()=>Logged&&!busy);
  ExportCommand=new(()=>Run(ExportFiltered),()=>Logged&&!busy&&DisplayHistory.Any());
  LogoutCommand=new(()=>Run(()=>End("Logout")),()=>Logged&&!busy&&!pendingSave);
  var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(10)};timer.Tick+=async(_,_)=>{if(Logged&&!busy&&!pendingSave&&DateTime.UtcNow-lastActivity>TimeSpan.FromMinutes(Math.Max(1,settings.SessionTimeoutMinutes)))await Run(()=>End("Timeout"));};timers.Add(timer);timer.Start();
 }
 public void Touch()=>lastActivity=DateTime.UtcNow;
 async Task Run(Func<Task> action) {if(busy||shutdownRequested)return;while(checking&&!shutdownRequested)await Task.Delay(25);if(busy||shutdownRequested)return;busy=true;Changed(nameof(Editing));try{Touch();await action();}catch(Exception ex){if(ex is Microsoft.Data.SqlClient.SqlException sql){LogCloseError(sql);if(sql.Number is 53 or 40 or 67 or 4060 or 18456 or -1){sqlReady=false;Sql="SQL: brak połączenia";sqlError="Nie udało się wykonać operacji SQL. "+sql.Message;}}Message="Błąd: "+ex.Message;}finally{busy=false;Changed(nameof(Editing));UpdateScreen();System.Windows.Input.CommandManager.InvalidateRequerySuggested();}}
 public Task Boot()=>Run(Initialize);
 async Task Initialize(){await CheckConnections(true,true);}
 async Task Login()
 {
  var found=await db.Login(Card);if(found==null){await db.Audit(null,"LoginDenied","Nieznana lub nieaktywna karta");throw new InvalidOperationException("Karta nieznana lub nieaktywna.");}
  await db.Audit(found.Id,"Login","Stanowisko lokalne");employee=found;Card="";Changed(nameof(Logged));Changed(nameof(Identity));
  Orders.Clear();foreach(var o in await db.Orders())Orders.Add(o);Changed(nameof(FilteredOrders));SelectedOrder=Orders.FirstOrDefault();
  Receipts.Clear();foreach(var r in await db.Receipts())Receipts.Add(r);
  Message="Wybierz operację.";await Refresh();Page="Menu";
 }
 string Kind=>Mode switch{"Pobranie"=>"Issue","Zwrot"=>"Return",_=>"Inventory"};
 async Task PrepareScan()
 {
  reportConfirmation="";
  if(Kind=="Issue"&&SelectedOrder==null)throw new InvalidOperationException("Wybierz zlecenie.");
  if(Kind=="Return"&&SelectedReceipt==null)throw new InvalidOperationException("Wybierz pierwotne pobranie.");
  parts=await db.Parts();Rows.Clear();Changed(nameof(Count));operation=Guid.NewGuid();faulted=false;
  inventoryCreated=DateTime.Now;inventoryFile=InventoryReport.FileName(operation,inventoryCreated);
  await db.Audit(employee!.Id,"ScanPrepared",operation.ToString());
  Page="Scan";Message="Naciśnij Start dla RFID lub zeskanuj EPC skanerem ręcznym (Enter/Tab).";UpdateScreen();
 }
 async Task Start()
 {
  reportConfirmation="";
  if(!ScanPage)await PrepareScan();
  if(!Ready||reader==null)throw new InvalidOperationException(ConnectionMessage);
  generation++;
  scanning=true;try{await reader.Start();}catch{scanning=false;rfidReady=false;readerFailed=true;readerError="Nie udało się rozpocząć odczytu FX7500. Ponów połączenie.";throw;}
  Rfid="RFID: odczyt aktywny";Message="Trwa odczyt. Stop zatrzymuje czytnik i zachowuje listę.";Page="Scan";UpdateScreen();
 }
 void Add(string raw)
 {
  var tag=raw.Trim().ToUpperInvariant();if(tag.Length==0)return;
  if(tag.Length>128||tag.Length%2!=0||!tag.All(Uri.IsHexDigit)){Message="EPC musi być ciągiem par znaków szesnastkowych (maks. 128 znaków).";return;}
  if(Rows.Any(x=>x.EPC==tag))return;
  parts.TryGetValue(tag,out var part);var status=part==null?"Nieznany EPC":Kind=="Issue"&&part.State!="Available"?"Część niedostępna":Kind=="Return"&&part.State!="Issued"?"Brak wydania":"OK";
  Rows.Add(new(tag,part?.Material??"—",part?.Name??"Nierozpoznany znacznik",status));Changed(nameof(Count));UpdateScreen();System.Windows.Input.CommandManager.InvalidateRequerySuggested();
 }
 async Task Stop(){try{if(reader!=null&&(scanning||faulted))await reader.Stop();await Application.Current.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);}catch{faulted=true;Rfid="RFID: niepotwierdzone zatrzymanie";throw;}finally{scanning=false;generation++;}Rfid="RFID: zatrzymany";Message=Kind=="Inventory"?"Odczyt zatrzymany. Możesz wznowić przyciskiem Start lub zapisać kontrolę.":Rows.Any(x=>x.Status!="OK")?"Odczyt zatrzymany. Zatwierdzenie wymaga rozpoznanych i dostępnych części — sprawdź statusy na liście.":Kind=="Return"?"Odczyt zatrzymany. Możesz wznowić przyciskiem Start lub zatwierdzić zwrot.":"Odczyt zatrzymany. Możesz wznowić przyciskiem Start lub zatwierdzić pobranie.";UpdateScreen();}
 async Task Clear(){await Stop();await db.Audit(employee!.Id,"ScanCleared",System.Text.Json.JsonSerializer.Serialize(Rows));await PrepareScan();}
 async Task CancelOperation()
 {
  await Stop();
  await db.Audit(employee!.Id,"Cancelled",System.Text.Json.JsonSerializer.Serialize(new{operation,Rows}));
  Rows.Clear();faulted=false;CancelVisible=false;Changed(nameof(Count));Page="Menu";
  Message="Operacja anulowana. Wybierz kolejną operację.";
 }
 async Task Confirm()
 {
  if(scanning)throw new InvalidOperationException("Zatrzymaj odczyt przyciskiem Stop przed zatwierdzeniem operacji.");
  if(!pendingSave)await Stop();if(faulted)throw new InvalidOperationException("Odczyt przerwany. Anuluj i zeskanuj ponownie.");
  string? selectedExport=null;
  if(Kind is "Inventory" or "Return")
  {
   selectedExport=ChooseExportPath(Kind=="Inventory"?inventoryFile:$"zwrot części {inventoryCreated:yyyy-MM-dd_HH-mm-ss}_{operation.ToString()[..8]}.xlsx");
   if(selectedExport==null){
    // Anulowanie okna wyboru pliku nie może pozostawić operacji zablokowanej.
    // Przy ponowieniu ten sam numer operacji jest bezpieczny dzięki kontroli Id w SQL.
    pendingSave=false;
    Message="Eksport anulowany. Odczyty pozostają na liście — wybierz Zapisz kontrolę, aby ponowić eksport.";
    return;
   }
  }
  pendingSave=true;
  string? reportPath=null;
  try
  {
   if(Kind=="Inventory")
   {
    await db.SaveInventory(operation,employee!,Rows.ToArray());
    OperationExport.Inventory(selectedExport!,operation,inventoryCreated,employee!,Rows.ToArray());reportPath=selectedExport;
   }
   else
   {
    await db.Save(operation,Kind,employee!,SelectedOrder?.Number,Kind=="Return"?SelectedReceipt?.Id:null,Rows.Select(x=>x.EPC).ToArray());
    if(Kind=="Return")OperationExport.Write(selectedExport!,["Id zwrotu","Id pierwotnego pobrania","Data","Pracownik","Kod pracownika","Zlecenie","EPC","Indeks","Nazwa","Ilość"],Rows.Select(r=>new[]{operation.ToString(),SelectedReceipt!.Id.ToString(),inventoryCreated.ToString("yyyy-MM-dd HH:mm:ss"),employee!.Name,employee.Barcode,SelectedReceipt.Order,r.EPC,r.Indeks,r.Nazwa,"1"}));
   }
  }
  catch(InvalidOperationException){pendingSave=false;throw;}
  catch(Exception ex){throw new Exception("Zapis niepotwierdzony. Lista jest zamrożona; ponów Zatwierdź z tym samym numerem operacji. "+ex.Message,ex);}
  pendingSave=false;
  if(reportPath!=null)
  {
   reportConfirmation="Zapisano kontrolę w SQL i raport: "+reportPath;
   Rows.Clear();faulted=false;CancelVisible=false;
   operation=Guid.NewGuid();inventoryCreated=DateTime.Now;inventoryFile=InventoryReport.FileName(operation,inventoryCreated);
   Changed(nameof(Count));Page="Scan";Touch();
   Message="Lista odczytów została wyczyszczona. Przygotuj kolejną kuwetę i naciśnij Start.";
   return;
  }
  var id=operation;var issued=Kind=="Issue";var returned=Kind=="Return";var quantity=Rows.Count;
  if(returned)ReturnConfirmed?.Invoke(quantity);
  if(issued)IssueConfirmed?.Invoke(quantity);
  employee=null;Rows.Clear();History.Clear();Changed(nameof(Logged));Changed(nameof(Identity));Changed(nameof(Count));Page="Login";
  Message=issued?$"Pobrano części: {quantity} szt. Zapisano w SQL. Wylogowano.":returned?$"Zwrócono części: {quantity} szt. Zapisano w SQL. Wylogowano.":$"Zapisano lokalnie: {id.ToString()[..8]}. Wylogowano. SAP nie jest podłączony.";
 }
 async Task End(string reason){await Stop();await db.Audit(employee?.Id,reason,System.Text.Json.JsonSerializer.Serialize(Rows));employee=null;Rows.Clear();History.Clear();Changed(nameof(Logged));Changed(nameof(Identity));Changed(nameof(Count));CancelVisible=false;Page="Login";Message="Sesja zakończona. Przyłóż kartę.";}
 async Task Refresh(){if(Until.Date<From.Date)throw new InvalidOperationException("Data końcowa poprzedza początkową.");var rows=await db.History(From.Date,Until.Date.AddDays(1),employee!.Id);History.Clear();foreach(var row in rows)History.Add(row);Changed(nameof(DisplayHistory));}
 Task Export(){var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Excel (*.xlsx)|*.xlsx|Plik CSV (*.csv)|*.csv",FileName=$"PartsBox_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"};if(dialog.ShowDialog()==true){if(System.IO.Path.GetExtension(dialog.FileName).Equals(".xlsx",StringComparison.OrdinalIgnoreCase)){ExcelReport.Write(dialog.FileName,History);Message="Wyeksportowano raport Excel: "+dialog.FileName;return Task.CompletedTask;}static string Q(string s)=>"\""+((s.StartsWith('=')||s.StartsWith('+')||s.StartsWith('-')||s.StartsWith('@'))?"'":"")+s.Replace("\"","\"\"")+"\"";var lines=new List<string>{"Id;Data;Operacja;Pracownik;Zlecenie;EPC;Indeks;Nazwa;Rozliczenie"};lines.AddRange(History.Select(r=>string.Join(';',new[]{r.Id.ToString(),r.Data.ToString("yyyy-MM-dd HH:mm:ss"),r.Operacja,r.Pracownik,r.Zlecenie,r.EPC,r.Indeks,r.Nazwa,r.Rozliczenie}.Select(Q))));System.IO.File.WriteAllLines(dialog.FileName,lines,new System.Text.UTF8Encoding(true));Message="Wyeksportowano raport CSV: "+dialog.FileName;}return Task.CompletedTask;}
 public Task Preview()=>Run(async()=>{Card="240,12345";await Login();await Start();Add("303400000000000000000002");Add("303400000000000000000003");}); public Task Close()=>ShutdownConnections();
}





