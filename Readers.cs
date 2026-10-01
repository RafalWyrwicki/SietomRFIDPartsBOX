using Symbol.RFID3;

namespace PartsBox;
public interface IReader : IDisposable
{
 event Action<string>? Tag;
 event Action<string>? Fault;
 Task Start(); Task Stop();
}
public sealed class DemoReader : IReader
{
 public event Action<string>? Tag;
 public event Action<string>? Fault {add{} remove{}}
 public Task Start()=>Task.CompletedTask;
 public Task Stop()=>Task.CompletedTask;
 public void Add(string epc)=>Tag?.Invoke(epc);
 public void Dispose(){}
}
public sealed class ZebraReader : IReader
{
 readonly RFIDReader reader;
 public bool IsConnected=>!disposed&&reader.IsConnected;
 public string Model=>reader.ReaderCapabilities.ModelName;
 readonly object gate=new();
 bool reading,disposed;
 public event Action<string>? Tag;
 public event Action<string>? Fault;
 public ZebraReader(string host,uint port)
 {
  if(string.IsNullOrWhiteSpace(host))throw new InvalidOperationException("Ustaw ReaderHost w appsettings.json.");
  reader=new RFIDReader(host,port,0);
 }
 bool subscribed;
 void EnsureConnected()
 {
  if(reader.IsConnected)return;
  reader.Connect();
  if(subscribed)return;
  reader.Events.ReadNotify+=OnRead; reader.Events.StatusNotify+=OnStatus;
  subscribed=true;
  reader.Events.AttachTagDataWithReadEvent=false;
  reader.Events.NotifyReaderDisconnectEvent=true;
  reader.Events.NotifyBufferFullEvent=true;
  reader.Events.NotifyBufferFullWarningEvent=true;
 }
 void OnRead(object sender,Events.ReadEventArgs e) { lock(gate) { try { Drain(); } catch(Exception ex) {reading=false;Fault?.Invoke(ex.Message);} } }
 void Drain() { TagData[]? tags;while((tags=reader.Actions.GetReadTags(1000)) is {Length:>0})foreach(var tag in tags)if(reading)Tag?.Invoke(tag.TagID); }
 void OnStatus(object sender,Events.StatusEventArgs e)
 {
  var status=e.StatusEventData.StatusEventType.ToString();
  if(status.Contains("DISCONNECTION")||status.Contains("BUFFER_FULL")||status.Contains("EXCEPTION")) { lock(gate)reading=false;Fault?.Invoke("Czytnik: "+status+". Powtórz odczyt."); }
 }
 public Task Connect()=>Task.Run(EnsureConnected);
 public Task Start()=>Task.Run(()=>{EnsureConnected();lock(gate){reading=false;Drain();reading=true;}try{reader.Actions.Inventory.Perform();}catch{reading=false;throw;}});
 public Task Stop()=>Task.Run(()=>{if(reader.IsConnected)reader.Actions.Inventory.Stop();lock(gate){Drain();reading=false;}});
 public void Dispose(){if(disposed)return;reading=false;if(subscribed){reader.Events.ReadNotify-=OnRead;reader.Events.StatusNotify-=OnStatus;subscribed=false;}if(reader.IsConnected)reader.Disconnect();else reader.Dispose();disposed=true;}
 public static async Task<ZebraReader> Open(Settings config)
 {
  string[] candidates=config.ReaderAddressMode.ToLowerInvariant() switch
  {
   "host"=>[config.ReaderHost],"ip"=>[config.ReaderIp],"auto"=>[config.ReaderHost,config.ReaderIp],
   _=>throw new InvalidOperationException("ReaderAddressMode: wybierz Auto, Host albo IP.")
  };
  var errors=new List<string>();
  foreach(var target in candidates.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct())
  {
   ZebraReader? device=null;
   try
   {
    var addresses=await System.Net.Dns.GetHostAddressesAsync(target).WaitAsync(TimeSpan.FromSeconds(5));
    var address=addresses.FirstOrDefault(x=>x.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork)??throw new InvalidOperationException("Brak adresu IPv4.");
    device=new ZebraReader(address.ToString(),config.ReaderPort);await device.Connect();return device;
   }
   catch(Exception ex){try{device?.Dispose();}catch{}errors.Add(target+": "+Describe(ex));}
  }
  throw new InvalidOperationException("Nie połączono FX7500. "+string.Join(" | ",errors));
 }
 static string Describe(Exception ex)=>ex is OperationFailureException failure?$"{failure.Result}: {failure.StatusDescription}; {failure.VendorMessage}":ex is InvalidUsageException usage?$"{usage.Info}; {usage.VendorMessage}":ex.Message;
}
