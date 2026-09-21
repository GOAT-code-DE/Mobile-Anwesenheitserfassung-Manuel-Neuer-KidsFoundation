using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace NeuerKids.Services;

// Small, dependency-free OOXML writer. User-supplied text is always an inline string,
// never a formula, and the input is exactly the report used by the dashboard.
public static class ExcelExport
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static byte[] Create(DashboardReport report)
    {
        var summary = new List<object?[]> { new object?[] { "NEUER KIDS · Auswertung" } };
        summary.AddRange(report.FilterLabels.Select(l => new object?[] { l }));
        summary.Add(["Kennzahl", "Auswahl", "Vergleich"]);
        summary.Add(["Besuche", Value(report.Current.Metrics.Visits), Value(report.Comparison?.Metrics.Visits)]);
        summary.Add([report.CrossSite ? "Unterschiedliche Kinder (Standortzählungen)" : "Unterschiedliche Kinder", Value(report.Current.Metrics.Children), Value(report.Comparison?.Metrics.Children)]);
        summary.Add(["Besuche je Kalendertag", Value(report.Current.Metrics.Average), Value(report.Comparison?.Metrics.Average)]);
        summary.Add(["Berücksichtigte Kalendertage", report.Current.Metrics.CalendarDays, report.Comparison?.Metrics.CalendarDays]);
        if (report.Current.Notice is not null) summary.Add([report.Current.Notice]);
        var sheets = new List<(string Name, List<object?[]> Rows)> { ("Überblick", summary) };
        AddSlice(sheets, report.Current, "Auswahl");
        if (report.Comparison is not null) AddSlice(sheets, report.Comparison, "Vergleich");
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            XNamespace content = "http://schemas.openxmlformats.org/package/2006/content-types";
            var types = new XElement(content + "Types",
                new XElement(content + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(content + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(content + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")));
            for (var i = 1; i <= sheets.Count; i++) types.Add(new XElement(content + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
            Write(zip, "[Content_Types].xml", types);
            XNamespace rels = "http://schemas.openxmlformats.org/package/2006/relationships";
            Write(zip, "_rels/.rels", new XElement(rels + "Relationships", Rel(rels, "rId1", "officeDocument", "xl/workbook.xml")));
            XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Write(zip, "xl/workbook.xml", new XElement(Ns + "workbook", new XAttribute(XNamespace.Xmlns + "r", r), new XElement(Ns + "sheets", sheets.Select((s, i) => new XElement(Ns + "sheet", new XAttribute("name", s.Name), new XAttribute("sheetId", i + 1), new XAttribute(r + "id", $"rId{i + 1}"))))));
            Write(zip, "xl/_rels/workbook.xml.rels", new XElement(rels + "Relationships", sheets.Select((_, i) => Rel(rels, $"rId{i + 1}", "worksheet", $"worksheets/sheet{i + 1}.xml"))));
            for (var i = 0; i < sheets.Count; i++)
            {
                var rows = sheets[i].Rows.Select((row, index) => new XElement(Ns + "row", new XAttribute("r", index + 1), row.Select((cell, column) => Cell(cell, $"{Column(column)}{index + 1}"))));
                Write(zip, $"xl/worksheets/sheet{i + 1}.xml", new XElement(Ns + "worksheet", new XElement(Ns + "cols", new XElement(Ns + "col", new XAttribute("min", 1), new XAttribute("max", 1), new XAttribute("width", 55), new XAttribute("customWidth", 1)), new XElement(Ns + "col", new XAttribute("min", 2), new XAttribute("max", 6), new XAttribute("width", 24), new XAttribute("customWidth", 1))), new XElement(Ns + "sheetData", rows)));
            }
        }
        return stream.ToArray();
    }
    private static void AddSlice(List<(string, List<object?[]>)> sheets, ReportSlice slice, string prefix)
    {
        var rows = new List<object?[]> { new object?[] { "Bereich", "Kategorie", "Besuche aus erhaltenen Einzelanwesenheiten" } };
        foreach (var (label, buckets) in new[] { ("Verlauf", slice.Timeline), ("Wochentag", slice.Weekdays), ("Alter", slice.Ages), ("Geschlecht", slice.Genders), ("Nationalität", slice.Nationalities), ("Standort", slice.Sites) })
            rows.AddRange(buckets.Select(b => new object?[] { label, b.Label, b.Value }));
        sheets.Add(($"{prefix} Diagramme", rows));
        var archive = new List<object?[]> { new object?[] { "Standort", "Monat", "Archivbesuche", "Archivprofile im Monat" }, new object?[] { "Ungefilterte Monatssummen; nicht auf Teilzeiträume oder demografische Filter aufteilbar." } };
        archive.AddRange(slice.Archive.Select(a => new object?[] { a.Site, a.Month.ToString("MM.yyyy"), a.Visits, a.Profiles }));
        sheets.Add(($"{prefix} Archiv", archive));
    }
    private static object Value(object? v) => v ?? "Für diese Auswahl nicht vollständig verfügbar";
    private static XElement Rel(XNamespace ns, string id, string type, string target) => new(ns + "Relationship", new XAttribute("Id", id), new XAttribute("Type", $"http://schemas.openxmlformats.org/officeDocument/2006/relationships/{type}"), new XAttribute("Target", target));
    private static XElement Cell(object? value, string reference) => value is int or long or double or decimal
        ? new XElement(Ns + "c", new XAttribute("r", reference), new XElement(Ns + "v", Convert.ToString(value, CultureInfo.InvariantCulture)))
        : new XElement(Ns + "c", new XAttribute("r", reference), new XAttribute("t", "inlineStr"), new XElement(Ns + "is", new XElement(Ns + "t", value?.ToString() ?? "")));
    private static string Column(int n) { var name = ""; do { name = (char)('A' + n % 26) + name; n = n / 26 - 1; } while (n >= 0); return name; }
    private static void Write(ZipArchive zip, string path, XElement root) { using var stream = zip.CreateEntry(path).Open(); new XDocument(root).Save(stream); }
}
