using OutSystems.ExternalLibraries.SDK;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Font;
using iText.Kernel.Colors;
using iText.IO.Font.Constants;
using System.Text;
using System.Text.Json;

namespace PdfStamperLibrary;

public class PdfStamper : IPdfStamper
{
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

            // ── Parse ink color (R,G,B in 0-1 range)
            var (r, g, b) = ParseColor(inkColor);

            // ── Open PDF with iText7
            using var inputMs  = new MemoryStream(templatePdf);
            using var outputMs = new MemoryStream();

            var reader  = new PdfReader(inputMs);
            var writer  = new PdfWriter(outputMs);
            var pdfDoc  = new PdfDocument(reader, writer);

            log.AppendLine($"[STEP 4] PDF opened: {pdfDoc.GetNumberOfPages()} pages");

            // iText7 built-in Helvetica — no font files, works on Lambda
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            int stamped = 0;
            int skipped = 0;

            foreach (var field in fields)
            {
                if (!values.TryGetValue(field.Field, out var value) || string.IsNullOrEmpty(value))
                {
                    skipped++;
                    continue;
                }

                // iText7 pages are 1-based
                int pageNum = field.Page + 1;
                if (pageNum < 1 || pageNum > pdfDoc.GetNumberOfPages())
                {
                    log.AppendLine($"  WARN: '{field.Field}' page {field.Page} out of range — skipped");
                    skipped++;
                    continue;
                }

                var page   = pdfDoc.GetPage(pageNum);
                var canvas = new PdfCanvas(page);
                var color  = new DeviceRgb(r, g, b);

                if (field.Type?.ToLower() == "checkbox")
                {
                    bool isChecked = value.Equals("true", StringComparison.OrdinalIgnoreCase)
                                  || value == "1";
                    if (isChecked)
                    {
                        // iText7 coords = PDF spec (origin bottom-left, Y up) — no flip needed
                        canvas.SetFillColor(color)
                              .Rectangle(field.X + 1, field.Y + 1, 7, 7)
                              .Fill();
                        stamped++;
                    }
                }
                else
                {
                    float fontSize = field.FontSize > 0 ? (float)field.FontSize : 9f;

                    canvas.BeginText()
                          .SetFontAndSize(font, fontSize)
                          .SetColor(color, true)
                          .MoveText(field.X, field.Y)
                          .ShowText(value)
                          .EndText();
                    stamped++;
                }

                canvas.Release();
            }

            log.AppendLine($"[STEP 5] Stamped: {stamped} fields, skipped: {skipped}");

            pdfDoc.Close();

            var filledBytes = outputMs.ToArray();
            log.AppendLine($"[STEP 6] Output size: {filledBytes.Length / 1024}KB");

            return new StampResult
            {
                Success       = true,
                Message       = $"Stamped {stamped} fields successfully",
                FilledPdf     = filledBytes,
                FieldsStamped = stamped,
                DetailedLog   = $"SUCCESS | {stamped} stamped | {skipped} skipped | {filledBytes.Length / 1024}KB output"
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

            using var ms     = new MemoryStream(pdfData);
            var reader       = new PdfReader(ms);
            var pdfDoc       = new PdfDocument(reader);
            var firstPage    = pdfDoc.GetPage(1);
            var pageSize     = firstPage.GetPageSize();
            int pageCount    = pdfDoc.GetNumberOfPages();
            pdfDoc.Close();

            return new PDFInfoResult
            {
                Success    = true,
                PageCount  = pageCount,
                PageWidth  = (decimal)pageSize.GetWidth(),
                PageHeight = (decimal)pageSize.GetHeight(),
                Message    = $"{pageCount} pages, {pageSize.GetWidth():F0}x{pageSize.GetHeight():F0}pt"
            };
        }
        catch (Exception ex)
        {
            return new PDFInfoResult { Success = false, Message = ex.Message };
        }
    }

    // ── GetBuildVersion ─────────────────────────────────────────────────────

    public string GetBuildVersion()
    {
        var buildMetadata = "BUILD_METADATA_PLACEHOLDER";
        return $"PdfStamper | {buildMetadata}";
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static (float r, float g, float b) ParseColor(string inkColor)
    {
        if (!string.IsNullOrWhiteSpace(inkColor))
        {
            var parts = inkColor.Split(',');
            if (parts.Length == 3
                && float.TryParse(parts[0].Trim(), out var r)
                && float.TryParse(parts[1].Trim(), out var g)
                && float.TryParse(parts[2].Trim(), out var b))
            {
                if (r <= 1f && g <= 1f && b <= 1f)
                    return (r, g, b);
                return (r / 255f, g / 255f, b / 255f);
            }
        }
        return (0f, 0f, 0f); // default black
    }
}

// ── Internal model ───────────────────────────────────────────────────────────

internal class FieldDefinition
{
    public string Field    { get; set; } = "";
    public int    Page     { get; set; }
    public double X        { get; set; }
    public double Y        { get; set; }
    public string Type     { get; set; } = "text";
    public double MaxWidth { get; set; } = 200;
    public double FontSize { get; set; } = 9;
}
