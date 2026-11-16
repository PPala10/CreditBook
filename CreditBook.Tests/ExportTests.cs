using System.Text;
using System.Text.RegularExpressions;
using CreditBook.Core;
using CreditBook.Storage;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;

internal static class ExportTests
{
    public static void Run(Action<string, Action> test)
    {
        test("CSV preserves Turkish, quoted multiline fields and statement balances", () =>
        {
            var statement = Sample("İşlem, \"ürün\"\r\nİkinci satır");
            var csv = StatementExport.Csv(statement);
            Check(csv[0] == '\uFEFF', "Excel-compatible UTF-8 BOM missing");
            var records = ParseCsv(csv[1..]);
            Check(records.Any(r => r.Contains("Çağrı ŞİMŞEK")), "Customer missing");
            Check(records.Any(r => r.Contains(statement.Rows[0].Description)), "Quoted multiline description changed");
            Check(records.Any(r => r.Contains("Açılış bakiyesi") && r.Contains("10.00")), "Opening balance missing");
            Check(records.Any(r => r.Contains("Kapanış bakiyesi") && r.Contains("35.50")), "Closing balance missing");
            Check(records.Any(r => r.Contains("25.50") && r.Contains("35.50")), "Statement row amounts missing");
        });
        test("CSV neutralizes spreadsheet formulas even behind leading whitespace", () =>
        {
            foreach (var description in new[] { "=1+1", "+SUM(A1)", "-1+2", "@SUM(A1)", "\t=1+1", "\r=1+1", "  =1+1" })
            {
                var rows = ParseCsv(StatementExport.Csv(Sample(description))[1..]);
                Check(rows.Any(r => r.Contains("'" + description)), "Unsafe spreadsheet field: " + description);
            }
        });
        test("PDF embeds a Unicode font for Turkish and lira without installed fonts", () =>
        {
            var bytes = StatementExport.Pdf(Sample("İı Ğğ Şş Öö Üü Çç ₺"));
            Check(Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-", "Invalid PDF signature");
            using var stream = new MemoryStream(bytes);
            using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            Check(pdf.PageCount > 0, "PDF is empty");
            var fonts = pdf.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
            var mappings = new StringBuilder();
            var embedded = false;
            foreach (var entry in fonts.Elements.Values)
            {
                var font = Dereference(entry!);
                if (font.Elements.GetDictionary("/ToUnicode") is { } map)
                    mappings.Append(Encoding.ASCII.GetString(map.Stream.UnfilteredValue));
                var descendants = font.Elements.GetArray("/DescendantFonts");
                if (descendants is null) continue;
                foreach (var item in descendants.Elements)
                    embedded |= Dereference(item).Elements.GetDictionary("/FontDescriptor")?.Elements.ContainsKey("/FontFile2") == true;
            }
            Check(embedded, "Font is not embedded");
            foreach (var code in new[] { "0130", "0131", "011E", "011F", "015E", "015F", "20BA" })
                Check(mappings.ToString().Contains(code, StringComparison.OrdinalIgnoreCase), "Missing Unicode mapping " + code);
        });
        test("PDF paginates long descriptions and many statement rows", () =>
        {
            var sample = Sample("Başlangıç");
            var rows = Enumerable.Range(1, 140).Select(i => new StatementRow(new DateTime(2026, 1, 2), "Borç",
                i == 1 ? string.Concat(Enumerable.Repeat("Çokuzunkesintisizaçıklama", 250)) : $"Satır {i}\nİkinci açıklama", 1, 0, 10 + i, i)).ToArray();
            using var stream = new MemoryStream(StatementExport.Pdf(sample with { Rows = rows, ClosingBalance = 150 }));
            using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            Check(pdf.PageCount > 4, "Long descriptions or rows were truncated");
            Check(pdf.Pages.Cast<PdfPage>().All(p => p.Contents.Elements.Count > 0), "Empty PDF page");
            for (var i = 0; i < pdf.PageCount; i++)
            {
                var text = PdfText(pdf.Pages[i]);
                Check(text.Contains("Hesap ekstresi"), "Repeated page header missing");
                Check(text.Contains($"Sayfa {i + 1} / {pdf.PageCount}"), "Page footer missing");
            }
            var finalText = PdfText(pdf.Pages[pdf.PageCount - 1]);
            Check(finalText.Contains("Satır 140"), "Final statement row missing");
            Check(finalText.Contains("Kapanış bakiyesi: 150,00 ₺"), "PDF closing balance changed");
        });
        test("Empty statement exports opening and closing balances", () =>
        {
            var sample = Sample("") with { Rows = Array.Empty<StatementRow>(), ClosingBalance = 10 };
            Check(StatementExport.Csv(sample).Contains("Kapanış bakiyesi"), "Empty CSV missing totals");
            using var stream = new MemoryStream(StatementExport.Pdf(sample));
            using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            Check(pdf.PageCount == 1, "Empty statement needs one page");
        });
        test("Exports display the warning about unrecorded legacy payment dates", () =>
        {
            const string warning = "Eski kapalı borçların ödeme tarihi bilinmiyor; dönem hesabına dahil edilmedi.";
            var sample = Sample("Satış") with { Warning = warning };
            Check(ParseCsv(StatementExport.Csv(sample)[1..]).Any(r => r.Contains(warning)), "Legacy CSV warning missing");
            using var stream = new MemoryStream(StatementExport.Pdf(sample));
            using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            Check(PdfText(pdf.Pages[0]).Contains("ödeme tarihi bilinmiyor"), "Legacy PDF warning missing");
        });
        test("PDF bounds oversized name phone and warning headers so statement rows still fit", () =>
        {
            var huge = string.Concat(Enumerable.Repeat("Çağrı ŞİMŞEK uzun açıklama ", 1000));
            var sample = Sample("Son satır korunmalı");
            foreach (var statement in new[]
            {
                sample with { Customer = sample.Customer with { Name = huge } },
                sample with { Customer = sample.Customer with { Phone = huge } },
                sample with { Warning = huge }
            })
            {
                using var stream = new MemoryStream(StatementExport.Pdf(statement));
                using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
                Check(pdf.PageCount == 1, "Oversized header prevented statement rows fitting on a page");
                var text = PdfText(pdf.Pages[0]);
                Check(text.Contains("…"), "Truncated header needs a visible ellipsis");
                Check(text.Contains("Son satır korunmalı"), "Oversized header hid the statement row");
                Check(text.Contains("Kapanış bakiyesi: 35,50 ₺"), "Oversized header hid closing balance");
            }
        });
    }

    private static AccountStatement Sample(string description) => new(new CustomerSummary(1, "Çağrı ŞİMŞEK", "05551234567", 100, 35.50m),
        new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), 10, 35.50m,
        new[] { new StatementRow(new DateTime(2026, 1, 2), "Borç", description, 25.50m, 0, 35.50m, 1) });

