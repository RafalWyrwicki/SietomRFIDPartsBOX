using System.IO;
using System.Text;
namespace PartsBox;
public static class OperationExport
{
 public static string? ChoosePath(string name)
 {
  var dialog=new Microsoft.Win32.SaveFileDialog{Title="Eksport Excel / CSV",Filter="Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",FileName=Path.ChangeExtension(name,".xlsx"),AddExtension=true,DefaultExt=".xlsx",OverwritePrompt=true};
  return dialog.ShowDialog()==true?dialog.FileName:null;
 }
 public static void Write(string path,IEnumerable<string> headers,IEnumerable<IEnumerable<string>> rows)
 {
  var temp=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"."+Guid.NewGuid().ToString("N")+".tmp");
  try
  {
   if(Path.GetExtension(path).Equals(".xlsx",StringComparison.OrdinalIgnoreCase))ExcelReport.WriteRows(temp,headers,rows);
   else
   {
    static string Q(string s)=>"\""+(s.Length>0&&"=+-@".Contains(s[0])?"'":"")+s.Replace("\"","\"\"")+"\"";
    File.WriteAllLines(temp,new[]{headers}.Concat(rows).Select(r=>string.Join(';',r.Select(Q))),new UTF8Encoding(true));
   }
   File.Move(temp,path,true);
  }
  finally{if(File.Exists(temp))File.Delete(temp);}
 }
 public static void Inventory(string path,Guid id,DateTime created,Employee employee,IEnumerable<ScanRow> rows)
 =>Write(path,["Id kontroli","Data","Pracownik","Kod pracownika","EPC","Indeks","Nazwa","Ilosc","Status"],rows.Select(r=>new[]{id.ToString(),created.ToString("yyyy-MM-dd HH:mm:ss"),employee.Name,employee.Barcode,r.EPC,r.Indeks,r.Nazwa,"1",r.Status}));
}
