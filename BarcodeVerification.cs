using System.IO;
namespace PartsBox;
static class BarcodeVerification
{
 public static void Run()
 {
  var buffer=new BarcodeKeyboardBuffer();long time=10000;
  static void Check(bool ok){if(!ok)throw new InvalidOperationException("Nieprawidłowe buforowanie skanera EPC.");}
  string? Scan(string code,int end=13){foreach(var c in code)Check(buffer.Feed("scanner",c,time+=10)==null);return buffer.Feed("scanner",end,time+=10);}
  Check(Scan("A0100000000000000001000100000000")=="A0100000000000000001000100000000");
  Check(Scan("A0200000000000000001000100000000",9)=="A0200000000000000001000100000000");
  Check(Scan("MAT10025")=="[nieprawidłowy kod]");Check(Scan(new string('A',129))=="[nieprawidłowy kod]");
  buffer.Feed("a",65,time+=10);buffer.Feed("b",66,time+=10);Check(buffer.Feed("a",13,time+=10)=="A");Check(buffer.Feed("b",13,time+=10)=="B");
  buffer.Feed("a",65,time+=10);Check(buffer.Feed("a",13,time+=2000)==null);
  buffer.Feed("a",65,time+=10);buffer.Clear();Check(buffer.Feed("a",13,time+=10)==null);
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"barcode-test-result.txt"),"PASS: Enter/Tab, pełny EPC, odrzucenie symbolu materiału i przepełnienia, rozdzielenie urządzeń, brak odczytu po przerwie lub utracie fokusu.");
 }
}
