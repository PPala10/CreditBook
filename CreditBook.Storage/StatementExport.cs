using System.Globalization;
using System.Text;
using CreditBook.Core;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace CreditBook.Storage;

/// <summary>Renders the supplied statement without recalculating its ledger balances.</summary>
public static class StatementExport
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly object FontLock = new();
    private const string FontFamily = "CreditBook Noto Sans";

    /// <summary>RFC 4180 CSV, including a UTF-8 BOM character for spreadsheet applications.</summary>
    public static string Csv(AccountStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var csv = new StringBuilder("\uFEFF");
        void Record(params string[] cells) => csv.AppendJoin(',', cells.Select(CsvCell)).Append("\r\n");
        Record("CreditBook hesap ekstresi");
        Record("Müşteri", statement.Customer.Name);
        Record("Telefon", statement.Customer.Phone ?? "");
        Record("Başlangıç", IsoDate(statement.From), "Bitiş", IsoDate(statement.To));
        Record("Para birimi", "TRY (₺)");
        if (!string.IsNullOrWhiteSpace(statement.Warning)) Record("Uyarı", statement.Warning);
        Record("Açılış bakiyesi", Number(statement.OpeningBalance));
        Record("Tarih", "İşlem", "Açıklama", "Borç", "Alacak", "Bakiye", "Kayıt no");
        foreach (var row in statement.Rows)
            Record(IsoDate(row.Date), row.Kind, row.Description, Number(row.Debit), Number(row.Credit), Number(row.Balance), row.RecordId.ToString(CultureInfo.InvariantCulture));
        Record("Dönem toplamı", "", "", Number(statement.Rows.Sum(r => r.Debit)), Number(statement.Rows.Sum(r => r.Credit)));
        Record("Kapanış bakiyesi", Number(statement.ClosingBalance));
        return csv.ToString();
    }

    private static string CsvCell(string value)
    {
        // Quoting alone does not stop Excel from evaluating formulas. Inspect the first
        // non-whitespace character as well as leading control characters.
        var first = value.AsSpan().TrimStart();
        if ((first.Length > 0 && "=+-@".Contains(first[0])) ||
            (value.Length > 0 && (value[0] == '\t' || value[0] == '\r' || value[0] == '\n')))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static byte[] Pdf(AccountStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        lock (FontLock)
        {
            // The bundled font makes exports identical on machines without installed fonts.
            GlobalFontSettings.FontResolver ??= new StatementFontResolver();
        }
        using var document = new PdfDocument();
        document.Info.Title = "CreditBook hesap ekstresi";
        document.Info.Author = "CreditBook";
        document.Info.Subject = $"{statement.Customer.Name} — {IsoDate(statement.From)} / {IsoDate(statement.To)}";
        var font = new XFont(FontFamily, 8.5, XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));
        var titleFont = new XFont(FontFamily, 16, XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));
        var headerFont = new XFont(FontFamily, 10, XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));
        const double margin = 36;
        const double lineHeight = 13;
        var widths = new[] { 61d, 74d, 172d, 72d, 72d, 72d };
        var positions = new double[widths.Length];
        positions[0] = margin;
        for (var i = 1; i < positions.Length; i++) positions[i] = positions[i - 1] + widths[i - 1];
        var tableWidth = widths.Sum();
        XGraphics? graphics = null;
        PdfPage? page = null;
        var y = 0d;
        var pageBottom = 0d;

        void Text(string text, XFont usedFont, double x, double top, double width, bool right = false)
            => graphics!.DrawString(text, usedFont, XBrushes.Black, new XRect(x, top, width, lineHeight + 5),
                right ? XStringFormats.TopRight : XStringFormats.TopLeft);

        void NewPage()
        {
            graphics?.Dispose();
            page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            graphics = XGraphics.FromPdfPage(page);
            pageBottom = page.Height.Point - 65;
            y = margin;
            Text("CreditBook · Hesap ekstresi", titleFont, margin, y, tableWidth); y += 29;
            foreach (var line in Wrap(statement.Customer.Name, headerFont, tableWidth, graphics, 3))
            { Text(line, headerFont, margin, y, tableWidth); y += 15; }
            if (!string.IsNullOrEmpty(statement.Customer.Phone))
            {
                foreach (var line in Wrap("Telefon: " + statement.Customer.Phone, font, tableWidth, graphics, 2))
                { Text(line, font, margin, y, tableWidth); y += lineHeight; }
            }
            Text($"Dönem: {statement.From:dd.MM.yyyy} – {statement.To:dd.MM.yyyy} (dahil)", font, margin, y, tableWidth); y += 18;
            Text($"Açılış bakiyesi: {Money(statement.OpeningBalance)}", headerFont, margin, y, tableWidth); y += 24;
            if (!string.IsNullOrWhiteSpace(statement.Warning))
            {
                foreach (var line in Wrap("Uyarı: " + statement.Warning, font, tableWidth, graphics, 3))
                { Text(line, font, margin, y, tableWidth); y += lineHeight; }
                y += 8;
            }
            graphics.DrawRectangle(XBrushes.LightGray, margin, y, tableWidth, 22);
            var labels = new[] { "Tarih", "İşlem", "Açıklama", "Borç (₺)", "Alacak (₺)", "Bakiye (₺)" };
            for (var i = 0; i < labels.Length; i++) Text(labels[i], font, positions[i] + 3, y + 4, widths[i] - 6, i >= 3);
            y += 26;
        }

        try
        {
            NewPage();
            foreach (var row in statement.Rows)
            {
                var cells = new[] { row.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), row.Kind,
                    row.Description, DisplayNumber(row.Debit), DisplayNumber(row.Credit), DisplayNumber(row.Balance) };
                var lines = cells.Select((cell, i) => Wrap(cell, font, widths[i] - 6, graphics!)).ToArray();
                var count = lines.Max(l => l.Count);
                var offset = 0;
                while (offset < count)
                {
                    var capacity = (int)Math.Floor((pageBottom - y - 7) / lineHeight);
                    if (capacity < 1) { NewPage(); continue; }
                    var take = Math.Min(count - offset, capacity);
                    for (var column = 0; column < cells.Length; column++)
                        for (var line = offset; line < Math.Min(offset + take, lines[column].Count); line++)
                            Text(lines[column][line], font, positions[column] + 3, y + 3 + (line - offset) * lineHeight, widths[column] - 6, column >= 3);
                    y += take * lineHeight + 7;
                    graphics!.DrawLine(XPens.LightGray, margin, y - 2, margin + tableWidth, y - 2);
                    offset += take;
                    if (offset < count) NewPage();
                }
            }
            if (y + 64 > pageBottom) NewPage();
            y += 10;
            Text("Dönem toplamı", headerFont, margin, y, 250);
            Text(DisplayNumber(statement.Rows.Sum(r => r.Debit)), font, positions[3] + 3, y, widths[3] - 6, true);
            Text(DisplayNumber(statement.Rows.Sum(r => r.Credit)), font, positions[4] + 3, y, widths[4] - 6, true);
            y += 24;
            Text("Kapanış bakiyesi: " + Money(statement.ClosingBalance), headerFont, margin, y, tableWidth, true);
            graphics!.Dispose();
            graphics = null;
            for (var index = 0; index < document.PageCount; index++)
            {
                var footerPage = document.Pages[index];
                using var footer = XGraphics.FromPdfPage(footerPage, XGraphicsPdfPageOptions.Append);
                footer.DrawString($"CreditBook · {statement.From:dd.MM.yyyy} – {statement.To:dd.MM.yyyy} · Sayfa {index + 1} / {document.PageCount}",
                    font, XBrushes.Gray, new XRect(margin, footerPage.Height.Point - 34, tableWidth, 15), XStringFormats.TopCenter);
            }
            using var output = new MemoryStream();
            document.Save(output, false);
            return output.ToArray();
        }
        finally { graphics?.Dispose(); }
    }

    private static List<string> Wrap(string text, XFont font, double width, XGraphics graphics, int maxLines = int.MaxValue)
    {
        var result = new List<string>();
        var paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        void MarkTruncated()
        {
            var last = result[^1];
            while (last.Length > 0 && graphics.MeasureString(last + "…", font).Width > width)
            {
                var length = last.Length - 1;
                if (length > 0 && char.IsLowSurrogate(last[length]) && char.IsHighSurrogate(last[length - 1])) length--;
                last = last[..length];
            }
            result[^1] = last.TrimEnd() + "…";
        }
        for (var paragraphIndex = 0; paragraphIndex < paragraphs.Length; paragraphIndex++)
        {
            var paragraph = paragraphs[paragraphIndex];
            if (paragraph.Length == 0)
            {
                result.Add("");
                if (result.Count >= maxLines)
                {
                    if (paragraphIndex + 1 < paragraphs.Length) MarkTruncated();
                    return result;
                }
                continue;
            }
            var remaining = paragraph;
            while (remaining.Length > 0)
            {
                // Binary search also handles a single word longer than its column.
                var low = 1;
                var high = remaining.Length;
                while (low < high)
                {
                    var middle = low + (high - low + 1) / 2;
                    if (graphics.MeasureString(remaining[..middle], font).Width <= width) low = middle;
                    else high = middle - 1;
                }
                var length = low;
                if (length < remaining.Length)
                {
                    var space = remaining.LastIndexOf(' ', length - 1, length);
                    if (space > 0) length = space;
                    // Keep UTF-16 surrogate pairs together at a hard wrap boundary.
                    if (length > 1 && char.IsHighSurrogate(remaining[length - 1]) && char.IsLowSurrogate(remaining[length])) length--;
                }
                result.Add(remaining[..length].TrimEnd());
                remaining = remaining[length..].TrimStart(' ');
                if (result.Count >= maxLines)
                {
                    if (remaining.Length > 0 || paragraphIndex + 1 < paragraphs.Length) MarkTruncated();
                    return result;
                }
            }
        }
        return result;
    }

    private static string IsoDate(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Number(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
    private static string DisplayNumber(decimal amount) => amount.ToString("N2", Turkish);
    private static string Money(decimal amount) => DisplayNumber(amount) + " ₺";

    private sealed class StatementFontResolver : IFontResolver
    {
        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
            => familyName == FontFamily ? new FontResolverInfo("CreditBook.NotoSans.Regular") : null;

        public byte[]? GetFont(string faceName)
        {
            if (faceName != "CreditBook.NotoSans.Regular") return null;
            using var resource = typeof(StatementExport).Assembly.GetManifestResourceStream("CreditBook.Storage.Fonts.NotoSans-Regular.ttf")
                ?? throw new InvalidOperationException("Bundled statement font was not included in the application.");
            using var output = new MemoryStream();
            resource.CopyTo(output);
            return output.ToArray();
        }
    }
}
