using System.IO;
using System.Text;

namespace PartsBox;
public static class InventoryReport
{
 public static string DirectoryPath
 {
  get
  {
   // F5 and the published application share the project's report directory.
   // A standalone installation without sources uses the directory beside its EXE.
   var current=new DirectoryInfo(AppContext.BaseDirectory);
   for(var depth=0;current!=null&&depth<5;depth++,current=current.Parent)
    if(File.Exists(Path.Combine(current.FullName,"PartsBox.csproj")))return Path.Combine(current.FullName,"raporty");
   return Path.Combine(AppContext.BaseDirectory,"raporty");
  }
 }
 public static string FileName(Guid id,DateTime created)=>$"kontrola stanów magazynowych {created:yyyy-MM-dd_HH-mm-ss}_{id.ToString()[..8]}.csv";
 public static string Write(string fileName,Guid id,DateTime created,Employee employee,IEnumerable<ScanRow> rows)
 {
  var folder=DirectoryPath;Directory.CreateDirectory(folder);
  var path=Path.Combine(folder,fileName);
  static string Q(string s)=>"\""+(s.Length>0&&"=+-@".Contains(s[0])?"'":"")+s.Replace("\"","\"\"")+"\"";
  var lines=new List<string>{"Id kontroli;Data;Pracownik;Kod pracownika;EPC;Indeks;Nazwa;Ilosc;Status"};
  lines.AddRange(rows.Select(r=>string.Join(';',new[]{id.ToString(),created.ToString("yyyy-MM-dd HH:mm:ss"),employee.Name,employee.Barcode,r.EPC,r.Indeks,r.Nazwa,"1",r.Status}.Select(Q))));
  var content=string.Join("\r\n",lines)+"\r\n";
  if(File.Exists(path)){if(File.ReadAllText(path)!=content)throw new IOException("Plik raportu istnieje z inną zawartością.");return path;}
  var temporary=path+".tmp";
  File.WriteAllText(temporary,content,new UTF8Encoding(true));File.Move(temporary,path);return path;
 }
}
