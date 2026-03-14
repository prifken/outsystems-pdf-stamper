using OutSystems.ExternalLibraries.SDK;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Font;
using iText.Kernel.Colors;
using iText.IO.Font.Constants;
using iText.Forms;
using iText.Forms.Fields;
using System.Text;
using System.Text.Json;
using UglyToad.PdfPig.Core;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;
using PdfPigWord = UglyToad.PdfPig.Content.Word;

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

    // ── StampPDFFromList ────────────────────────────────────────────────────

    public StampResult StampPDFFromList(
        byte[] templatePdf,
        string fieldInstancesJson,
        string inkColor)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fieldInstancesJson))
                throw new ArgumentException("fieldInstancesJson is empty");

            var instances = JsonSerializer.Deserialize<List<FieldInstance>>(
                fieldInstancesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new ArgumentException("fieldInstancesJson could not be parsed");

            // Build fieldMapJson and valuesJson from the same list
            var fieldMapJson = System.Text.Json.JsonSerializer.Serialize(
                instances.Select(f => new FieldDefinition
                {
                    Field    = f.Field,
                    Page     = f.Page,
                    X        = f.X,
                    Y        = f.Y,
                    Type     = f.Type,
                    MaxWidth = f.MaxWidth,
                    FontSize = f.FontSize
                }).ToList());

            var valuesJson = System.Text.Json.JsonSerializer.Serialize(
                instances.ToDictionary(f => f.Field, f => f.Value ?? ""));

            return StampPDF(templatePdf, fieldMapJson, valuesJson, inkColor);
        }
        catch (Exception ex)
        {
            return new StampResult
            {
                Success     = false,
                Message     = ex.Message,
                FilledPdf   = Array.Empty<byte>(),
                DetailedLog = $"StampPDFFromList ERROR: {ex.Message}"
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

    // ── ExtractTextBlocks ───────────────────────────────────────────────────

    public ExtractionResult ExtractTextBlocks(byte[] pdfData, int pageFilter, string textFilter)
    {
        try
        {
            if (pdfData == null || pdfData.Length == 0)
                throw new ArgumentException("pdfData is empty");

            var blocks = new List<object>();

            using var doc = PdfPigDoc.Open(pdfData);
            int pageCount = doc.NumberOfPages;

            int startPage = pageFilter >= 0 ? pageFilter + 1 : 1;
            int endPage   = pageFilter >= 0 ? pageFilter + 1 : pageCount;

            for (int pageNum = startPage; pageNum <= endPage; pageNum++)
            {
                var page = doc.GetPage(pageNum);
                foreach (PdfPigWord word in page.GetWords())
                {
                    if (!string.IsNullOrEmpty(textFilter) &&
                        !word.Text.Contains(textFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    blocks.Add(new
                    {
                        text   = word.Text,
                        page   = pageNum - 1,
                        x      = (int)Math.Round(word.BoundingBox.Left),
                        y      = (int)Math.Round(word.BoundingBox.Bottom),
                        width  = (int)Math.Round(word.BoundingBox.Width),
                        height = (int)Math.Round(word.BoundingBox.Height)
                    });
                }
            }

            return new ExtractionResult
            {
                Success        = true,
                Message        = $"Extracted {blocks.Count} blocks from {pageCount} pages",
                TextBlocksJson = JsonSerializer.Serialize(blocks),
                PageCount      = pageCount,
                BlockCount     = blocks.Count
            };
        }
        catch (Exception ex)
        {
            return new ExtractionResult
            {
                Success        = false,
                Message        = ex.Message,
                TextBlocksJson = "[]",
                PageCount      = 0,
                BlockCount     = 0
            };
        }
    }

    // ── GetAllPageDimensions ─────────────────────────────────────────────────

    public string GetAllPageDimensions(byte[] pdfData)
    {
        try
        {
            if (pdfData == null || pdfData.Length == 0)
                throw new ArgumentException("pdfData is empty");

            using var doc = PdfPigDoc.Open(pdfData);
            int pageCount = doc.NumberOfPages;

            var pages = new List<object>();
            for (int pageNum = 1; pageNum <= pageCount; pageNum++)
            {
                var page = doc.GetPage(pageNum);
                pages.Add(new
                {
                    page      = pageNum - 1,   // 0-indexed
                    widthPts  = (int)Math.Round(page.Width),
                    heightPts = (int)Math.Round(page.Height)
                });
            }

            return JsonSerializer.Serialize(new { pageCount, pages });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { pageCount = 0, pages = Array.Empty<object>(), error = ex.Message });
        }
    }

    // ── ExtractFormFields ────────────────────────────────────────────────────

    public string ExtractFormFields(byte[] pdfData, int pageFilter)
    {
        if (pdfData == null || pdfData.Length == 0)
            return JsonSerializer.Serialize(new { error = "pdfData is empty", fields = Array.Empty<object>() });

        var allFields = new List<RawField>();

        using var doc = PdfPigDoc.Open(pdfData);

        int startPage = pageFilter >= 0 ? pageFilter : 0;
        int endPage   = pageFilter >= 0 ? pageFilter : doc.NumberOfPages - 1;

        for (int pageIdx = startPage; pageIdx <= endPage; pageIdx++)
        {
            if (pageIdx >= doc.NumberOfPages) break;
            var page = doc.GetPage(pageIdx + 1); // PdfPig pages are 1-indexed

            var words    = page.GetWords().ToList();
            var sections = EfBuildSectionTracker(words);

            // ── Classify all paths on this page ──
            var borderedRects = new List<PdfRectangle>();
            var underlines    = new List<PdfRectangle>();
            var checkboxRects = new List<PdfRectangle>();

            foreach (var path in page.ExperimentalAccess.Paths)
            {
                var bbox = path.GetBoundingRectangle();
                if (!bbox.HasValue) continue;
                var r = bbox.Value;
                double w = r.Width, h = r.Height;

                if (w > 400 && h < 3) continue; // section divider — skip

                if (w > 25 && h < 2)
                    underlines.Add(r);
                else if (w > 20 && h >= 6 && h <= 35)
                    borderedRects.Add(r);
                else if (w >= 3 && w <= 18 && h >= 3 && h <= 18)
                {
                    double ar = w / Math.Max(h, 0.01);
                    if (ar >= 0.5 && ar <= 2.0)
                        checkboxRects.Add(r);
                }
            }

            // Pattern A: bordered rectangle input fields
            allFields.AddRange(EfBorderedFields(borderedRects, words, pageIdx, sections));

            // Pattern B: underline fields
            allFields.AddRange(EfUnderlineFields(underlines, words, pageIdx, sections));

            // Pattern C: unicode checkbox glyphs in the text stream
            allFields.AddRange(EfUnicodeCheckboxes(words, pageIdx, sections));

            // Pattern D: geometric drawn checkbox squares
            allFields.AddRange(EfGeometricCheckboxes(checkboxRects, words, pageIdx, sections));
        }

        // Deduplicate: PdfPig emits the same path geometry for both stroke and fill
        // operations, producing identical bounding boxes twice. Keep first occurrence
        // of any field whose page + pattern + coordinates match within 3pt tolerance.
        var deduped = new List<RawField>();
        foreach (var f in allFields)
        {
            bool isDup = deduped.Any(existing =>
                existing.Page    == f.Page    &&
                existing.Pattern == f.Pattern &&
                Math.Abs(existing.X0 - f.X0) < 3.0 &&
                Math.Abs(existing.Y0 - f.Y0) < 3.0);
            if (!isDup)
                deduped.Add(f);
        }

        return JsonSerializer.Serialize(
            deduped.Select(f => new {
                label      = f.Label,
                fieldType  = f.FieldType,
                page       = f.Page,
                x0         = f.X0,
                y0         = f.Y0,
                x1         = f.X1,
                y1         = f.Y1,
                width      = Math.Round(f.X1 - f.X0, 2),
                height     = Math.Round(f.Y1 - f.Y0, 2),
                confidence = f.Confidence,
                pattern    = f.Pattern,
                section    = f.Section,
            }),
            new JsonSerializerOptions { WriteIndented = false });
    }

    // ── DetectFormType ───────────────────────────────────────────────────────

    public string DetectFormType(byte[] pdfData)
    {
        try
        {
            if (pdfData == null || pdfData.Length == 0)
                throw new ArgumentException("pdfData is empty");

            using var ms     = new MemoryStream(pdfData);
            using var reader = new PdfReader(ms);
            using var pdfDoc = new PdfDocument(reader);

            var form = PdfAcroForm.GetAcroForm(pdfDoc, false);

            if (form == null || form.GetAllFormFields().Count == 0)
                return JsonSerializer.Serialize(new {
                    formType    = "flat",
                    hasAcroForm = false,
                    fieldCount  = 0,
                    fields      = Array.Empty<object>()
                });

            var allFormFields = form.GetAllFormFields();

            // Collect all field names so we can filter out XFA parent containers.
            // A container is any field whose name is a path prefix of another field
            // (e.g. "topmostSubform[0].Page1[0]" is a container because
            // "topmostSubform[0].Page1[0].f1_01[0]" exists). Containers share the
            // same bounding box as their first child and are not directly fillable.
            var allNames     = allFormFields.Keys.ToHashSet();
            var containerNames = allNames
                .Where(name => allNames.Any(other => other != name
                    && other.StartsWith(name + ".", StringComparison.Ordinal)))
                .ToHashSet();

            var detectedFields = new List<object>();
            foreach (var kvp in allFormFields)
            {
                if (containerNames.Contains(kvp.Key)) continue; // skip XFA containers

                var field   = kvp.Value;
                var widgets = field.GetWidgets();
                if (widgets == null || widgets.Count == 0) continue;

                var widget  = widgets[0];
                var rectArr = widget.GetRectangle(); // PdfArray: [x0, y0, x1, y1]
                if (rectArr == null) continue;

                float x0 = rectArr.GetAsNumber(0)?.FloatValue() ?? 0;
                float y0 = rectArr.GetAsNumber(1)?.FloatValue() ?? 0;
                float x1 = rectArr.GetAsNumber(2)?.FloatValue() ?? 0;
                float y1 = rectArr.GetAsNumber(3)?.FloatValue() ?? 0;

                var   page    = widget.GetPage();
                int   pageNum = 0;
                if (page != null)
                    try { pageNum = pdfDoc.GetPageNumber(page) - 1; } catch { }

                string fieldType;
                if      (field is PdfSignatureFormField) fieldType = "signature";
                else if (field is PdfButtonFormField)    fieldType = "checkbox";
                else if (field is PdfChoiceFormField)    fieldType = "dropdown";
                else if (field is PdfTextFormField)      fieldType = "text";
                else                                     fieldType = "text";

                detectedFields.Add(new {
                    name         = kvp.Key,
                    fieldType    = fieldType,
                    page         = pageNum,
                    x0           = Math.Round(x0, 2),
                    y0           = Math.Round(y0, 2),
                    x1           = Math.Round(x1, 2),
                    y1           = Math.Round(y1, 2),
                    width        = Math.Round(x1 - x0, 2),
                    height       = Math.Round(y1 - y0, 2),
                    currentValue = field.GetValueAsString() ?? ""
                });
            }

            return JsonSerializer.Serialize(new {
                formType    = "acroform",
                hasAcroForm = true,
                fieldCount  = detectedFields.Count,
                fields      = detectedFields
            }, new JsonSerializerOptions { WriteIndented = false });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new {
                formType    = "flat",
                hasAcroForm = false,
                fieldCount  = 0,
                fields      = Array.Empty<object>(),
                error       = ex.Message
            });
        }
    }

    // ── FillAcroForm ─────────────────────────────────────────────────────────

    public StampResult FillAcroForm(byte[] templatePdf, string valuesJson, bool flatten)
    {
        var log = new StringBuilder();
        try
        {
            if (templatePdf == null || templatePdf.Length == 0)
                throw new ArgumentException("templatePdf is empty");
            if (string.IsNullOrWhiteSpace(valuesJson))
                throw new ArgumentException("valuesJson is empty");

            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(
                valuesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new ArgumentException("valuesJson could not be parsed");

            using var inputMs  = new MemoryStream(templatePdf);
            using var outputMs = new MemoryStream();
            using var reader   = new PdfReader(inputMs);
            using var writer   = new PdfWriter(outputMs);
            using var pdfDoc   = new PdfDocument(reader, writer);

            var form = PdfAcroForm.GetAcroForm(pdfDoc, false)
                       ?? throw new InvalidOperationException("PDF has no AcroForm fields — use StampPDFFromList for flat PDFs");

            var allFields = form.GetAllFormFields();
            int filled = 0, skipped = 0;

            foreach (var kvp in values)
            {
                string key   = kvp.Key;
                string value = kvp.Value;

                // Exact match first
                if (allFields.TryGetValue(key, out var field))
                {
                    FillField(field, value, log);
                    filled++;
                    continue;
                }

                // Partial match — AcroForm names can be deeply nested:
                // e.g., "topmostSubform[0].Page1[0].StreetAddress[0]"
                var partial = allFields.FirstOrDefault(f =>
                    f.Key.EndsWith($".{key}[0]",  StringComparison.OrdinalIgnoreCase) ||
                    f.Key.EndsWith($"[{key}]",    StringComparison.OrdinalIgnoreCase) ||
                    f.Key.Equals(key,             StringComparison.OrdinalIgnoreCase));

                if (partial.Value != null)
                {
                    FillField(partial.Value, value, log);
                    log.AppendLine($"  partial match: {key} → {partial.Key}");
                    filled++;
                }
                else
                {
                    log.AppendLine($"  SKIP: {key} — no matching AcroForm field");
                    skipped++;
                }
            }

            if (flatten)
                form.FlattenFields();

            pdfDoc.Close();

            var filledBytes = outputMs.ToArray();
            return new StampResult {
                Success       = true,
                FilledPdf     = filledBytes,
                FieldsStamped = filled,
                Message       = $"Filled {filled} AcroForm fields" + (flatten ? " (flattened)" : " (editable)") + $", skipped {skipped}",
                DetailedLog   = log.ToString()
            };
        }
        catch (Exception ex)
        {
            return new StampResult {
                Success     = false,
                FilledPdf   = Array.Empty<byte>(),
                Message     = ex.Message,
                DetailedLog = log.ToString()
            };
        }
    }

    private static void FillField(PdfFormField field, string value, StringBuilder log)
    {
        if (field is PdfButtonFormField)
        {
            // Find the "on" appearance state — varies by PDF (Yes, On, X, custom value)
            var states    = field.GetAppearanceStates();
            var onState   = states?.FirstOrDefault(s =>
                !s.Equals("Off", StringComparison.OrdinalIgnoreCase)) ?? "Yes";
            bool isChecked = value.Equals("true", StringComparison.OrdinalIgnoreCase)
                          || value.Equals("yes",  StringComparison.OrdinalIgnoreCase)
                          || value == "1" || value == "X";
            field.SetValue(isChecked ? onState : "Off");
        }
        else
        {
            field.SetValue(value);
        }
        log.AppendLine($"  OK: {field.GetFieldName()} = {value}");
    }

    // ── GetBuildVersion ─────────────────────────────────────────────────────

    public string GetBuildVersion()
    {
        var buildMetadata = "BUILD_METADATA_PLACEHOLDER";
        return $"PdfStamper | {buildMetadata}";
    }

    // ── ExtractFormFields: pattern extractors ────────────────────────────────

    private static List<RawField> EfBorderedFields(
        List<PdfRectangle> rects, List<PdfPigWord> words, int pageIdx,
        List<(double Y, string Name)> sections)
    {
        var fields = new List<RawField>();
        foreach (var r in rects)
        {
            var (label, conf) = EfLabelAboveOrLeft(r.Left, r.Bottom, r.Right, r.Top, words);
            if (label == null) continue;
            fields.Add(new RawField(
                label, EfInferType(label, r.Width, r.Height), pageIdx,
                Math.Round(r.Left, 2), Math.Round(r.Bottom, 2),
                Math.Round(r.Right, 2), Math.Round(r.Top, 2),
                conf, "bordered_rect", EfGetSection(r.Bottom, sections)));
        }
        return fields;
    }

    private const double EfInputHeight = 14.0; // synthetic field height above underline

    private static List<RawField> EfUnderlineFields(
        List<PdfRectangle> underlines, List<PdfPigWord> words, int pageIdx,
        List<(double Y, string Name)> sections)
    {
        var fields = new List<RawField>();
        foreach (var ul in underlines.OrderByDescending(u => u.Bottom))
        {
            if (ul.Width < 30) continue;

            var (label, conf) = EfLabelAboveUnderline(ul.Left, ul.Top, ul.Right, words);
            if (label == null)
                (label, conf) = EfLabelLeftOfUnderline(ul.Left, (ul.Bottom + ul.Top) / 2.0, words);
            if (label == null) continue;

            // Field area is ABOVE the underline (higher Y in PDF space)
            fields.Add(new RawField(
                label, EfInferType(label, ul.Width, EfInputHeight), pageIdx,
                Math.Round(ul.Left, 2), Math.Round(ul.Top, 2),
                Math.Round(ul.Right, 2), Math.Round(ul.Top + EfInputHeight, 2),
                conf, "underline", EfGetSection(ul.Bottom, sections)));
        }
        return fields;
    }

    private static readonly char[] CbChars = { '☐', '☑', '□', '■', '☒' };

    private static List<RawField> EfUnicodeCheckboxes(
        List<PdfPigWord> words, int pageIdx,
        List<(double Y, string Name)> sections)
    {
        var fields = new List<RawField>();
        foreach (var cb in words)
        {
            if (!cb.Text.Any(c => CbChars.Contains(c))) continue;

            double cbMidY  = (cb.BoundingBox.Bottom + cb.BoundingBox.Top) / 2.0;
            double cbRight = cb.BoundingBox.Right;

            // Words to the right on the same line, left-to-right order
            var rightWords = words
                .Where(rw => rw != cb
                    && rw.BoundingBox.Left > cbRight - 1
                    && rw.BoundingBox.Left < cbRight + 200
                    && Math.Abs((rw.BoundingBox.Bottom + rw.BoundingBox.Top) / 2.0 - cbMidY) < 5)
                .OrderBy(rw => rw.BoundingBox.Left)
                .ToList();

            var labelParts = new List<string>();
            foreach (var rw in rightWords)
            {
                if (rw.Text.Any(c => CbChars.Contains(c))) break; // stop at next checkbox glyph
                labelParts.Add(rw.Text);
            }

            string label = labelParts.Count > 0
                ? string.Join(" ", labelParts)
                : "[unlabeled checkbox]";

            fields.Add(new RawField(
                label, "checkbox", pageIdx,
                Math.Round(cb.BoundingBox.Left, 2), Math.Round(cb.BoundingBox.Bottom, 2),
                Math.Round(cb.BoundingBox.Right, 2), Math.Round(cb.BoundingBox.Top, 2),
                labelParts.Count > 0 ? "high" : "low",
                "unicode_checkbox", EfGetSection(cb.BoundingBox.Bottom, sections)));
        }
        return fields;
    }

    private static List<RawField> EfGeometricCheckboxes(
        List<PdfRectangle> rects, List<PdfPigWord> words, int pageIdx,
        List<(double Y, string Name)> sections)
    {
        var fields = new List<RawField>();
        foreach (var cb in rects)
        {
            double cbMidY = (cb.Bottom + cb.Top) / 2.0;
            var labelParts = words
                .Where(w => !w.Text.Any(c => CbChars.Contains(c))
                    && w.BoundingBox.Left > cb.Right + 1
                    && w.BoundingBox.Left < cb.Right + 200
                    && Math.Abs((w.BoundingBox.Bottom + w.BoundingBox.Top) / 2.0 - cbMidY) < 6)
                .OrderBy(w => w.BoundingBox.Left)
                .Take(8)
                .Select(w => w.Text)
                .ToList();

            string label = labelParts.Count > 0
                ? string.Join(" ", labelParts)
                : "[unknown checkbox]";

            fields.Add(new RawField(
                label, "checkbox", pageIdx,
                Math.Round(cb.Left, 2), Math.Round(cb.Bottom, 2),
                Math.Round(cb.Right, 2), Math.Round(cb.Top, 2),
                labelParts.Count > 0 ? "high" : "low",
                "geometric_checkbox", EfGetSection(cb.Bottom, sections)));
        }
        return fields;
    }

    // ── ExtractFormFields: label finders ─────────────────────────────────────

    private static (string? label, string confidence) EfLabelAboveOrLeft(
        double rx0, double ry0, double rx1, double ry1, List<PdfPigWord> words)
    {
        // Strategy 1: nearest text line directly above (gap <= 15pt)
        // "Above" in PDF space = word.Bottom >= rect.Top (ry1) - small gap
        var above = new List<PdfPigWord>();
        foreach (var w in words)
        {
            if (w.Text.Any(c => CbChars.Contains(c))) continue;
            double gap = w.BoundingBox.Bottom - ry1; // positive = word is above rect
            if (gap < -1 || gap > 15) continue;
            if (w.BoundingBox.Right < rx0 - 5 || w.BoundingBox.Left > rx1 + 5) continue;
            above.Add(w);
        }
        if (above.Count > 0)
        {
            var lines = EfGroupIntoLines(above);
            // Sort ascending by Bottom; lines[^1] has highest Bottom = nearest to rect
            lines.Sort((a, b) => a[0].BoundingBox.Bottom.CompareTo(b[0].BoundingBox.Bottom));
            var nearest = lines[^1];
            nearest.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
            return (string.Join(" ", nearest.Select(w => w.Text)), "high");
        }

        // Strategy 2: text to the left at the same vertical midpoint (within 60pt)
        double midY = (ry0 + ry1) / 2.0;
        var left = words
            .Where(w => !w.Text.Any(c => CbChars.Contains(c))
                && Math.Abs((w.BoundingBox.Bottom + w.BoundingBox.Top) / 2.0 - midY) <= 8
                && w.BoundingBox.Right < rx0
                && rx0 - w.BoundingBox.Right < 60)
            .OrderBy(w => w.BoundingBox.Left)
            .ToList();
        if (left.Count > 0)
            return (string.Join(" ", left.Select(w => w.Text)), "medium");

        return (null, "low");
    }

    private static (string? label, string confidence) EfLabelAboveUnderline(
        double ulLeft, double ulTop, double ulRight, List<PdfPigWord> words)
    {
        // Label sits above the underline: word.Bottom >= ulTop - gap, gap <= 20pt
        var candidates = new List<PdfPigWord>();
        foreach (var w in words)
        {
            if (w.Text.Any(c => CbChars.Contains(c))) continue;
            double gap = w.BoundingBox.Bottom - ulTop;
            if (gap < -1 || gap > 20) continue;
            if (w.BoundingBox.Right < ulLeft - 5 || w.BoundingBox.Left > ulRight + 5) continue;
            candidates.Add(w);
        }
        if (candidates.Count == 0) return (null, "low");

        var lines = EfGroupIntoLines(candidates);
        lines.Sort((a, b) => a[0].BoundingBox.Bottom.CompareTo(b[0].BoundingBox.Bottom));
        var nearest = lines[^1]; // highest Bottom = nearest to the underline
        nearest.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        return (string.Join(" ", nearest.Select(w => w.Text)), "high");
    }

    private static (string? label, string confidence) EfLabelLeftOfUnderline(
        double ulLeft, double ulMidY, List<PdfPigWord> words)
    {
        var left = words
            .Where(w => !w.Text.Any(c => CbChars.Contains(c))
                && Math.Abs((w.BoundingBox.Bottom + w.BoundingBox.Top) / 2.0 - ulMidY) <= 10
                && w.BoundingBox.Right < ulLeft
                && ulLeft - w.BoundingBox.Left < 80)
            .OrderBy(w => w.BoundingBox.Left)
            .ToList();
        if (left.Count == 0) return (null, "low");
        return (string.Join(" ", left.Select(w => w.Text)), "medium");
    }

    // ── ExtractFormFields: shared utilities ───────────────────────────────────

    private static List<(double Y, string Name)> EfBuildSectionTracker(List<PdfPigWord> words)
    {
        var sections = new List<(double Y, string Name)>();
        for (int i = 0; i < words.Count; i++)
        {
            var text = words[i].Text;
            if (text != "Item" && text != "Section") continue;
            for (int j = i + 1; j < Math.Min(i + 3, words.Count); j++)
            {
                var digits = words[j].Text.TrimEnd('.').Replace(".", "");
                if (digits.Length > 0 && digits.All(char.IsDigit))
                {
                    sections.Add((words[i].BoundingBox.Bottom, $"{text} {words[j].Text.TrimEnd('.')}"));
                    break;
                }
            }
        }
        return sections;
    }

    /// <summary>
    /// Returns the section header immediately above <paramref name="fieldY"/>.
    /// In PDF space (Y up), "above" = higher Y. Returns the section with the
    /// smallest Y that is still &gt;= fieldY - 5 (i.e., closest header above).
    /// </summary>
    private static string EfGetSection(double fieldY, List<(double Y, string Name)> sections)
    {
        string result = "";
        double closestY = double.MaxValue;
        foreach (var (sy, sname) in sections)
        {
            if (sy >= fieldY - 5 && sy < closestY)
            {
                closestY = sy;
                result = sname;
            }
        }
        return result;
    }

    /// <summary>
    /// Group words into lines by BoundingBox.Bottom proximity (PDF-native Y).
    /// Returned list is sorted ascending by Bottom (lowest Y first).
    /// </summary>
    private static List<List<PdfPigWord>> EfGroupIntoLines(
        List<PdfPigWord> words, double tolerance = 3.0)
    {
        if (words.Count == 0) return new();
        var sorted = words.OrderBy(w => w.BoundingBox.Bottom).ToList();
        var lines = new List<List<PdfPigWord>>();
        var current = new List<PdfPigWord> { sorted[0] };
        for (int i = 1; i < sorted.Count; i++)
        {
            if (Math.Abs(sorted[i].BoundingBox.Bottom - current[0].BoundingBox.Bottom) < tolerance)
                current.Add(sorted[i]);
            else
            {
                lines.Add(current);
                current = new List<PdfPigWord> { sorted[i] };
            }
        }
        lines.Add(current);
        return lines;
    }

    private static string EfInferType(string label, double width, double height)
    {
        // Signature fields are genuinely large: tall (>40pt) AND wide (>150pt).
        // Table cells and header rows are wide but shorter — don't misclassify them.
        if (height > 40 && width > 150) return "signature";
        var low = label.ToLowerInvariant();
        if (low.Contains("signature"))                           return "signature";
        if (low.Contains("date") || low.Contains("mm/dd"))      return "date";
        if (low.Contains('$') || low.Contains("amount")
                              || low.Contains("price"))         return "currency";
        if (low.Contains("phone") || low.Contains("telephone")) return "phone";
        if (low.Contains("zip")   || low.Contains("postal"))    return "zip_code";
        return "text_input";
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

// ── Internal models ──────────────────────────────────────────────────────────

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

// Mirrors the OutSystems FieldInstance structure — used by StampPDFFromList
// so JSON Serialize(Fields) from ODC can be passed directly.
internal class FieldInstance
{
    public string Field     { get; set; } = "";
    public string Label     { get; set; } = "";
    public string Section   { get; set; } = "";
    public int    Page      { get; set; }
    public double X         { get; set; }
    public double Y         { get; set; }
    public string Type      { get; set; } = "text";
    public double MaxWidth  { get; set; } = 200;
    public double FontSize  { get; set; } = 9;
    public string ValueType { get; set; } = "Text";
    public string Value     { get; set; } = "";
}

// Intermediate record used during ExtractFormFields — not exposed to ODC.
// Coordinates are in PDF-native space (bottom-left origin, Y increases upward).
internal sealed record RawField(
    string Label,
    string FieldType,
    int    Page,
    double X0,
    double Y0,
    double X1,
    double Y1,
    string Confidence,
    string Pattern,
    string Section);
