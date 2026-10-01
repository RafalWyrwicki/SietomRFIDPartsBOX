using System.IO;
using System.Text.Json;
using System.Windows;
namespace PartsBox;
public partial class App : Application
{
 protected override async void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);
  try
  {
   if(e.Args.Contains("--file-import-test")){await FileImportVerification.Run();Shutdown();return;}
   if(e.Args.Contains("--barcode-test")){BarcodeVerification.Run();Shutdown();return;}
   if(e.Args.Contains("--confirmation-preview"))
   {
    ShutdownMode=ShutdownMode.OnExplicitShutdown;
    foreach(var returned in new[]{false,true})
    {
     var dialog=new OperationConfirmationWindow(returned,2);
     dialog.Loaded+=async(_,_)=>
     {
      await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
      dialog.UpdateLayout();
      var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(dialog);
      var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
      var folder=Path.Combine(AppContext.BaseDirectory,"Widoki");Directory.CreateDirectory(folder);
      using(var file=File.Create(Path.Combine(folder,returned?"potwierdzenie-zwrotu.png":"potwierdzenie-pobrania.png")))png.Save(file);
      dialog.Close();
     };
     dialog.ShowDialog();
    }
    Shutdown();return;
   }
   var config=JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"appsettings.json")))??throw new InvalidOperationException("Brak konfiguracji.");
   if(e.Args.Contains("--local-verification")&&e.Args.Any(a=>a is "--inventory-save-test" or "--issue-flow-test" or "--preview"))
    config.ConnectionString=@"Server=.\SQLEXPRESS;Database=SietomPartsBox_Development;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5";
   if(e.Args.Contains("--self-test")){await SelfTest.Run(config);Shutdown();return;}
   if(e.Args.Contains("--card-test")){await SelfTest.Cards(config);Shutdown();return;}
   if(e.Args.Contains("--reader-test")){await ReaderVerification.Run(config);Shutdown();return;}
   if(e.Args.Contains("--inventory-test")){await InventoryVerification.Run(config);Shutdown();return;}
   if(e.Args.Contains("--inventory-save-test")){config.DemoMode=true;await new MainViewModel(config).VerifyInventorySave();Shutdown();return;}
   if(e.Args.Contains("--issue-flow-test")){config.DemoMode=true;await new MainViewModel(config).VerifyIssueFlow();Shutdown();return;}
   if(e.Args.Contains("--preview"))config.DemoMode=true;
   var vm=new MainViewModel(config);
   var hardwarePreview=e.Args.Contains("--reader-preview");
   var connectionsPreview=e.Args.Contains("--connections-preview");
   var preview=e.Args.Contains("--preview")||hardwarePreview||connectionsPreview;
   var window=new MainWindow(vm,!preview);MainWindow=window;
   if(preview)window.Loaded+=async(_,_)=>
   {
    try
    {
     var folder=Path.Combine(AppContext.BaseDirectory,"Widoki");Directory.CreateDirectory(folder);
     if(!hardwarePreview&&!connectionsPreview)await window.VerifyKeyboardCards(vm);
     Func<string,Task> capture=async name=>
     {
      await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);window.UpdateLayout();
      var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);
      var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(folder,name+".png"));encoder.Save(output);
      if(name=="04-pobranie")
      {
       window.Width=1024;window.Height=700;await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);window.UpdateLayout();
       var small=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);small.Render(window);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(small));using(var f=File.Create(Path.Combine(folder,"10-maly-ekran.png")))png.Save(f);window.Width=1280;window.Height=800;
      }
     };
     if(connectionsPreview)await vm.VerifyConnections(capture);else if(hardwarePreview)await vm.VerifyReaderView(capture);else await vm.VerifyViews(capture);
     if(!hardwarePreview&&!connectionsPreview)File.WriteAllText(Path.Combine(folder,"wynik-testu.txt"),"PASS: logowanie, menu, wybór zlecenia, odczyt, blokada nieznanego EPC, deduplikacja, dialog anulowania, czyszczenie, wylogowanie, wybór pobrania, zwrot, inwentaryzacja, raporty i filtr typu. "+DateTime.Now);
     window.Close();
    }
    catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"preview-error.txt"),ex.ToString());Shutdown(1);}
   };
   window.Show();
  }
  catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"startup-error.txt"),ex.ToString());if(!e.Args.Any(a=>a.EndsWith("-test")||a=="--preview"))MessageBox.Show(ex.Message,"PartsBOX");Shutdown(1);}
 }
}

