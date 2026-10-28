using System.Globalization;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Bms.Application.ExpenseRequests;
using Bms.Domain.ExpenseRequests;

namespace Bms.Infrastructure.Attachments;

/// <summary>No Office processes, macros, HTML, external relationships or formula
/// evaluation. OOXML is a bounded values/text view, not a page-layout renderer.</summary>
public sealed class AttachmentPreviewRenderer : IAttachmentPreviewRenderer
{
    public const string DocumentMediaType = "application/vnd.bms.document-preview+json";
    private const int MaxText = 500_000;
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Excel = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace RelId = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private sealed record Section(string Name, string? Text, string[][]? Rows);
    private sealed record DocumentPreview(string Notice, Section[] Sections);
    public static bool IsMediaTypeAllowed(string value) => value is "application/pdf"
        or "image/png" or "image/jpeg" or "image/gif" or "image/webp"
        or "text/plain; charset=utf-8" or DocumentMediaType;

    public AttachmentPreview Render(string fileName, byte[] original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (original.Length == 0 || original.Length > ExpenseRequestAttachmentUploadPolicy.MaximumFileSizeBytes)
            throw new AttachmentPreviewInvalidException();
        var data = original.AsSpan();
        if (data.StartsWith("%PDF-"u8)) return new(original, "application/pdf");
        if (data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return Image(original, "image/png");
        if (data.StartsWith(new byte[] { 255, 216, 255 })) return Image(original, "image/jpeg");
        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8)) return Image(original, "image/gif");
        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8))
            return Image(original, "image/webp");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is ".txt" or ".csv" or ".log" or ".json" or ".xml")
        {
            if (data.Length > MaxText * 2) throw new AttachmentPreviewUnsupportedException();
            try
            {
                var text = data.StartsWith(new byte[] { 255, 254 })
                    ? new UnicodeEncoding(false, true, true).GetString(original, 2, original.Length - 2)
                    : data.StartsWith(new byte[] { 254, 255 })
                        ? new UnicodeEncoding(true, true, true).GetString(original, 2, original.Length - 2)
                        : new UTF8Encoding(false, true).GetString(original).TrimStart('\uFEFF');
                if (text.Length == 0 || text.Length > MaxText || text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
                    throw new AttachmentPreviewInvalidException();
                return new(Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8");
            }
            catch (DecoderFallbackException) { throw new AttachmentPreviewUnsupportedException(); }
        }
        if (extension is not (".docx" or ".xlsx" or ".docm" or ".xlsm"))
            throw new AttachmentPreviewUnsupportedException();
        try
        {
            using var stream = new MemoryStream(original, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            if (zip.Entries.Count > 2_000 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count)
                throw new AttachmentPreviewInvalidException();
            long expanded = 0;
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                expanded = checked(expanded + entry.Length);
                if (expanded > 40_000_000 || entry.Length > 8_000_000) throw new AttachmentPreviewUnsupportedException();
            }
            var sections = extension is ".docx" or ".docm" ? WordSections(zip, token) : ExcelSections(zip, token);
            var result = JsonSerializer.SerializeToUtf8Bytes(new DocumentPreview(
                "Упрощённый просмотр: без оформления, рисунков и макросов. Формулы не пересчитываются; показаны сохранённые значения. Для полной версии скачайте оригинал.",
                sections), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (result.Length > 4_000_000) throw new AttachmentPreviewUnsupportedException();
            return new(result, DocumentMediaType);
        }
        catch (Exception e) when (e is InvalidDataException or XmlException or OverflowException or ArgumentException)
        { throw new AttachmentPreviewInvalidException(); }
    }

    private static AttachmentPreview Image(byte[] original, string type)
    {
        var data = original.AsSpan(); long width = 0, height = 0;
        if (type == "image/png" && data.Length >= 24 && data.Slice(12, 4).SequenceEqual("IHDR"u8))
        { width = BinaryPrimitives.ReadUInt32BigEndian(data[16..]); height = BinaryPrimitives.ReadUInt32BigEndian(data[20..]); }
        else if (type == "image/gif" && data.Length >= 10)
        { width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]); height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]); }
        else if (type == "image/webp" && data.Length >= 30 && data.Slice(12, 4).SequenceEqual("VP8X"u8))
        { width = 1 + data[24] + (data[25] << 8) + (data[26] << 16); height = 1 + data[27] + (data[28] << 8) + (data[29] << 16); }
        else if (type == "image/webp" && data.Length >= 30 && data.Slice(12, 4).SequenceEqual("VP8 "u8)
            && data.Slice(23, 3).SequenceEqual(new byte[] { 157, 1, 42 }))
        { width = BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3fff; height = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3fff; }
        else if (type == "image/webp" && data.Length >= 25 && data.Slice(12, 4).SequenceEqual("VP8L"u8) && data[20] == 47)
        { var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]); width = (bits & 0x3fff) + 1; height = ((bits >> 14) & 0x3fff) + 1; }
        else if (type == "image/jpeg")
        {
            for (var p = 2; p + 4 < data.Length;)
            {
                if (data[p++] != 255) break;
                while (p < data.Length && data[p] == 255) p++;
                if (p >= data.Length) break;
                var marker = data[p++];
                if (marker is 217 or 218) break;
                if (marker is 1 or >= 208 and <= 215) continue;
                if (p + 2 > data.Length) break;
                var length = BinaryPrimitives.ReadUInt16BigEndian(data[p..]);
                if (length < 2 || p + length > data.Length) break;
                if (marker is >= 192 and <= 207 && marker is not (196 or 200 or 204) && length >= 7)
                { height = BinaryPrimitives.ReadUInt16BigEndian(data[(p + 3)..]); width = BinaryPrimitives.ReadUInt16BigEndian(data[(p + 5)..]); break; }
                p += length;
            }
        }
        if (width <= 0 || height <= 0) throw new AttachmentPreviewInvalidException();
        if (width > 20_000 || height > 20_000 || width * height > 20_000_000) throw new AttachmentPreviewUnsupportedException();
        return new(original, type);
    }

    private static XDocument Xml(ZipArchive zip, string name, CancellationToken token)
    {
        var entry = zip.GetEntry(name) ?? throw new AttachmentPreviewInvalidException();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 4_000_000, MaxCharactersFromEntities = 0
        };
        // Validate complexity with a forward-only reader before allocating an
        // XML tree. Compressed size alone does not bound node/depth expansion.
        using (var scan = entry.Open())
        using (var guard = XmlReader.Create(scan, settings))
        {
            var nodes = 0;
            while (guard.Read())
            {
                token.ThrowIfCancellationRequested();
                if (++nodes > 150_000 || guard.Depth > 64) throw new AttachmentPreviewUnsupportedException();
            }
        }
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, settings);
        token.ThrowIfCancellationRequested();
        return XDocument.Load(reader, LoadOptions.None);
    }

    // OOXML t elements contain text, never nested elements. Do not ask for
    // XElement.Value on attacker-controlled subtrees or concatenate them before
    // checking the budget: nested t nodes would duplicate descendant text.
    private static void AppendOfficeText(StringBuilder output, XElement element, int maximum, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (element.HasElements) throw new AttachmentPreviewUnsupportedException();
        foreach (var node in element.Nodes().OfType<XText>())
        {
            token.ThrowIfCancellationRequested();
            if (node.Value.Length > maximum - output.Length) throw new AttachmentPreviewUnsupportedException();
            output.Append(node.Value);
        }
    }

    private static string ExcelText(XElement? element, int maximum, CancellationToken token)
    {
        var output = new StringBuilder();
        if (element is not null) foreach (var text in element.Descendants(Excel + "t"))
            AppendOfficeText(output, text, maximum, token);
        return output.ToString();
    }

    private static Section[] WordSections(ZipArchive zip, CancellationToken token)
    {
        var document = Xml(zip, "word/document.xml", token);
        var body = document.Root?.Element(Word + "body") ?? throw new AttachmentPreviewInvalidException();
        var text = new StringBuilder();
        // Paragraph order includes table cells. Tabs separate adjacent table cells;
        // no hyperlinks, fields, embedded objects or external content are followed.
        foreach (var paragraph in body.Descendants(Word + "p"))
        {
            token.ThrowIfCancellationRequested();
            foreach (var node in paragraph.Descendants())
            {
                token.ThrowIfCancellationRequested();
                if (node.Name == Word + "t") AppendOfficeText(text, node, MaxText, token);
                else if (node.Name == Word + "tab" || node.Name == Word + "br" || node.Name == Word + "cr")
                {
                    if (text.Length == MaxText) throw new AttachmentPreviewUnsupportedException();
                    text.Append(node.Name == Word + "tab" ? '\t' : '\n');
                }
                if (text.Length > MaxText) throw new AttachmentPreviewUnsupportedException();
            }
            if (Environment.NewLine.Length > MaxText - text.Length) throw new AttachmentPreviewUnsupportedException();
            text.AppendLine();
        }
        return [new Section("Документ", text.ToString(), null)];
    }

    private static Section[] ExcelSections(ZipArchive zip, CancellationToken token)
    {
        var workbook = Xml(zip, "xl/workbook.xml", token);
        var workbookRoot = workbook.Root ?? throw new AttachmentPreviewInvalidException();
        var date1904 = (string?)workbookRoot.Element(Excel + "workbookPr")?.Attribute("date1904") is "1" or "true";
        var relations = Xml(zip, "xl/_rels/workbook.xml.rels", token).Root?.Elements(Relationships + "Relationship")
            .Where(e => (string?)e.Attribute("TargetMode") != "External")
            .ToDictionary(e => (string?)e.Attribute("Id") ?? "", e => (string?)e.Attribute("Target") ?? "")
            ?? throw new AttachmentPreviewInvalidException();
        var sharedItems = new List<string>();
        var sharedCharacters = 0;
        if (zip.GetEntry("xl/sharedStrings.xml") is not null)
        {
            var root = Xml(zip, "xl/sharedStrings.xml", token).Root ?? throw new AttachmentPreviewInvalidException();
            foreach (var item in root.Elements(Excel + "si"))
            {
                var value = ExcelText(item, MaxText - sharedCharacters, token);
                sharedCharacters = checked(sharedCharacters + value.Length);
                sharedItems.Add(value);
            }
        }
        var shared = sharedItems.ToArray();
        var styles = zip.GetEntry("xl/styles.xml") is null ? null : Xml(zip, "xl/styles.xml", token);
        var formats = styles?.Root?.Element(Excel + "numFmts")?.Elements(Excel + "numFmt")
            .ToDictionary(e => (int?)e.Attribute("numFmtId") ?? -1, e => (string?)e.Attribute("formatCode") ?? "") ?? [];
        var xfs = styles?.Root?.Element(Excel + "cellXfs")?.Elements(Excel + "xf")
            .Select(e => (int?)e.Attribute("numFmtId") ?? 0).ToArray() ?? [];
        var sections = new List<Section>();
        int cellCount = 0, characterCount = 0;
        foreach (var sheet in workbookRoot.Element(Excel + "sheets")?.Elements(Excel + "sheet") ?? [])
        {
            token.ThrowIfCancellationRequested();
            if (sections.Count >= 32) throw new AttachmentPreviewUnsupportedException();
            var relationId = (string?)sheet.Attribute(RelId + "id") ?? "";
            if (!relations.TryGetValue(relationId, out var target)) throw new AttachmentPreviewUnsupportedException();
            var path = target.StartsWith("/xl/", StringComparison.Ordinal) ? target[1..] : "xl/" + target;
            // Only worksheet parts; never resolve arbitrary URLs, traversal or
            // external relationships from an untrusted package.
            if (!path.StartsWith("xl/worksheets/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal)
                || path.Contains('\\') || path.Contains(':')) throw new AttachmentPreviewUnsupportedException();
            var rows = new List<string[]>();
            var previousRow = 0;
            foreach (var row in Xml(zip, path, token).Root?.Element(Excel + "sheetData")?.Elements(Excel + "row") ?? [])
            {
                token.ThrowIfCancellationRequested();
                var rowIndex = (int?)row.Attribute("r") ?? previousRow + 1;
                if (rowIndex <= previousRow || rowIndex > 2_000) throw new AttachmentPreviewUnsupportedException();
                while (rows.Count < rowIndex - 1) rows.Add([]);
                previousRow = rowIndex;
                var cells = new List<string>();
                foreach (var cell in row.Elements(Excel + "c"))
                {
                    if (++cellCount > 20_000) throw new AttachmentPreviewUnsupportedException();
                    var address = (string?)cell.Attribute("r") ?? throw new AttachmentPreviewInvalidException();
                    var col = 0; var p = 0;
                    while (p < address.Length && address[p] is >= 'A' and <= 'Z')
                    { col = checked(col * 26 + address[p++] - 'A' + 1); if (col > 100) throw new AttachmentPreviewUnsupportedException(); }
                    if (col <= cells.Count || p == 0 || !int.TryParse(address.AsSpan(p), out var addressRow) || addressRow != rowIndex)
                        throw new AttachmentPreviewInvalidException();
                    while (cells.Count < col - 1) cells.Add("");
                    var value = (string?)cell.Element(Excel + "v") ?? "";
                    var type = (string?)cell.Attribute("t");
                    if (type == "s")
                    {
                        if (!int.TryParse(value, out var index) || index < 0 || index >= shared.Length) throw new AttachmentPreviewInvalidException();
                        value = shared[index];
                    }
                    else if (type == "inlineStr") value = ExcelText(cell.Element(Excel + "is"), MaxText - characterCount, token);
                    else if (type == "b") value = value == "1" ? "Да" : "Нет";
                    else if (type is null or "n")
                    {
                        var style = (int?)cell.Attribute("s") ?? 0;
                        var formatId = style >= 0 && style < xfs.Length ? xfs[style] : 0;
                        value = FormatNumber(value, formatId, formats.GetValueOrDefault(formatId, ""), date1904);
                    }
                    if (value.Length == 0 && cell.Element(Excel + "f") is not null) value = "[формула без сохранённого результата]";
                    characterCount = checked(characterCount + value.Length);
                    if (characterCount > MaxText) throw new AttachmentPreviewUnsupportedException();
                    cells.Add(value);
                }
                rows.Add(cells.ToArray());
            }
            sections.Add(new Section((string?)sheet.Attribute("name") ?? $"Лист {sections.Count + 1}", null, rows.ToArray()));
        }
        if (sections.Count == 0) throw new AttachmentPreviewInvalidException();
        return sections.ToArray();
    }

    private static string FormatNumber(string value, int id, string custom, bool date1904)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) return value;
        // Custom date/financial formats can be ambiguous; do not silently invent
        // currency, percentages or units. The original saved number is retained.
        if (id is >= 14 and <= 22 && number >= 0 && number < 2_958_465)
        {
            try
            {
                var date = DateTime.FromOADate(number + (date1904 ? 1462 : number < 60 ? 1 : 0));
                return date.ToString(id is 18 or 19 or 20 or 21 ? "HH:mm:ss" : id == 22 ? "dd.MM.yyyy HH:mm" : "dd.MM.yyyy", CultureInfo.InvariantCulture);
            }
            catch (ArgumentException) { return value; }
        }
        if (id == 9 || id == 10) return number.ToString("P1", CultureInfo.GetCultureInfo("ru-RU"));
        if (custom.Length != 0) return value;
        return number.ToString("0.################", CultureInfo.GetCultureInfo("ru-RU"));
    }
}
