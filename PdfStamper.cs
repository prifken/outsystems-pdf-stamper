using OutSystems.ExternalLibraries.SDK;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Drawing;
using System.Text;
using System.Text.Json;

namespace PdfStamperLibrary;

public class PdfStamper : IPdfStamper
{
    // Register embedded font resolver once at startup.
    // Lambda has no system fonts — this is required or every XFont call fails.
    static PdfStamper()
    {
        PdfSharpCore.Fonts.GlobalFontSettings.FontResolver = EmbeddedFontResolver.Instance;
    }

    // ── StampPDF ────────────────────────────────────────────────────────────

    public StampResult StampPDF(
        byte[] templatePdf,
        string fieldMapJson,
        string valuesJson,
        string inkColor)
    {
        var log = new StringBuilder();
        log.AppendLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] StampPDF started");

        try
        {
            // ── Validate inputs
            if (templatePdf == null || templatePdf.Length == 0)
                throw new ArgumentException("templatePdf is empty or null");
            if (string.IsNullOrWhiteSpace(fieldMapJson))
                throw new ArgumentException("fieldMapJson is empty");
            if (string.IsNullOrWhiteSpace(valuesJson))
                throw new ArgumentException("valuesJson is empty");

            log.AppendLine($"[STEP 1] Template size: {templatePdf.Length / 1024}KB");

            // ── Parse field map
            var fields = JsonSerializer.Deserialize<List<FieldDefinition>>(
                fieldMapJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new ArgumentException("fieldMapJson could not be parsed");

            log.AppendLine($"[STEP 2] Field map: {fields.Count} fields defined");

            // ── Parse values
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(
                valuesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new ArgumentException("valuesJson could not be parsed");

            log.AppendLine($"[STEP 3] Values: {values.Count} values provided");

            // ── Parse ink color
            var ink = ParseColor(inkColor);

            // ── Open PDF
            using var ms = new MemoryStream(templatePdf);
            var document = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
            log.AppendLine($"[STEP 4] PDF opened: {document.PageCount} pages");

            // ── Stamp each field
            int stamped = 0;
            int skipped = 0;

            foreach (var field in fields)
            {
                if (!values.TryGetValue(field.Field, out var value) || string.IsNullOrEmpty(value))
                {
                    skipped++;
                    continue;
                }

                if (field.Page < 0 || field.Page >= document.PageCount)
                {
                    log.AppendLine($"  WARN: field '{field.Field}' references page {field.Page} but PDF has {document.PageCount} pages — skipped");
                    skipped++;
                    continue;
                }

                var page = document.Pages[field.Page];
                var gfx  = XGraphics.FromPdfPage(page);

                // PDF spec coords (origin bottom-left, Y up) →
                // PdfSharpCore coords (origin top-left, Y down)
                double pdfSharpY = (double)page.Height - field.Y;

                if (field.Type?.ToLower() == "checkbox")
                {
                    bool isChecked = value.Equals("true", StringComparison.OrdinalIgnoreCase)
                                  || value == "1";
                    if (isChecked)
                    {
                        var brush = new XSolidBrush(ink);
                        // Filled 7×7pt square, offset 1pt from checkbox origin for alignment
                        gfx.DrawRectangle(brush, field.X + 1, pdfSharpY - 8, 7, 7);
                        stamped++;
                    }
                    // false checkbox = do nothing (leave the ☐ empty)
                }
                else
                {
                    // Text field
                    double fontSize = field.FontSize > 0 ? field.FontSize : 9;
                    double maxWidth = field.MaxWidth > 0 ? field.MaxWidth : 200;

                    var font  = new XFont("Helvetica", fontSize, XFontStyle.Regular);
                    var brush = new XSolidBrush(ink);

                    // Truncate if too long to avoid overflow
                    var displayValue = TruncateToWidth(gfx, font, value, maxWidth);
                    gfx.DrawString(displayValue, font, brush, new XPoint(field.X, pdfSharpY));
                    stamped++;
                }

                gfx.Dispose();
            }

            log.AppendLine($"[STEP 5] Stamped: {stamped} fields, skipped: {skipped}");

            // ── Save to output stream
            using var outMs = new MemoryStream();
            document.Save(outMs, false);
            var filledBytes = outMs.ToArray();

            log.AppendLine($"[STEP 6] Output size: {filledBytes.Length / 1024}KB");

            // Success: return minimal log (avoid 5.5MB payload limit)
            return new StampResult
            {
                Success      = true,
                Message      = $"Stamped {stamped} fields successfully",
                FilledPdf    = filledBytes,
                FieldsStamped = stamped,
                DetailedLog  = $"SUCCESS | {stamped} stamped | {skipped} skipped | {filledBytes.Length / 1024}KB output"
            };
        }
        catch (Exception ex)
        {
            log.AppendLine($"ERROR: {ex.Message}");
            log.AppendLine($"Stack: {ex.StackTrace}");

            return new StampResult
            {
                Success     = false,
                Message     = ex.Message,
                FilledPdf   = Array.Empty<byte>(),
                DetailedLog = log.ToString()
            };
        }
    }

    // ── GetPDFInfo ──────────────────────────────────────────────────────────

    public PDFInfoResult GetPDFInfo(byte[] pdfData)
    {
        try
        {
            if (pdfData == null || pdfData.Length == 0)
                throw new ArgumentException("pdfData is empty");

            using var ms  = new MemoryStream(pdfData);
            var document  = PdfReader.Open(ms, PdfDocumentOpenMode.ReadOnly);
            var firstPage = document.Pages[0];

            return new PDFInfoResult
            {
                Success    = true,
                PageCount  = document.PageCount,
                PageWidth  = (decimal)firstPage.Width.Point,
                PageHeight = (decimal)firstPage.Height.Point,
                Message    = $"{document.PageCount} pages, {firstPage.Width:F0}x{firstPage.Height:F0}pt"
            };
        }
        catch (Exception ex)
        {
            return new PDFInfoResult
            {
                Success  = false,
                Message  = ex.Message
            };
        }
    }

    // ── GetBuildVersion ─────────────────────────────────────────────────────
    // IMPORTANT: BUILD_METADATA_PLACEHOLDER is replaced by GitHub Actions
    // before compilation. This forces a unique modelDigest on every deploy,
    // which is required for ODC to recognise a new revision.

    public string GetBuildVersion()
    {
        var buildMetadata = "BUILD_METADATA_PLACEHOLDER";
        return $"PdfStamper | {buildMetadata}";
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static XColor ParseColor(string inkColor)
    {
        if (string.IsNullOrWhiteSpace(inkColor))
            return XColors.Black;

        try
        {
            var parts = inkColor.Split(',');
            if (parts.Length == 3
                && double.TryParse(parts[0].Trim(), out var r)
                && double.TryParse(parts[1].Trim(), out var g)
                && double.TryParse(parts[2].Trim(), out var b))
            {
                // Values can be 0-1 (PDF style) or 0-255 (RGB style)
                if (r <= 1 && g <= 1 && b <= 1)
                    return XColor.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
                else
                    return XColor.FromArgb((int)r, (int)g, (int)b);
            }
        }
        catch { /* fall through to default */ }

        return XColors.Black;
    }

    private static string TruncateToWidth(XGraphics gfx, XFont font, string text, double maxWidth)
    {
        if (gfx.MeasureString(text, font).Width <= maxWidth)
            return text;

        // Binary-search trim to fit
        int len = text.Length;
        while (len > 1)
        {
            len--;
            var candidate = text[..len] + "…";
            if (gfx.MeasureString(candidate, font).Width <= maxWidth)
                return candidate;
        }
        return text[..1];
    }
}

// ── Internal model for deserialising field map JSON ──────────────────────────

internal class FieldDefinition
{
    public string Field     { get; set; } = "";
    public int    Page      { get; set; }
    public double X         { get; set; }
    public double Y         { get; set; }        // PDF spec coords (origin bottom-left)
    public string Type      { get; set; } = "text";
    public double MaxWidth  { get; set; } = 200;
    public double FontSize  { get; set; } = 9;
}
