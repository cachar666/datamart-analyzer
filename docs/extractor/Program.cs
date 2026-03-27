using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;
using System.Linq;

var docPath = @"C:\Proyectos\AA\datamart-analyzer\docs\Descriptivo SINCO ADPRO 2026.docx";
var sb = new StringBuilder();
using var doc = WordprocessingDocument.Open(docPath, isEditable: false);
var body = doc.MainDocumentPart?.Document?.Body;
if (body != null)
{
    foreach (var el in body.Elements())
    {
        if (el is Paragraph p) { var t = p.InnerText.Trim(); if (t.Length > 0) sb.AppendLine(t); }
        else if (el is Table tbl) {
            foreach (var row in tbl.Elements<TableRow>()) {
                var cells = row.Elements<TableCell>().Select(c => c.InnerText.Trim()).Where(c => c.Length > 0);
                sb.AppendLine(string.Join(" | ", cells));
            }
        }
    }
}
System.Console.Write(sb.ToString());
