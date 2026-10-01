using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualBasic.FileIO;

namespace PartsBox;
public record ImportedOrder(DateTime Date,string Number,string Status,string Equipment,string Description,string EquipmentDescription,string LocationDescription,string Location);
public record ImportedStock(int Row,string Material,string Description,string Unit,string InventoryNumber,string StorageLocation,string Plant,string Epc,string Barcode,decimal Quantity);
public static class ImportFiles
{
 static string Normalize(string s)=>Regex.Replace(s.Replace('\u00a0',' ').Trim(),@"\s+"," ").ToUpperInvariant();
 static string Limit(string s,int length,int row){s=s.Trim();if(s.Length>length)throw new InvalidDataException($"Wiersz {row}: pole przekracza {length} znaków.");return s;}
 public static List<ImportedOrder> Orders(string path)
 {
  using var zip=ZipFile.OpenRead(path);
  XDocument Xml(string name)=>XDocument.Load(zip.GetEntry(name)?.Open()??throw new InvalidDataException("Brak elementu XLSX: "+name));
  XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
  var shared=zip.GetEntry("xl/sharedStrings.xml") is null?[]:Xml("xl/sharedStrings.xml").Descendants(ns+"si").Select(e=>string.Concat(e.Descendants(ns+"t").Select(x=>x.Value))).ToArray();
  var workbook=Xml("xl/workbook.xml");var sheet=workbook.Descendants(ns+"sheet").First();
  var id=(string?)sheet.Attribute(XName.Get("id","http://schemas.openxmlformats.org/officeDocument/2006/relationships"));
  var target=(string?)Xml("xl/_rels/workbook.xml.rels").Descendants().First(e=>(string?)e.Attribute("Id")==id).Attribute("Target")??throw new InvalidDataException("Brak arkusza.");
  var entry=target.StartsWith('/')?target.TrimStart('/'):"xl/"+target;
  var rows=new List<(int Number,Dictionary<int,string> Cells)>();
  foreach(var row in Xml(entry).Descendants(ns+"row"))
  {
   var cells=new Dictionary<int,string>();
   foreach(var c in row.Elements(ns+"c"))
   {
    var reference=(string?)c.Attribute("r")??"";int col=0;foreach(var ch in reference.TakeWhile(char.IsLetter))col=col*26+char.ToUpperInvariant(ch)-'A'+1;
    var value=(string?)c.Element(ns+"v")??"";var type=(string?)c.Attribute("t");
    if(type=="s")value=shared[int.Parse(value,CultureInfo.InvariantCulture)];
    if(type=="inlineStr")value=string.Concat(c.Descendants(ns+"t").Select(t=>t.Value));
    if(type=="e")throw new InvalidDataException("Błąd komórki XLSX: "+reference);
    cells[col]=value.Trim();
   }
   rows.Add(((int?)row.Attribute("r")??rows.Count+1,cells));
  }
  string[] headers=["Data wprowadzenia","Zlecenie","Status użytkownika","Urządzenie","Krótki tekst","Oznaczenie obiektu technicznego","Oznaczenie lokalizacji funkcjonalnej","Lokalizacja funkcjonalna"];
  var header=rows.Take(20).FirstOrDefault(r=>headers.All(h=>r.Cells.Values.Any(v=>Normalize(v)==Normalize(h))));
  if(header.Cells==null)throw new InvalidDataException("Nie znaleziono wymaganych 8 nagłówków zleceń.");
  var columns=headers.Select(h=>header.Cells.Single(c=>Normalize(c.Value)==Normalize(h)).Key).ToArray();
  var result=new List<ImportedOrder>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  bool date1904=(string?)workbook.Descendants(ns+"workbookPr").FirstOrDefault()?.Attribute("date1904") is "1" or "true";
  foreach(var row in rows.Where(r=>r.Number>header.Number))
  {
   var values=columns.Select(c=>row.Cells.GetValueOrDefault(c,"")).ToArray();if(values.All(string.IsNullOrWhiteSpace))continue;
   string V(int i,int max)=>Limit(values[i],max,row.Number);
   var number=V(1,50);if(number.Length==0||!seen.Add(number))throw new InvalidDataException($"Wiersz {row.Number}: brak numeru lub powtórzone zlecenie {number}.");
   DateTime date;if(double.TryParse(values[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var serial))date=DateTime.FromOADate(serial+(date1904?1462:0));
   else if(!DateTime.TryParse(values[0],CultureInfo.GetCultureInfo("pl-PL"),DateTimeStyles.None,out date))throw new InvalidDataException($"Wiersz {row.Number}: nieprawidłowa data.");
   result.Add(new(date.Date,number,V(2,120),V(3,80),V(4,1000),V(5,500),V(6,500),V(7,150)));
  }
  if(result.Count==0)throw new InvalidDataException("Plik nie zawiera zleceń.");return result;
 }
 public static List<ImportedStock> Stocks(string path)
 {
  Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);var bytes=File.ReadAllBytes(path);string text;
  try{text=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');}catch(DecoderFallbackException){text=Encoding.GetEncoding(1250).GetString(bytes);}
  using var parser=new TextFieldParser(new StringReader(text)){HasFieldsEnclosedInQuotes=true};parser.SetDelimiters(";");
  var header=parser.ReadFields()??throw new InvalidDataException("Pusty CSV.");
  string[] names=["Numer składnika","Opis","SN","Numer inwentarzowy","Lokalizacja","Miasto","epc","kod kreskowy","ilość"];
  var columns=names.Select(n=>Array.FindIndex(header,h=>Normalize(h)==Normalize(n))).ToArray();if(columns.Any(i=>i<0))throw new InvalidDataException("Brak wymaganych nagłówków CSV.");
  var result=new List<ImportedStock>();
  while(!parser.EndOfData)
  {
   var line=(int)parser.LineNumber;var cells=parser.ReadFields()!;if(cells.All(string.IsNullOrWhiteSpace))continue;
   if(cells.Length!=header.Length)throw new InvalidDataException($"Wiersz {line}: niezgodna liczba kolumn.");
   string V(int i,int max)=>Limit(cells[columns[i]],max,line);
   var epc=V(6,128).ToUpperInvariant();if(epc.Length==0||epc.Length%2!=0||!epc.All(Uri.IsHexDigit))throw new InvalidDataException($"Wiersz {line}: nieprawidłowy EPC.");
   var quantity=V(8,40).Replace(" ","").Replace("\u00a0","").Replace(',','.');
   if(!decimal.TryParse(quantity,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var qty)||qty<0||decimal.Round(qty,3)!=qty)throw new InvalidDataException($"Wiersz {line}: nieprawidłowa ilość.");
   if(V(0,80).Length==0||V(4,80).Length==0||V(5,80).Length==0)throw new InvalidDataException($"Wiersz {line}: brak indeksu, lokalizacji lub miasta.");
   result.Add(new(line,V(0,80),V(1,1000),V(2,20),V(3,80),V(4,80),V(5,80),epc,V(7,128),qty));
  }
  if(result.Count==0)throw new InvalidDataException("Plik nie zawiera części.");return result;
 }
}