    private static PdfDictionary Dereference(PdfItem item) => (PdfDictionary)(item is PdfReference reference ? reference.Value : item);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static string PdfText(PdfPage page)
    {
        // Decode the actual embedded glyphs through the PDF's Unicode map, independently
        // of the exporter. This catches omitted rows/totals and invisible Unicode text.
        var mappings = new Dictionary<int, char>();
        var fonts = page.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
        foreach (var item in fonts.Elements.Values)
        {
            if (Dereference(item!).Elements.GetDictionary("/ToUnicode") is not { } map) continue;
            var cmap = Encoding.ASCII.GetString(map.Stream.UnfilteredValue);
            foreach (Match match in Regex.Matches(cmap, @"<([0-9A-Fa-f]{4})><([0-9A-Fa-f]{4})><([0-9A-Fa-f]{4})>"))
            {
                var first = Convert.ToInt32(match.Groups[1].Value, 16);
                var last = Convert.ToInt32(match.Groups[2].Value, 16);
                var unicode = Convert.ToInt32(match.Groups[3].Value, 16);
                for (var glyph = first; glyph <= last; glyph++) mappings[glyph] = (char)(unicode + glyph - first);
            }
        }
        var text = new StringBuilder();
        foreach (var op in ContentReader.ReadContent(page).OfType<COperator>().Where(op => op.OpCode.Name == "Tj"))
        {
            var value = ((CString)op.Operands[0]).Value;
            for (var i = 0; i + 1 < value.Length; i += 2)
                text.Append(mappings.GetValueOrDefault((value[i] << 8) | value[i + 1], '?'));
            text.Append('\n');
        }
        return text.ToString();
    }

    // Parse the result independently, including escaped quotes and CR/LF inside quoted cells.
    private static List<List<string>> ParseCsv(string csv)
    {
        var result = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (c == '"')
            {
                if (quoted && i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (!quoted && (c == ',' || c == '\r' || c == '\n'))
            {
                row.Add(cell.ToString()); cell.Clear();
                if (c != ',')
                {
                    result.Add(row); row = new();
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                }
            }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); result.Add(row); }
        Check(!quoted, "Unterminated CSV quote");
        return result;
    }
}
