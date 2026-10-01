using System.IO;
using System.IO.Compression;
using System.Xml;

namespace PartsBox;
public static class ExcelReport
{
 public static void Write(string path,IEnumerable<HistoryRow> rows)
 =>WriteRows(path,["Id operacji","Data lokalna","Operacja","Pracownik","Zlecenie","EPC","Indeks","Materiał","Rozliczenie"],rows.Select(r=>new[]{r.Id.ToString(),r.Data.ToString("yyyy-MM-dd HH:mm:ss"),r.Operacja,r.Pracownik,r.Zlecenie,r.EPC,r.Indeks,r.Nazwa,r.Rozliczenie}));
 public static void WriteRows(string path,IEnumerable<string> headers,IEnumerable<IEnumerable<string>> rows)
 {
  using var file=File.Create(path);using var zip=new ZipArchive(file,ZipArchiveMode.Create);
  void Entry(string name,string xml){using var w=new StreamWriter(zip.CreateEntry(name).Open());w.Write(xml);}
  Entry("[Content_Types].xml","""<?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""");
  Entry("_rels/.rels","""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
  Entry("xl/workbook.xml","""<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Operacje" sheetId="1" r:id="rId1"/></sheets></workbook>""");
  Entry("xl/_rels/workbook.xml.rels","""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
  const string ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
  using var stream=zip.CreateEntry("xl/worksheets/sheet1.xml").Open();using var x=XmlWriter.Create(stream,new(){Encoding=new System.Text.UTF8Encoding(false)});
  x.WriteStartElement("worksheet",ns);x.WriteStartElement("sheetData",ns);
  void Row(IEnumerable<string> values){x.WriteStartElement("row",ns);foreach(var v in values){x.WriteStartElement("c",ns);x.WriteAttributeString("t","inlineStr");x.WriteStartElement("is",ns);x.WriteElementString("t",ns,v);x.WriteEndElement();x.WriteEndElement();}x.WriteEndElement();}
  Row(headers);
  foreach(var r in rows)Row(r);
  x.WriteEndElement();x.WriteEndElement();
 }
}
