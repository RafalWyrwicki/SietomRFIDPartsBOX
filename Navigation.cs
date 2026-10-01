using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace PartsBox;
public sealed partial class MainViewModel
{
 string page="Login",reportType="Wszystkie";bool cancelVisible;
 string returnSearch="";
 public string ReturnSearch {get=>returnSearch;set{Set(ref returnSearch,value);SelectedReceipt=null;Changed(nameof(FilteredReceipts));}}
 public IEnumerable<Receipt> FilteredReceipts=>Receipts.Where(r=>r.Order.Contains(ReturnSearch.Trim(),StringComparison.OrdinalIgnoreCase));
 public string Page {get=>page;private set{Set(ref page,value);UpdateScreen();}}
 public bool CancelVisible {get=>cancelVisible;set{Set(ref cancelVisible,value);System.Windows.Input.CommandManager.InvalidateRequerySuggested();}}
 public bool LoginPage=>Page=="Login";
 public bool MenuPage=>Page=="Menu";
 public bool OrdersPage=>Page=="Orders";
 public bool ReturnsPage=>Page=="Returns";
 public bool ScanPage=>Page=="Scan";
 public bool InventoryScanPage=>ScanPage&&Kind=="Inventory";
 public bool ReportsPage=>Page=="Reports";
 public string Header=>Page switch {"Login"=>"RFID PartBOX", "Menu"=>Identity,"Orders"=>"Pobranie części | Wybór zlecenia","Returns"=>"Zwrot części | Wybór pobrania","Reports"=>"Raporty",_=>Mode switch{"Zwrot"=>"Zwrot niewykorzystanych części","Inwentaryzacja"=>"Kontrola stanów magazynowych",_=>"Pobranie części"}};
 public string HeaderRight=>employee is null||MenuPage?DateTime.Now.ToString("HH:mm"):employee.Name+(ScanPage&&Kind!="Inventory"?$" | {(Kind=="Return"?SelectedReceipt?.Order:SelectedOrder?.Number)}":"");
 public string ScanTitle=>Mode switch{"Zwrot"=>"Odczyt zwracanych części","Inwentaryzacja"=>"Zliczanie unikalnych EPC w kuwecie",_=>"Pozycje zlecenia i bieżące odczyty RFID"};
 public string ConfirmLabel=>Mode switch{"Zwrot"=>"Zatwierdź zwrot","Inwentaryzacja"=>"Zapisz kontrolę",_=>"Zatwierdź"};
 public string ScanState=>faulted?"Odczyt przerwany — wyczyść i ponów":scanning&&reader is ZebraReader {IsConnected:true}?"Odczyt aktywny | Kuweta SIETOM | FX7500":"Odczyt zatrzymany | Kuweta SIETOM";
 public int TagCount=>Rows.Count;
 public bool ShowScanErrors=>false;
 public GridLength ErrorColumnWidth=>new(ShowScanErrors?185:0);
 public int ErrorCount=>Rows.Count(x=>x.Status!="OK");
 public Brush SqlColor=>Sql.Contains("połączony")&&!Sql.Contains("niepołączony")?Brushes.LightGreen:Brushes.Salmon;
 public Brush RfidColor=>rfidReady?Brushes.LightGreen:Brushes.Salmon;
 public string RfidShort=>rfidReady?(scanning?"FX7500: odczyt":"FX7500: połączony") : "RFID: niepołączony";
 public string CardStatus=>cardReady?"Czytnik kart: podłączony":"Czytnik kart: brak połączenia";
 public string EmployeeDetails=>employee is null?"":string.Join(" • ",new[]{employee.Barcode,employee.Position}.Where(s=>s.Length>0));
 public string[] ReportTypes {get;}=["Wszystkie","Pobranie","Zwrot","Inwentaryzacja"];
 public string ReportType {get=>reportType;set{Set(ref reportType,value);Changed(nameof(DisplayHistory));}}
 public IEnumerable<HistoryRow> DisplayHistory=>History.Where(r=>ReportType=="Wszystkie"||r.Operacja==ReportType);
 public Command IssuePageCommand {get;private set;}=null!;
 public Command SelectOrderCommand {get;private set;}=null!;
 public Command SelectReturnCommand {get;private set;}=null!;
 public Command ReturnPageCommand {get;private set;}=null!;
 public Command InventoryPageCommand {get;private set;}=null!;
 public Command ReportsPageCommand {get;private set;}=null!;
 public Command BackCommand {get;private set;}=null!;
 public Command InventoryBackCommand {get;private set;}=null!;
 public Command DismissCancelCommand {get;private set;}=null!;
 public Command AcceptCancelCommand {get;private set;}=null!;
 public Command DemoLoginCommand {get;private set;}=null!;
 public Command ImportOrdersCommand {get;private set;}=null!;
 public Command ImportStockCommand {get;private set;}=null!;
 public event Action<string>? Notice;
 void InitializeNavigation()
 {
  ImportOrdersCommand=new(()=>Run(async()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Zlecenia Excel (*.xlsx)|*.xlsx",Title="Import zleceń"};if(dialog.ShowDialog()!=true)return;Message=await db.ImportOrders(dialog.FileName,employee!.Id);}),()=>Editing&&MenuPage);
  ImportStockCommand=new(()=>Run(async()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Magazyn CSV (*.csv)|*.csv",Title="Import magazynu"};if(dialog.ShowDialog()!=true)return;Message=await db.ImportStockRows(dialog.FileName,employee!.Id);}),()=>Editing&&MenuPage);
  SelectReturnCommand=new(()=>Run(PrepareScan),()=>Editing&&ReturnsPage&&SelectedReceipt!=null);
  SelectOrderCommand=new(()=>Run(async()=>{if(await db.OrderIsInit(SelectedOrder!.Number)){Message="Zlecenie nieaktywne. Proszę uruchomić przekazanie do wykonania";Notice?.Invoke(Message);return;}await PrepareScan();}),()=>Editing&&OrdersPage&&SelectedOrder!=null);
  IssuePageCommand=new(()=>Run(async()=>{Mode="Pobranie";Orders.Clear();foreach(var order in await db.ImportedOrders())Orders.Add(order);SelectedOrder=Orders.FirstOrDefault();OrderLocation="Wszystkie";Changed(nameof(OrderLocations));Changed(nameof(FilteredOrders));Page="Orders";Message="Wybierz zlecenie z listy.";}),()=>Editing&&MenuPage);
  ReturnPageCommand=new(()=>Run(async()=>{Mode="Zwrot";ReturnSearch="";Receipts.Clear();foreach(var r in await db.Receipts())Receipts.Add(r);SelectedReceipt=null;Changed(nameof(FilteredReceipts));Page="Returns";Message="Wskaż pobranie, do którego zwracasz części.";}),()=>Editing&&MenuPage);
  InventoryPageCommand=new(()=>Run(async()=>{Mode="Inwentaryzacja";await PrepareScan();}),()=>Editing&&MenuPage);
  ReportsPageCommand=new(()=>Run(async()=>{await Refresh();Page="Reports";Message="Raporty operacji zalogowanego pracownika.";}),()=>Editing&&MenuPage);
  BackCommand=new(()=>{Page="Menu";Message="Wybierz operację.";return Task.CompletedTask;},()=>Editing&&!ScanPage);
  InventoryBackCommand=new(()=>Run(()=>{reportConfirmation="";Page="Menu";Message="Wybierz kolejną operację.";return Task.CompletedTask;}),()=>Logged&&InventoryScanPage&&!busy&&!scanning&&!pendingSave&&!CancelVisible&&Rows.Count==0);
  DismissCancelCommand=new(()=>{CancelVisible=false;return Task.CompletedTask;});
  AcceptCancelCommand=new(()=>Run(CancelOperation),()=>Logged&&!busy&&!pendingSave);
  DemoLoginCommand=new(()=>Run(async()=>{Card="31533344";await Login();}),()=>Demo&&!Logged&&!busy);
  var clock=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};clock.Tick+=(_,_)=>Changed(nameof(HeaderRight));timers.Add(clock);clock.Start();
 }
 void UpdateScreen()
 {
  Changed(nameof(InventoryScanPage));
  foreach(var p in new[]{nameof(LoginPage),nameof(MenuPage),nameof(OrdersPage),nameof(ReturnsPage),nameof(ScanPage),nameof(ReportsPage),nameof(Header),nameof(HeaderRight),nameof(ScanTitle),nameof(ConfirmLabel),nameof(ScanState),nameof(TagCount),nameof(ErrorCount),nameof(ReportConfirmation),nameof(HasReportConfirmation),nameof(ShowScanErrors),nameof(ErrorColumnWidth),nameof(SqlColor),nameof(RfidColor),nameof(RfidShort),nameof(EmployeeDetails),nameof(Ready),nameof(ConnectionProblem),nameof(ConnectionMessage),nameof(CardStatus),nameof(CardColor),nameof(SapStatus),nameof(SapColor)})Changed(p);
 }
 // Exercises actual navigation and rendering against the development database, without confirming stock movements.
 public async Task VerifyReaderView(Func<string,Task> capture)
 {
  Assert(!Demo,"Test wymaga fizycznego czytnika.");
  try
  {
   await Boot();Card="31533344";await Invoke(LoginCommand);Assert(MenuPage,"Nie zalogowano");
   await Invoke(InventoryPageCommand);Assert(ScanPage&&!scanning&&StartCommand.CanExecute(null)&&!StopCommand.CanExecute(null),"Nieprawidłowy stan początkowy");
   await capture("12-kontrola-gotowa");
   await Invoke(StartCommand);Assert(scanning&&!faulted&&ScanState.Contains("FX7500")&&StopCommand.CanExecute(null),"Nie uruchomiono FX7500: "+Message);
   await Task.Delay(3000);await capture("13-kontrola-FX7500");
   await Invoke(StopCommand);var count=Rows.Count;Assert(!scanning&&!faulted&&StartCommand.CanExecute(null),"Nie zatrzymano FX7500: "+Message);
   await capture("14-kontrola-zatrzymana");
   await Invoke(StartCommand);Assert(scanning&&Rows.Count>=count,"Wznowienie skasowało listę");await Task.Delay(1000);await Invoke(StopCommand);
   System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"reader-view-test-result.txt"),$"PASS: logowanie, kontrola początkowo zatrzymana, przyciski Start/Stop, status FX7500, wznowienie. EPC={Rows.Count}. {DateTime.Now}");
   if(Rows.Count>0){await Invoke(ConfirmCommand);Assert(ScanPage&&Logged&&Rows.Count==0&&HasReportConfirmation,"Nie zapisano kontroli: "+Message);System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"reader-view-test-result.txt"),"\n"+Message);}
   else {await Invoke(ClearCommand);Assert(Rows.Count==0&&!scanning,"Wyczyść nie przywróciło stanu gotowości");}
  }
  finally{await Close();System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"reader-view-test-result.txt"),"\nPołączenie zamknięte.");}
 }
 public async Task VerifyViews(Func<string,Task> capture)
 {
  if(!Demo)throw new InvalidOperationException("Podgląd wymaga trybu demonstracyjnego.");
  await Boot();UpdateScreen();await capture("01-logowanie");
  await Run(async()=>{Card="31533344";await Login();});Assert(MenuPage,"Logowanie nie otworzyło menu");await capture("02-menu");
  await Run(()=>End("CardVerification"));await Run(async()=>{Card="31534052";await Login();});Assert(employee?.Name=="Wywrot Julia","Nieprawidłowy pracownik drugiej karty");await capture("11-druga-karta");await Run(()=>End("CardVerification"));await Invoke(DemoLoginCommand);
  await Invoke(IssuePageCommand);Assert(OrdersPage,"Kafel pobrania nie otworzył zleceń");await Invoke(BackCommand);Assert(MenuPage,"Wróć nie otworzył menu");await Invoke(IssuePageCommand);await capture("03-zlecenia");
  await Invoke(SelectOrderCommand);Assert(ScanPage&&!scanning,"Wybór zlecenia uruchomił odczyt");await Invoke(StartCommand);Assert(scanning,"Nie rozpoczęto odczytu");
  var available=parts.Values.Where(p=>p.State=="Available"&&p.Epc.All(Uri.IsHexDigit)).Take(2).ToArray();foreach(var p in available)Add(p.Epc);
  if(available.Length>0){var n=Rows.Count;Add(available[0].Epc);Assert(Rows.Count==n,"Powielony odczyt EPC");}
  Add("FFFFFFFFFFFF");Assert(!ConfirmCommand.CanExecute(null),"Nieznany EPC nie blokuje zatwierdzenia");await capture("04-pobranie");
  await Invoke(CancelCommand);Assert(CancelVisible,"Brak dialogu anulowania");await capture("05-anulowanie");await Invoke(DismissCancelCommand);Assert(ScanPage&&!CancelVisible,"Powrót z dialogu zmienił ekran");
  await Invoke(ClearCommand);Assert(Rows.Count==0,"Czyść nie opróżniło listy");
  await Invoke(CancelCommand);await Invoke(AcceptCancelCommand);Assert(MenuPage&&Logged&&!scanning&&Rows.Count==0,"Anulowanie nie przywróciło menu");
  await Invoke(ReturnPageCommand);Assert(ReturnsPage,"Brak wyboru pobrania");await capture("06-wybor-pobrania");
  SelectedReceipt=Receipts.FirstOrDefault();if(SelectedReceipt!=null){await Run(Start);await capture("07-zwrot");await Run(()=>End("PreviewCancelled"));await Run(async()=>{Card="240,12345";await Login();});}
  await Invoke(InventoryPageCommand);foreach(var p in parts.Values.Where(p=>p.Epc.All(Uri.IsHexDigit)).Take(3))Add(p.Epc);Assert(!ShowScanErrors&&TagCount==Rows.Count,"Licznik kontroli");await capture("08-inwentaryzacja");await Invoke(CancelCommand);await Invoke(AcceptCancelCommand);Assert(MenuPage&&Logged&&!scanning&&Rows.Count==0,"Anulowanie kontroli nie wraca do menu");await capture("19-menu-po-anulowaniu");await Run(()=>End("PreviewCancelled"));
  await Invoke(DemoLoginCommand);await Invoke(ReportsPageCommand);Assert(ReportsPage,"Brak raportów");await capture("09-raporty");ReportType="Zwrot";Assert(DisplayHistory.All(r=>r.Operacja=="Zwrot"),"Filtr raportu");ReportType="Wszystkie";
  await Run(()=>End("PreviewFinished"));
 }
 static void Assert(bool condition,string text){if(!condition)throw new InvalidOperationException(text);}
 async Task Invoke(Command command){Assert(command.CanExecute(null),"Polecenie nawigacji jest niedostępne");command.Execute(null);while(busy)await Task.Delay(10);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);}
 Task ExportFiltered()
 {
  var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",FileName=$"PartsBox_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"};
  if(dialog.ShowDialog()!=true)return Task.CompletedTask;
  var rows=DisplayHistory.ToArray();
  if(System.IO.Path.GetExtension(dialog.FileName).Equals(".xlsx",StringComparison.OrdinalIgnoreCase))ExcelReport.Write(dialog.FileName,rows);
  else
  {
   static string Q(string s)=>"\""+(s.Length>0&&"=+-@".Contains(s[0])?"'":"")+s.Replace("\"","\"\"")+"\"";
   var lines=new List<string>{"Id;Data;Operacja;Pracownik;Zlecenie;EPC;Indeks;Nazwa;Rozliczenie"};
   lines.AddRange(rows.Select(r=>string.Join(';',new[]{r.Id.ToString(),r.Data.ToString("yyyy-MM-dd HH:mm:ss"),r.Operacja,r.Pracownik,r.Zlecenie,r.EPC,r.Indeks,r.Nazwa,r.Rozliczenie}.Select(Q))));
   System.IO.File.WriteAllLines(dialog.FileName,lines,new System.Text.UTF8Encoding(true));
  }
  Message=$"Wyeksportowano {rows.Length} pozycji zgodnie z filtrami.";return Task.CompletedTask;
 }
}






