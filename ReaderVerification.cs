using System.IO;

namespace PartsBox;
public static class ReaderVerification
{
 public static async Task Run(Settings settings)
 {
  var lines=new List<string>();
  foreach(var mode in new[]{"Host","IP"})
  {
   settings.ReaderAddressMode=mode;
   using var reader=await ZebraReader.Open(settings);
   var tags=new System.Collections.Concurrent.ConcurrentDictionary<string,byte>();
   string? error=null;reader.Tag+=epc=>tags.TryAdd(epc,0);reader.Fault+=message=>error=message;
   lines.Add($"{mode}: Connected={reader.IsConnected}; Model={reader.Model}");
   try{await reader.Start();await Task.Delay(3000);}finally{await reader.Stop();}
   if(error!=null)throw new Exception(error);
   lines.Add($"{mode}: Start/Stop OK; unique EPC={tags.Count}");
   lines.AddRange(tags.Keys.OrderBy(x=>x));
  }
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"reader-test-result.txt"),lines);
 }
}
