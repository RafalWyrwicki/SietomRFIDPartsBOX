using System.Windows;
namespace PartsBox;
public partial class MainWindow : Window
{
 bool closing,closeInProgress;
 readonly System.Windows.Media.TranslateTransform NumericKeyboardTransform=new();
 bool draggingKeyboard; System.Windows.Point keyboardStart; double keyboardX,keyboardY; System.Windows.Controls.Border? draggingElement; readonly Dictionary<System.Windows.Controls.Border,System.Windows.Media.TranslateTransform> keyboardTransforms=[];
 public MainWindow(MainViewModel vm,bool autoBoot=true)
 {
  InitializeComponent();NumericKeyboard.RenderTransform=NumericKeyboardTransform;DataContext=vm;
  vm.Notice+=message=>new OperationConfirmationWindow(message){Owner=this}.ShowDialog();
  vm.IssueConfirmed+=quantity=>new OperationConfirmationWindow(false,quantity){Owner=this}.ShowDialog();
  vm.ReturnConfirmed+=quantity=>new OperationConfirmationWindow(true,quantity){Owner=this}.ShowDialog();
  var cardPause=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(350)};
  string cardBuffer="";long lastKey=0;
  void SubmitCard(){cardPause.Stop();var number=cardBuffer;cardBuffer="";var valid=number.Length>=6&&number.Length<=40&&System.Text.RegularExpressions.Regex.IsMatch(number,@"^\d+,\d+$");if(vm.LoginPage&&valid&&vm.LoginCommand.CanExecute(null)){vm.Card=number;vm.LoginCommand.Execute(null);}}
  void ReaderKey(int key)
  {
   if(!vm.LoginPage||!IsActive){cardPause.Stop();cardBuffer="";return;}
   var now=Environment.TickCount64;if(now-lastKey>350)cardBuffer="";lastKey=now;
   if(key==13){SubmitCard();return;}
   if(key is 188 or 190){cardBuffer+=",";cardPause.Stop();cardPause.Start();return;}
   if(key is >=48 and <=57){cardBuffer+=(char)key;cardPause.Stop();cardPause.Start();}
   else if(key is >=96 and <=105){cardBuffer+=(char)('0'+key-96);cardPause.Stop();cardPause.Start();}
   else {cardBuffer="";cardPause.Stop();}
  }
  cardPause.Tick+=(_,_)=>SubmitCard();
  CardKeyboardInput? cardReader=null;
  var barcodeBuffer=new BarcodeKeyboardBuffer();
  void BarcodeKey(int key,string device)
  {
   if(!IsActive||!vm.CanReceiveBarcode){barcodeBuffer.Clear();return;}
   var value=barcodeBuffer.Feed(device,key,Environment.TickCount64);
   if(value!=null)vm.ReceiveBarcode(value);
  }
  SourceInitialized+=(_,_)=>cardReader=new CardKeyboardInput(new System.Windows.Interop.WindowInteropHelper(this).Handle,vm.CardReaderIdentifier,ReaderKey,BarcodeKey);
  Deactivated+=(_,_)=>barcodeBuffer.Clear();
  vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(vm.Page)||e.PropertyName==nameof(vm.CancelVisible))barcodeBuffer.Clear();};
  PreviewKeyDown+=(_,e)=>{if(vm.ScanPage&&!vm.CancelVisible&&e.Key is not (System.Windows.Input.Key.Escape))e.Handled=true;};
  Closed+=(_,_)=>cardReader?.Dispose();
  Deactivated+=(_,_)=>{cardPause.Stop();cardBuffer="";};
  vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(vm.Page)){cardPause.Stop();cardBuffer="";}};
  PreviewKeyDown+=(_,e)=>{if(vm.LoginPage&&e.Key is not (System.Windows.Input.Key.Tab or System.Windows.Input.Key.Escape))e.Handled=true;};
  vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(vm.CancelVisible)&&vm.CancelVisible)Dispatcher.BeginInvoke(()=>FocusCancel(this,vm));};
  PreviewKeyDown+=(_,_)=>vm.Touch();PreviewMouseDown+=(_,_)=>vm.Touch();
  if(autoBoot)Loaded+=async(_,_)=>await vm.Boot();
  Closing+=async(_,e)=>{if(closing)return;e.Cancel=true;if(closeInProgress)return;closeInProgress=true;IsEnabled=false;cardPause.Stop();try{await System.Windows.Threading.Dispatcher.Yield();await vm.Close();closing=true;Close();}catch(Exception ex){closeInProgress=false;IsEnabled=true;MessageBox.Show("Nie udało się bezpiecznie zakończyć sesji: "+ex.Message);}};
  Closed+=(_,_)=>cardPause.Stop();
 }
 void CloseButton_Click(object sender,RoutedEventArgs e)=>Close();
 void OrderSearch_GotFocus(object sender,RoutedEventArgs e){if(DataContext is MainViewModel vm)vm.ShowNumericKeyboard();}
 void ReturnSearch_GotFocus(object sender,RoutedEventArgs e){if(DataContext is MainViewModel vm)vm.ShowNumericKeyboard(true);}
 void NumericKey_Click(object sender,RoutedEventArgs e){if(DataContext is MainViewModel vm&&sender is System.Windows.Controls.Button b)vm.NumericKey(b.Content?.ToString()??"");}
 void NumericKeyboard_Close(object sender,RoutedEventArgs e){if(DataContext is MainViewModel vm)vm.HideNumericKeyboard();}
 void NumericKeyboard_MouseDown(object sender,System.Windows.Input.MouseButtonEventArgs e){if(e.OriginalSource is System.Windows.Controls.Button||sender is not System.Windows.Controls.Border b)return;draggingKeyboard=true;draggingElement=b;keyboardStart=e.GetPosition(this);if(!keyboardTransforms.TryGetValue(b,out var transform)){transform=new();keyboardTransforms[b]=transform;b.RenderTransform=transform;}keyboardX=transform.X;keyboardY=transform.Y;b.CaptureMouse();e.Handled=true;}
 void NumericKeyboard_MouseMove(object sender,System.Windows.Input.MouseEventArgs e){if(!draggingKeyboard||draggingElement is null)return;var p=e.GetPosition(this);keyboardX+=p.X-keyboardStart.X;keyboardY+=p.Y-keyboardStart.Y;keyboardStart=p;var transform=keyboardTransforms[draggingElement];transform.X=keyboardX;transform.Y=keyboardY;}
 void NumericKeyboard_MouseUp(object sender,System.Windows.Input.MouseButtonEventArgs e){draggingKeyboard=false;draggingElement?.ReleaseMouseCapture();draggingElement=null;}
 static bool FocusCancel(DependencyObject root,MainViewModel vm)
 {
  for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)
  {
   var child=System.Windows.Media.VisualTreeHelper.GetChild(root,i);
   if(child is System.Windows.Controls.Button b&&ReferenceEquals(b.Command,vm.DismissCancelCommand))
   {
    DependencyObject? parent=b;
    while(parent!=null){if(parent is System.Windows.Controls.Grid g&&System.Windows.Controls.Grid.GetRowSpan(g)==4){System.Windows.Input.KeyboardNavigation.SetTabNavigation(g,System.Windows.Input.KeyboardNavigationMode.Cycle);break;}parent=System.Windows.Media.VisualTreeHelper.GetParent(parent);}
    b.Focus();return true;
   }
   if(FocusCancel(child,vm))return true;
  }
  return false;
 }
 public async Task VerifyKeyboardCards(MainViewModel vm)
 {
  await vm.Boot();
  foreach(var (number,name) in new[]{("31533344","Jabłoński Artur"),("31534052","Wywrot Julia")})
  {
   // UI has no editable card field; exercise login mapping independently of USB hardware.
   var readyUntil=DateTime.UtcNow.AddSeconds(5);while(!vm.LoginCommand.CanExecute(null)&&DateTime.UtcNow<readyUntil)await Task.Delay(25);
   vm.Card=number;vm.LoginCommand.Execute(null);
   var until=DateTime.UtcNow.AddSeconds(5);
   while(!(vm.MenuPage&&vm.Editing)&&DateTime.UtcNow<until)await Task.Delay(25);
   if(!vm.MenuPage||!vm.Identity.Contains(name))throw new InvalidOperationException("Nieudane logowanie wejściem klawiaturowym: "+number);
   vm.LogoutCommand.Execute(null);
   until=DateTime.UtcNow.AddSeconds(5);while(!vm.LoginPage&&DateTime.UtcNow<until)await Task.Delay(25);
   if(!vm.LoginPage)throw new InvalidOperationException("Nieudane wylogowanie w teście kart.");
  }
  System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"keyboard-test-result.txt"),"PASS: mapowanie dwóch numerów kart na pracowników i wylogowanie. Test programowy nie zastępuje próby fizycznego czytnika USB. "+DateTime.Now);
 }
}


